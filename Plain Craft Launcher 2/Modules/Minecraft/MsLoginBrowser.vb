Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.Wpf

''' <summary>
''' 微软登录专用的内嵌浏览器窗口（WebView2 / Edge 内核）。
''' 它会自动截获回调地址（login.live.com/oauth20_desktop.srf）中的授权码，因此玩家不需要手动复制任何链接，
''' 也不会看到微软那个“不要复制或分享此 URL”的提示页。
''' </summary>
Public Class MsLoginBrowser
    Inherits Window

    '截获到的授权码；为 Nothing 表示玩家关闭了窗口
    Public Result As String

    Private ReadOnly Browser As New WebView2()

    Public Sub New(Url As String)
        Title = "微软登录 - PCL"
        Width = 520
        Height = 720
        MinWidth = 420
        MinHeight = 520
        WindowStartupLocation = WindowStartupLocation.CenterOwner
        Background = New MyColor(245, 245, 245)

        Dim Hint As New TextBlock With {
            .Text = "在此窗口登录 Microsoft 帐户。登录完成后窗口会自动关闭。",
            .Margin = New Thickness(16, 12, 16, 12),
            .TextWrapping = TextWrapping.Wrap
        }
        Dim Panel As New DockPanel()
        DockPanel.SetDock(Hint, Dock.Top)
        Panel.Children.Add(Hint)
        Panel.Children.Add(Browser)
        Content = Panel

        AddHandler Browser.NavigationStarting, AddressOf OnNavigationStarting
        AddHandler Browser.NavigationCompleted, AddressOf OnNavigationCompleted
        AddHandler Browser.SourceChanged, AddressOf OnSourceChanged
        AddHandler Loaded, Async Sub() Await InitAsync(Url)
    End Sub

    'WebView2 初始化是异步的，失败时关闭窗口，让调用方退回系统浏览器
    Private Async Function InitAsync(Url As String) As Task
        Try
            Dim DataFolder As String = IO.Path.Combine(PathTemp, "WebView2")
            Dim Env As CoreWebView2Environment = Await CoreWebView2Environment.CreateAsync(Nothing, DataFolder, Nothing)
            Await Browser.EnsureCoreWebView2Async(Env)
            Browser.CoreWebView2.Navigate(Url)
        Catch ex As Exception
            Logger.Warn(ex, "WebView2 初始化失败，将改用系统浏览器登录")
            Close()
        End Try
    End Function

    Private Sub OnNavigationStarting(sender As Object, e As CoreWebView2NavigationStartingEventArgs)
        Logger.Info("微软登录导航：" & MaskUrl(e.Uri))
        If TryCapture(e.Uri) Then e.Cancel = True
    End Sub

    Private Sub OnNavigationCompleted(sender As Object, e As CoreWebView2NavigationCompletedEventArgs)
        TryCapture(Browser.Source)
    End Sub

    Private Sub OnSourceChanged(sender As Object, e As CoreWebView2SourceChangedEventArgs)
        TryCapture(Browser.Source)
    End Sub

    '检查地址里是否带了回调的授权码，有就收下并关闭窗口
    Private Function TryCapture(Address As Uri) As Boolean
        If Address Is Nothing OrElse Result IsNot Nothing Then Return False
        Dim Text As String = Address.AbsoluteUri
        '只认带 code 参数的地址；页面 3 秒后会自动跳转到 ?removed=true，那种地址必须忽略
        If Not Text.Contains("oauth20_desktop.srf") OrElse Not Text.Contains("code=") Then Return False
        Dim Code As String = ParseOAuthCode(Text)
        If String.IsNullOrWhiteSpace(Code) Then Return False
        Result = Code
        Logger.Info($"微软登录：已截获授权码（长度 {Code.Length}）")
        Close()
        Return True
    End Function

    Private Function TryCapture(Address As String) As Boolean
        If String.IsNullOrWhiteSpace(Address) Then Return False
        Return TryCapture(New Uri(Address))
    End Function

    '日志里不记录授权码本身
    Private Shared Function MaskUrl(Address As Uri) As String
        If Address Is Nothing Then Return "(null)"
        Return MaskUrl(Address.AbsoluteUri)
    End Function

    Private Shared Function MaskUrl(Address As String) As String
        If String.IsNullOrEmpty(Address) Then Return "(null)"
        Dim Index As Integer = Address.IndexOf("code=", StringComparison.OrdinalIgnoreCase)
        If Index >= 0 Then Return Address.Substring(0, Index + 5) & "***"
        Return Address
    End Function

End Class
