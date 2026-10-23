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
            If Target.Scheme <> Uri.UriSchemeHttp AndAlso Target.Scheme <> Uri.UriSchemeHttps Then Return "只支持 http/https 地址"
            If Not Settings.Get(Of Boolean)("AgentAllowLocalNet") AndAlso IsPrivateHost(Target.Host) Then
                Return "出于安全考虑，默认禁止访问内网地址（可在设置中放开）"
            End If
            Dim Offset As Integer = If(Args("offset") Is Nothing, 0, Math.Max(0, Args("offset").ToObject(Of Integer)()))
            Dim Limit As Integer = If(Args("limit") Is Nothing, 60000, Math.Max(1000, Args("limit").ToObject(Of Integer)()))
            Limit = Math.Min(Limit, 200000)

            Dim Text As String = FetchRaw(Url, Args)
            If Text Is Nothing Then
                '被 Cloudflare / 反爬拦截时，退回到只读文本代理
                Dim Fallback As String = FetchRaw("https://r.jina.ai/" & Url, Nothing)
                If Fallback IsNot Nothing Then Return Clip(Fallback, Offset, Limit, True)
                Return "抓取失败：目标站点拒绝了请求（可能有人机验证或防爬限制）"
            End If
            Return Clip(Text, Offset, Limit, False)
        Catch ex As Exception
            Return "抓取失败：" & ex.Message
        End Try
    End Function

    Private Function Clip(Text As String, Offset As Integer, Limit As Integer, Proxied As Boolean) As String
        If Offset >= Text.Length Then Return "（偏移超出内容末尾，共 " & Text.Length & " 字符）"
        Dim Take As Integer = Math.Min(Limit, Text.Length - Offset)
        Dim Body As String = Text.Substring(Offset, Take)
        If Proxied Then Body = "（经由文本代理获取）" & vbCrLf & Body
        If Offset + Take < Text.Length Then
            Body &= vbCrLf & "……（未完，共 " & Text.Length & " 字符；继续读请传 offset=" & (Offset + Take) & "）"
        End If
        Return Body
    End Function

    Private Function FetchRaw(Url As String, Args As JObject) As String
        Try
            Dim Method As String = If(Args Is Nothing OrElse Args("method") Is Nothing, "GET", Args("method").ToString().ToUpper())
            Dim Request As HttpWebRequest = CType(WebRequest.Create(New Uri(Url)), HttpWebRequest)
            Request.Method = Method
            Request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
            Request.Accept = "text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8"
            Request.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8")
            Request.Headers.Add("Cache-Control", "no-cache")
            Request.AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate
            Request.Timeout = 25000
            Request.ReadWriteTimeout = 25000
            Request.AllowAutoRedirect = True
            Request.MaximumAutomaticRedirections = 8
            Request.Referer = New Uri(Url).GetLeftPart(UriPartial.Authority)
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
                Return Text
            End Using
        Catch ex As WebException
            Dim Status As Integer = 0
            Try
                If ex.Response IsNot Nothing Then Status = CInt(CType(ex.Response, HttpWebResponse).StatusCode)
            Catch
            End Try
            If Status = 403 OrElse Status = 429 OrElse Status = 503 Then Return Nothing
            Return Nothing
        Catch
            Return Nothing
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
