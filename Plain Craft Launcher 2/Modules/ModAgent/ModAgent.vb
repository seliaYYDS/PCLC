Imports Newtonsoft.Json.Linq

''' <summary>AI 助理的会话、工具循环与请求调度。</summary>
Public Class ModAgent

    Public Shared ReadOnly Instance As New ModAgent()

    Public ReadOnly Raw As New JArray()
    Public IsWaiting As Boolean = False
    Public MaxRounds As Integer = 6
    ''' <summary>高风险操作的确认回调：参数为工具名与说明，返回是否允许。</summary>
    Public ConfirmHandler As Func(Of String, String, Boolean)
    Private ReadOnly Confirmed As New HashSet(Of String)

    Private ReadOnly Sync As New Object()

    ''' <summary>清空会话。</summary>
    Public Sub Clear()
        SyncLock Sync
            Raw.Clear()
            Raw.Add(New JObject From {{"role", "system"}, {"content", BuildSystemPrompt()}})
        End SyncLock
    End Sub

    Private Function BuildSystemPrompt() As String
        Return "你是 PCLC 启动器的助理，名叫小瑟。" & vbCrLf &
               "你帮助用户完成 Minecraft 版本安装、问题排查与日常使用。" & vbCrLf &
               "你可以调用工具获取真实状态（版本列表、日志等），需要信息时先调用工具，不要凭空猜测。" & vbCrLf &
               "回答使用简体中文，简洁直接。"
    End Function

    Public Sub EnsureInit()
        SyncLock Sync
            If Raw.Count = 0 Then
                Raw.Clear()
                Raw.Add(New JObject From {{"role", "system"}, {"content", BuildSystemPrompt()}})
                LoadSession()
            End If
        End SyncLock
    End Sub

    Private Function SessionPath() As String
        Return Paths.Base & "PCL\Agent\session.json"
    End Function

    ''' <summary>把当前会话写入磁盘。</summary>
    Public Sub SaveSession()
        Try
            DirectoryUtils.Create(Paths.Base & "PCL\Agent\")
            IO.File.WriteAllText(SessionPath(), Raw.ToString(Newtonsoft.Json.Formatting.None), System.Text.Encoding.UTF8)
        Catch ex As Exception
            Logger.Warn(ex, "保存 AI 会话失败")
        End Try
    End Sub

    Private Sub LoadSession()
        Try
            Dim Path As String = SessionPath()
            If Not IO.File.Exists(Path) Then Return
            Dim Text As String = IO.File.ReadAllText(Path, System.Text.Encoding.UTF8)
            Dim Loaded = JArray.Parse(Text)
            If Loaded Is Nothing OrElse Loaded.Count = 0 Then Return
            For Each Item In Loaded
                If Item("role")?.ToString() = "system" Then Continue For
                Raw.Add(Item)
            Next
        Catch ex As Exception
            Logger.Warn(ex, "读取 AI 会话失败")
        End Try
    End Sub

    ''' <summary>发送用户消息并跑完工具循环；回调在调用线程触发。</summary>
    Public Sub Send(UserText As String, OnText As Action(Of String), OnTool As Action(Of String), OnDone As Action, OnFail As Action(Of String))
        EnsureInit()
        If IsWaiting Then
            OnFail("上一个请求还在进行中")
            Return
        End If
        IsWaiting = True
        SyncLock Sync
            Raw.Add(New JObject From {{"role", "user"}, {"content", UserText}})
            SaveSession()
        End SyncLock

        Try
            Dim Rounds As Integer = 0
            Do While Rounds < MaxRounds
                Rounds += 1
                Dim Snapshot As JArray
                SyncLock Sync
                    Snapshot = CType(Raw.DeepClone(), JArray)
                End SyncLock

                Dim Message As JObject = ModAgentLlm.ChatStream(Snapshot, ModAgentTools.BuildToolsJson(), Sub(Chunk) OnText(Chunk))
                Dim ToolCalls = TryCast(Message("tool_calls"), JArray)
                If ToolCalls IsNot Nothing AndAlso ToolCalls.Count > 0 Then
                    SyncLock Sync
                        Raw.Add(Message)
                    End SyncLock
                    For Each Item In ToolCalls
                        Dim Name As String = Item("function")("name").ToString()
                        Dim ArgsText As String = Item("function")("arguments")?.ToString()
                        If OnTool IsNot Nothing Then OnTool(Name)
                        Dim Result As String = RunTool(Name, ArgsText)
                        SyncLock Sync
                            Raw.Add(New JObject From {
                                {"role", "tool"},
                                {"tool_call_id", Item("id").ToString()},
                                {"content", Result}
                            })
                        End SyncLock
                    Next
                    Continue Do
                End If

                SyncLock Sync
                    Raw.Add(Message)
                End SyncLock
                IsWaiting = False
                OnDone()
                Return
            Loop
            IsWaiting = False
            OnFail("工具调用次数超过上限（" & MaxRounds & " 轮）")
        Catch ex As Exception
            IsWaiting = False
            OnFail(ex.Message)
        End Try
    End Sub

    '按安全策略判断是否需要确认
    Private Function NeedsConfirm(Tool As AgentTool) As Boolean
        Dim Policy As Integer = Settings.Get(Of Integer)("AgentSafety")
        Select Case Tool.Risk
            Case 0
                Return False
            Case 1
                Return Policy = 0
            Case Else
                If Policy = 2 AndAlso Confirmed.Contains(Tool.Name) Then Return False
                Return True
        End Select
    End Function
    Private Function RunTool(Name As String, ArgsText As String) As String
        Dim Tool As AgentTool = ModAgentTools.FindTool(Name)
        If Tool Is Nothing Then Return "未知工具：" & Name
        Dim Args As JObject
        Try
            Args = If(String.IsNullOrWhiteSpace(ArgsText), New JObject(), JObject.Parse(ArgsText))
        Catch
            Args = New JObject()
        End Try
        Try
            If NeedsConfirm(Tool) Then
                If ConfirmHandler Is Nothing Then Return "需要用户确认后才能执行：" & Name
                If Not ConfirmHandler(Name, ArgsText) Then Return "用户拒绝执行该操作"
                Confirmed.Add(Name)
            End If
            Return Tool.Run(Args)
        Catch ex As Exception
            Return "工具执行失败：" & ex.Message
        End Try
    End Function

End Class
