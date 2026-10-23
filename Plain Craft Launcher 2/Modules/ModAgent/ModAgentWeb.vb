Imports System.Net
Imports System.Text
Imports System.Text.RegularExpressions
Imports Newtonsoft.Json.Linq

''' <summary>web_fetch 工具：抓取网页并转成纯文本。</summary>
Public Module ModAgentWeb

    Public Function Fetch(Args As JObject) As String
        Try
            Dim Url As String = Args("url")?.ToString()
            If String.IsNullOrWhiteSpace(Url) Then Return "缺少 url 参数"
            Url = Url.Trim()
            Dim Target As New Uri(Url)
            If Target.Scheme <> Uri.UriSchemeHttp AndAlso Target.Scheme <> Uri.UriSchemeHttps Then Return "只支持 http/https"
            If Not Settings.Get(Of Boolean)("AgentAllowLocalNet") AndAlso IsPrivateHost(Target.Host) Then
                Return "出于安全考虑，默认禁止访问内网地址（可在设置中放开）"
            End If

            Dim Method As String = If(Args("method") Is Nothing, "GET", Args("method").ToString().ToUpper())
            Dim Request As HttpWebRequest = CType(WebRequest.Create(Target), HttpWebRequest)
            Request.Method = Method
            Request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) PCLC-Agent"
            Request.Timeout = 20000
            Request.ReadWriteTimeout = 20000
            Request.AllowAutoRedirect = True
            Request.MaximumAutomaticRedirections = 5
            If Method = "POST" Then
                Dim Body As String = If(Args("body") Is Nothing, "", Args("body").ToString())
                Dim Data As Byte() = Encoding.UTF8.GetBytes(Body)
                Request.ContentType = "application/json"
                Request.ContentLength = Data.Length
                Using Stream = Request.GetRequestStream()
                    Stream.Write(Data, 0, Data.Length)
                End Using
            End If

            Using Response = CType(Request.GetResponse(), HttpWebResponse)
                Dim Charset As String = Response.CharacterSet
                Dim Encoding As Encoding = If(String.IsNullOrWhiteSpace(Charset), Encoding.UTF8, Encoding.GetEncoding(Charset))
                Dim Text As String
                Using Reader As New IO.StreamReader(Response.GetResponseStream(), Encoding)
                    Text = Reader.ReadToEnd()
                End Using
                Dim ContentType As String = If(Response.ContentType, "")
                If ContentType.Contains("html") Then Text = StripHtml(Text)
                If Text.Length > 20000 Then Text = Text.Substring(0, 20000) & vbCrLf & "……（内容已截断）"
                Return Text
            End Using
        Catch ex As WebException
            Dim Detail As String = ex.Message
            Try
                If ex.Response IsNot Nothing Then
                    Using Reader As New IO.StreamReader(ex.Response.GetResponseStream())
                        Detail &= "：" & Reader.ReadToEnd()
                    End Using
                End If
            Catch
            End Try
            Return "抓取失败：" & Detail
        Catch ex As Exception
            Return "抓取失败：" & ex.Message
        End Try
    End Function

    Private Function StripHtml(Html As String) As String
        Dim Text As String = Regex.Replace(Html, "<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase)
        Text = Regex.Replace(Text, "<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase)
        Text = Regex.Replace(Text, "<!--[\s\S]*?-->", " ")
        Text = Regex.Replace(Text, "<br\s*/?>", vbCrLf, RegexOptions.IgnoreCase)
        Text = Regex.Replace(Text, "</(p|div|li|tr|h[1-6])>", vbCrLf, RegexOptions.IgnoreCase)
        Text = Regex.Replace(Text, "<[^>]+>", "")
        Text = WebUtility.HtmlDecode(Text)
        Text = Regex.Replace(Text, "[ \t]+", " ")
        Text = Regex.Replace(Text, "(\r?\n\s*){3,}", vbCrLf & vbCrLf)
        Return Text.Trim()
    End Function

    Private Function IsPrivateHost(Host As String) As Boolean
        Host = Host.ToLower()
        If Host = "localhost" OrElse Host = "::1" OrElse Host.EndsWith(".local") Then Return True
        Dim Parts As String() = Host.Split("."c)
        If Parts.Length = 4 Then
            Dim First As Integer
            If Integer.TryParse(Parts(0), First) Then
                If First = 10 OrElse First = 127 Then Return True
                If First = 192 AndAlso Parts(1) = "168" Then Return True
                If First = 172 Then
                    Dim Second As Integer
                    If Integer.TryParse(Parts(1), Second) AndAlso Second >= 16 AndAlso Second <= 31 Then Return True
                End If
            End If
        End If
        Return False
    End Function

End Module
