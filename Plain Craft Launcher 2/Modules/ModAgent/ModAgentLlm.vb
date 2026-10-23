Imports System.IO
Imports System.Net
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>OpenAI 兼容的对话请求（带工具调用）。</summary>
Public Module ModAgentLlm

    Private CurrentRequest As HttpWebRequest = Nothing
    Private CurrentThread As Threading.Thread = Nothing

    Public ReadOnly Property IsBusy As Boolean
        Get
            Return CurrentThread IsNot Nothing AndAlso CurrentThread.IsAlive
        End Get
    End Property

    Public Sub Cancel()
        Try
            If CurrentRequest IsNot Nothing Then CurrentRequest.Abort()
        Catch
        End Try
        CurrentThread = Nothing
    End Sub

    ''' <summary>发起一次对话请求，返回 message 对象（可能包含 tool_calls）。</summary>
    Public Function ChatOnce(Messages As JArray, Tools As JArray) As JObject
        Dim BaseUrl As String = ModAgentProviders.CurrentBaseUrl()
        Dim Model As String = ModAgentProviders.CurrentModel()
        Dim Key As String = Settings.Get(Of String)("AgentApiKey")
        If String.IsNullOrWhiteSpace(BaseUrl) Then Throw New Exception("尚未配置接口地址")
        If String.IsNullOrWhiteSpace(Model) Then Throw New Exception("尚未配置模型名")

        Dim Body As New JObject From {
            {"model", Model},
            {"messages", Messages},
            {"temperature", 0.3}
        }
        If Tools IsNot Nothing AndAlso Tools.Count > 0 Then Body("tools") = Tools

        Dim Request As HttpWebRequest = CType(WebRequest.Create(BaseUrl & "/chat/completions"), HttpWebRequest)
        CurrentRequest = Request
        Request.Method = "POST"
        Request.ContentType = "application/json"
        Request.Timeout = 120000
        Request.ReadWriteTimeout = 120000
        If Not String.IsNullOrWhiteSpace(Key) Then Request.Headers.Add("Authorization", "Bearer " & Key)

        Dim Data As Byte() = Encoding.UTF8.GetBytes(Body.ToString(Newtonsoft.Json.Formatting.None))
        Request.ContentLength = Data.Length
        Using Stream = Request.GetRequestStream()
            Stream.Write(Data, 0, Data.Length)
        End Using

        Try
            Using Response = CType(Request.GetResponse(), HttpWebResponse)
                Using Reader As New StreamReader(Response.GetResponseStream(), Encoding.UTF8)
                    Dim Text As String = Reader.ReadToEnd()
                    Dim Json As JObject = JObject.Parse(Text)
                    Return CType(Json("choices")(0)("message"), JObject)
                End Using
            End Using
        Catch ex As WebException
            Dim Detail As String = ex.Message
            If ex.Response IsNot Nothing Then
                Using Reader As New StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8)
                    Dim Text As String = Reader.ReadToEnd()
                    If Text.Length > 400 Then Text = Text.Substring(0, 400)
                    Detail &= "：" & Text
                End Using
            End If
            Throw New Exception(Detail)
        Finally
            CurrentRequest = Nothing
        End Try
    End Function

End Module
