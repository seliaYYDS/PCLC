Imports System.IO
Imports System.Net
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>OpenAI 兼容的流式对话请求（支持工具调用）。</summary>
Public Module ModAgentLlm

    Private CurrentRequest As HttpWebRequest = Nothing

    Public Sub Cancel()
        Try
            If CurrentRequest IsNot Nothing Then CurrentRequest.Abort()
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' 发起一次流式对话，文本增量通过 OnDelta 回调；返回完整的 message 对象（可能含 tool_calls）。
    ''' </summary>
    Public Function ChatStream(Messages As JArray, Tools As JArray, OnDelta As Action(Of String)) As JObject
        Dim BaseUrl As String = ModAgentProviders.CurrentBaseUrl()
        Dim Model As String = ModAgentProviders.CurrentModel()
        Dim Key As String = Settings.Get(Of String)("AgentApiKey")
        If String.IsNullOrWhiteSpace(BaseUrl) Then Throw New Exception("尚未配置接口地址")
        If String.IsNullOrWhiteSpace(Model) Then Throw New Exception("尚未配置模型名")

        Dim Body As New JObject From {
            {"model", Model},
            {"messages", Messages},
            {"temperature", 0.3},
            {"stream", True}
        }
        If Tools IsNot Nothing AndAlso Tools.Count > 0 Then Body("tools") = Tools

        Dim Request As HttpWebRequest = CType(WebRequest.Create(BaseUrl & "/chat/completions"), HttpWebRequest)
        CurrentRequest = Request
        Request.Method = "POST"
        Request.ContentType = "application/json"
        Request.Accept = "text/event-stream"
        Request.Timeout = 120000
        Request.ReadWriteTimeout = 120000
        Request.AllowReadStreamBuffering = False
        If Not String.IsNullOrWhiteSpace(Key) Then Request.Headers.Add("Authorization", "Bearer " & Key)

        Dim Data As Byte() = Encoding.UTF8.GetBytes(Body.ToString(Newtonsoft.Json.Formatting.None))
        Request.ContentLength = Data.Length
        Using Stream = Request.GetRequestStream()
            Stream.Write(Data, 0, Data.Length)
        End Using

        Dim Content As New StringBuilder()
        Dim ToolCalls As New JObject()
        Dim ChunkCount As Integer = 0
        Dim Watch As Stopwatch = Stopwatch.StartNew()

        Try
            Using Response = CType(Request.GetResponse(), HttpWebResponse)
                Dim ContentType As String = If(Response.ContentType, "")
                Using Reader As New StreamReader(Response.GetResponseStream(), Encoding.UTF8)
                    Dim FirstLine As String = Nothing
                    Do While True
                        Dim Line As String = Reader.ReadLine()
                        If Line Is Nothing Then Exit Do
                        If String.IsNullOrWhiteSpace(Line) Then Continue Do
                        FirstLine = Line
                        Exit Do
                    Loop
                    Dim IsSse As Boolean = FirstLine IsNot Nothing AndAlso FirstLine.TrimStart().StartsWith("data:")
                    Logger.Info($"[AgentLlm] contentType={ContentType} sse={IsSse}")
                    If Not IsSse Then
                        Dim RawBody As String = If(FirstLine, "") & vbLf & Reader.ReadToEnd()
                        If Not String.IsNullOrWhiteSpace(RawBody) Then
                            Dim JsonAll As JObject = JObject.Parse(RawBody)
                            Dim Msg = TryCast(JsonAll("choices")?.Item(0)?("message"), JObject)
                            If Msg IsNot Nothing Then
                                Dim Full As String = ExtractText(Msg("content"))
                                If Not String.IsNullOrEmpty(Full) Then
                                    Content.Append(Full)
                                    ChunkCount += 1
                                    OnDelta(Full)
                                End If
                                Dim CallsAll = TryCast(Msg("tool_calls"), JArray)
                                If CallsAll IsNot Nothing Then
                                    For Each Item In CallsAll
                                        Dim Idx As String = ToolCalls.Count.ToString()
                                        ToolCalls(Idx) = New JObject From {
                                            {"id", Item("id")?.ToString()},
                                            {"name", Item("function")?("name")?.ToString()},
                                            {"arguments", Item("function")?("arguments")?.ToString()}}
                                    Next
                                End If
                            End If
                        End If
                    Else
                        Dim Pending As String = FirstLine
                        Do While True
                            Dim Line As String
                            If Pending IsNot Nothing Then
                                Line = Pending
                                Pending = Nothing
                            Else
                                Line = Reader.ReadLine()
                            End If
                            If Line Is Nothing Then Exit Do
                            If Not Line.TrimStart().StartsWith("data:") Then Continue Do
                            Dim Payload As String = Line.TrimStart().Substring(5).Trim()
                            If Payload = "[DONE]" Then Exit Do
                            If Payload.Length = 0 Then Continue Do
                            Dim Json As JObject
                            Try
                                Json = JObject.Parse(Payload)
                            Catch
                                Continue Do
                            End Try
                            Dim Choices = TryCast(Json("choices"), JArray)
                            If Choices Is Nothing OrElse Choices.Count = 0 Then Continue Do
                            Dim Delta = TryCast(Choices(0)("delta"), JObject)
                            If Delta Is Nothing Then Continue Do

                            Dim Text As String = ExtractText(Delta("content"))
                            If Not String.IsNullOrEmpty(Text) Then
                                Content.Append(Text)
                                ChunkCount += 1
                                OnDelta(Text)
                            End If

                            Dim Calls = TryCast(Delta("tool_calls"), JArray)
                            If Calls IsNot Nothing Then
                                For Each Item In CType(Calls, JArray)
                                    Dim Index As String = If(Item("index") Is Nothing, "0", Item("index").ToString())
                                    If ToolCalls(Index) Is Nothing Then
                                        ToolCalls(Index) = New JObject From {{"id", ""}, {"name", ""}, {"arguments", ""}}
                                    End If
                                    Dim Entry = CType(ToolCalls(Index), JObject)
                                    If Item("id") IsNot Nothing Then Entry("id") = Item("id").ToString()
                                    Dim FunctionBody = TryCast(Item("function"), JObject)
                                    If FunctionBody IsNot Nothing Then
                                        If FunctionBody("name") IsNot Nothing Then Entry("name") = Entry("name").ToString() & FunctionBody("name").ToString()
                                        If FunctionBody("arguments") IsNot Nothing Then Entry("arguments") = Entry("arguments").ToString() & FunctionBody("arguments").ToString()
                                    End If
                                Next
                            End If
                        Loop
                    End If
                End Using
            End Using
        Catch ex As WebException
            Dim Detail As String = ex.Message
            Try
                If ex.Response IsNot Nothing Then
                    Using Reader As New StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8)
                        Dim Text As String = Reader.ReadToEnd()
                        If Text.Length > 400 Then Text = Text.Substring(0, 400)
                        Detail &= "：" & Text
                    End Using
                End If
            Catch
            End Try
            Throw New Exception(Detail)
        Finally
            CurrentRequest = Nothing
        End Try

        Watch.Stop()
        Logger.Info($"[AgentLlm] chunks={ChunkCount} content={Content.Length} tools={ToolCalls.Count} elapsed={Watch.ElapsedMilliseconds}ms")
        Return BuildMessage(Content, ToolCalls)
    End Function

    Private Function BuildMessage(Content As StringBuilder, ToolCalls As JObject) As JObject
        Dim Message As New JObject From {{"role", "assistant"}, {"content", Content.ToString()}}
        If ToolCalls.Count > 0 Then
            Dim Array As New JArray()
            For Each Prop As JProperty In ToolCalls.Properties()
                Dim Entry = CType(Prop.Value, JObject)
                Array.Add(New JObject From {
                    {"id", Entry("id").ToString()},
                    {"type", "function"},
                    {"function", New JObject From {
                        {"name", Entry("name").ToString()},
                        {"arguments", Entry("arguments").ToString()}
                    }}
                })
            Next
            Message("tool_calls") = Array
        End If
        Return Message
    End Function


    Private Function ExtractText(Token As JToken) As String
        If Token Is Nothing Then Return ""
        If Token.Type = JTokenType.Array Then
            Dim Builder As New StringBuilder()
            For Each Part In CType(Token, JArray)
                If Part.Type = JTokenType.Object Then
                    Builder.Append(If(Part("text")?.ToString(), ""))
                Else
                    Builder.Append(Part.ToString())
                End If
            Next
            Return Builder.ToString()
        End If
        Return Token.ToString()
    End Function

End Module