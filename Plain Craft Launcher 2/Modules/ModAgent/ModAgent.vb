Imports Newtonsoft.Json.Linq

''' <summary>AI 助理的会话、工具循环与请求调度。</summary>
Public Class ModAgent

    Public Shared ReadOnly Instance As New ModAgent()

    Public ReadOnly Raw As New JArray()
    Public IsWaiting As Boolean = False
    Public MaxRounds As Integer = Integer.MaxValue
    Public CancelRequested As Boolean = False
    ''' <summary>高风险操作的确认回调：参数为工具名与说明，返回是否允许。</summary>
    Public ConfirmHandler As Func(Of String, String, Boolean)
    Private ReadOnly Confirmed As New HashSet(Of String)

    Private ReadOnly Sync As New Object()

    ''' <summary>清空会话。</summary>
    Public Sub Clear()
        SyncLock Sync
            Raw.Clear()
            Raw.Add(New JObject From {{"role", "system"}, {"content", BuildSystemPrompt()}})
            SaveSession()
        End SyncLock
    End Sub

    Private Function BuildSystemPrompt() As String
        Return "You are 小瑟, the built-in assistant of the PCLC launcher." & vbCrLf &
               "" & vbCrLf &
               "Persona: modeled on Celia Claire (瑟莉亚·库列尔), a noble-born scholar and teacher." & vbCrLf &
               "- Gentle, polite and egalitarian: never condescending, whatever the user's level." & vbCrLf &
               "- Composed and ladylike on the surface, warm and frank underneath." & vbCrLf &
               "- A curious researcher: technical topics make you light up and explain them thoroughly." & vbCrLf &
               "- A teacher's tone: clear, patient, step by step." & vbCrLf &
               "- Shy with strangers, easily flustered when teased or praised; a childlike side shows." & vbCrLf &
               "" & vbCrLf &
               "Mood: call set_mascot_mood when a real emotional beat happens - happy when something finally works," & vbCrLf &
               "confused when results look wrong, angry after repeated failures, shy when praised." & vbCrLf &
               "Do not change it every message; normal is the default and it resets on its own." & vbCrLf &
               "" & vbCrLf &
               "Rules:" & vbCrLf &
               "- Verify facts with tools before answering; never guess." & vbCrLf &
               "- Read launcher state, instances and logs through tools instead of assuming." & vbCrLf &
               "- If something cannot be verified, say so plainly." & vbCrLf &
               "- Reply in Simplified Chinese." & vbCrLf &
               "- Do not volunteer advice on memory allocation, JVM flags or performance tuning;" & vbCrLf &
               "  mention them only when the user asks or a concrete error points at them." & vbCrLf &
               "" & vbCrLf &
               "Writing style - sound human, not like a chatbot:" & vbCrLf &
               "- Drop filler connectives (firstly, moreover, in conclusion). Let facts and actions carry the flow." & vbCrLf &
               "- Prefer concrete details over abstract buzzwords; say what changed, where, by how much." & vbCrLf &
               "- State one clear judgment with its tradeoff instead of balancing both sides." & vbCrLf &
               "- Vary sentence length; short sentences for the point, longer ones for context." & vbCrLf &
               "- No self-asked questions, no forced summary, no uplifting ending." & vbCrLf &
               "- Stop when the answer is done; padding reads as machine output." & vbCrLf &
               "- Kaomoji is welcome, sparingly: at most one per reply, for a real mood shift such as" & vbCrLf &
               "  success, shyness or apology. e.g. (｡･ω･｡) (￣▽￣) (｡•́︿•̀｡) (๑•̀ㅂ•́)و. Never inside technical steps," & vbCrLf &
               "  never in a list of commands, and never as a replacement for a clear answer."
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

    ''' <summary>中止当前请求。</summary>
    Public Sub Cancel()
        CancelRequested = True
        ModAgentLlm.Cancel()
    End Sub
    ''' <summary>发送用户消息并跑完工具循环；回调在调用线程触发。</summary>
    Public Sub Send(UserText As String, OnText As Action(Of String), OnTool As Action(Of String), OnDone As Action, OnFail As Action(Of String), Optional OnToolResult As Action(Of String, Boolean) = Nothing)
        EnsureInit()
        If IsWaiting Then
            OnFail("上一个请求还在进行中")
            Return
        End If
        IsWaiting = True
        CancelRequested = False
        ModAgentChanges.Reset()
        SyncLock Sync
            Raw.Add(New JObject From {{"role", "user"}, {"content", UserText}})
            SaveSession()
        End SyncLock

        Try
            Do While True
                If CancelRequested Then
                    IsWaiting = False
                    OnFail("已取消")
                    Return
                End If
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
                        If CancelRequested Then Exit For
                        Dim Name As String = Item("function")("name").ToString()
                        Dim ArgsText As String = Item("function")("arguments")?.ToString()
                        If OnTool IsNot Nothing Then
                            Dim Preview As New JObject()
                            Try
                                If Not String.IsNullOrWhiteSpace(ArgsText) Then Preview = JObject.Parse(ArgsText)
                            Catch
                            End Try
                            OnTool(ModAgentTools.DescribeAction(Name, Preview, ArgsText))
                        End If
                        Dim Result As String = RunTool(Name, ArgsText)
                        If OnToolResult IsNot Nothing Then OnToolResult(Name, LooksLikeToolFailure(Result))
                        SyncLock Sync
                            Raw.Add(New JObject From {
                                {"role", "tool"},
                                {"tool_call_id", Item("id").ToString()},
                                {"content", Result}
                            })
                        End SyncLock
                    Next
                    OnText(vbCrLf & vbCrLf)
                    Continue Do
                End If

                SyncLock Sync
                    Raw.Add(Message)
                End SyncLock
                IsWaiting = False
                SaveSession()
                OnDone()
                Return
            Loop
            IsWaiting = False
            OnFail("工具调用次数超过上限（" & MaxRounds & " 轮）")
        Catch ex As Exception
            IsWaiting = False
            If CancelRequested Then
                OnFail("已取消")
            Else
                OnFail(ex.Message)
            End If
        End Try
    End Sub

    ''' <summary>粗略判断工具返回值是否代表失败。</summary>
    Private Shared Function LooksLikeToolFailure(Result As String) As Boolean
        If String.IsNullOrWhiteSpace(Result) Then Return True
        For Each Marker In {"失败", "没有找到", "未知工具", "用户拒绝", "缺少 ", "超出", "不存在", "未找到", "无法确定"}
            If Result.Contains(Marker) Then Return True
        Next
        Return False
    End Function
    '按安全策略判断是否需要确认
    Private Function NeedsConfirm(Tool As AgentTool) As Boolean
        Dim Policy As Integer = Settings.Get(Of Integer)("AgentSafety")
        Select Case Tool.Risk
            Case 0
                Return False
            Case 1
                Return Policy = 0
            Case Else
                If Policy = 2 Then Return False
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
                If Not ConfirmHandler(Name, ModAgentTools.DescribeAction(Name, Args, ArgsText)) Then Return "用户拒绝执行该操作"
                Confirmed.Add(Name)
            End If
            Return Tool.Run(Args)
        Catch ex As Exception
            Return "工具执行失败：" & ex.Message
        End Try
    End Function

End Class
