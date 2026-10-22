'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "OpenSource"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLDebug"
    '用于微软登录的 ClientId。默认使用 Minecraft 官方启动器公开的 Live 应用 ID，无需自行注册 Azure 应用
    Public OAuthClientId As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), "00000000402b5328")
    '微软登录使用的 OAuth 权限范围（Live 版）
    Public Const MsOAuthScope As String = "service::user.auth.xboxlive.com::MBI_SSL"
    '微软登录的固定回调地址（由 Live 应用预先注册，无需本地监听）
    Public Const MsRedirectUri As String = "https://login.live.com/oauth20_desktop.srf"
    'CurseForge API Key
    Public CurseForgeAPIKey As String = If(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), "")
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

#Region "网络鉴权"

    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    '主色：决定主要控件与标题栏基调
    Public ColorHue As Integer = 228, ColorSat As Integer = 45, ColorLightAdjust As Integer = -2
    '副色：决定浅色阶与标题栏渐变的另一端
    Public ColorHue2 As Integer = 255, ColorSat2 As Integer = 35, ColorLightAdjust2 As Integer = 10
    '兼容旧字段
    Public ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0

    ''' <summary>把颜色换算成主色阶使用的 HSL（H 0-360，S/L 0-100）。</summary>
    Friend Sub MyColorToHsl(Col As MyColor, ByRef Hue As Integer, ByRef Sat As Integer, ByRef Light As Integer)
        Dim R As Double = Col.R / 255, G As Double = Col.G / 255, B As Double = Col.B / 255
        Dim Max As Double = Math.Max(R, Math.Max(G, B))
        Dim Min As Double = Math.Min(R, Math.Min(G, B))
        Dim L As Double = (Max + Min) / 2
        Dim H As Double = 0, S As Double = 0
        If Max <> Min Then
            Dim D As Double = Max - Min
            S = If(L > 0.5, D / (2 - Max - Min), D / (Max + Min))
            Select Case Max
                Case R : H = (G - B) / D + If(G < B, 6, 0)
                Case G : H = (B - R) / D + 2
                Case Else : H = (R - G) / D + 4
            End Select
            H *= 60
        End If
        Hue = CInt(Math.Round(H))
        Sat = CInt(Math.Round(S * 100))
        Light = CInt(Math.Round(L * 100))
    End Sub
    ''' <summary>解析 H,S,L 形式的字符串，失败返回 False。</summary>
    Friend Function TryParseHsl(Text As String, ByRef Hue As Integer, ByRef Sat As Integer, ByRef Light As Integer) As Boolean
        Try
            Dim Parts = Text.Split(","c)
            If Parts.Length <> 3 Then Return False
            Hue = CInt(Parts(0))
            Sat = CInt(Parts(1))
            Light = CInt(Parts(2))
            Return True
        Catch
            Return False
        End Try
    End Function
    '主题配色表：每项为 主色 H,S,L 与 副色 H,S,L
    Private ReadOnly ThemePairs As New Dictionary(Of Integer, Integer()) From {
        {0, {210, 85, 45, 195, 75, 58}},
        {1, {172, 55, 45, 150, 50, 58}},
        {2, {128, 45, 45, 95, 45, 56}},
        {3, {45, 75, 45, 28, 80, 56}},
        {4, {28, 45, 45, 16, 40, 58}},
        {5, {210, 6, 45, 210, 4, 56}},
        {6, {340, 60, 45, 318, 55, 58}},
        {7, {275, 50, 45, 252, 45, 58}},
        {8, {40, 60, 45, 22, 55, 58}},
        {9, {22, 75, 45, 8, 70, 58}},
        {10, {358, 65, 45, 338, 60, 58}},
        {11, {215, 90, 43, 190, 85, 56}},
        {12, {190, 80, 45, 45, 90, 52}},
        {13, {45, 90, 45, 12, 85, 56}},
        {15, {228, 45, 43, 255, 35, 58}}
    }

    ''' <summary>读取某个主题的主色与副色（H,S,L）。自定义主题从设置读取，失败返回 False。</summary>
    Friend Function GetThemeColors(ThemeId As Integer,
                                   ByRef H1 As Integer, ByRef S1 As Integer, ByRef L1 As Integer,
                                   ByRef H2 As Integer, ByRef S2 As Integer, ByRef L2 As Integer) As Boolean
        If ThemeId = 14 Then
            If Not TryParseHsl(Settings.Get(Of String)("UiCustomPrimary"), H1, S1, L1) Then Return False
            If Not TryParseHsl(Settings.Get(Of String)("UiCustomSecondary"), H2, S2, L2) Then Return False
            Return True
        End If
        Dim Pair As Integer() = Nothing
        If Not ThemePairs.TryGetValue(ThemeId, Pair) Then Return False
        H1 = Pair(0)
        S1 = Pair(1)
        L1 = Pair(2)
        H2 = Pair(3)
        S2 = Pair(4)
        L2 = Pair(5)
        Return True
    End Function

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If NewTheme >= 0 Then
                ThemeNow = NewTheme
                Dim H1 As Integer, S1 As Integer, L1 As Integer
                Dim H2 As Integer, S2 As Integer, L2 As Integer
                If GetThemeColors(NewTheme, H1, S1, L1, H2, S2, L2) Then
                    ColorHue = H1
                    ColorSat = S1
                    ColorLightAdjust = L1 - 45
                    ColorHue2 = H2
                    ColorSat2 = S2
                    ColorLightAdjust2 = L2 - 48
                End If
            End If

            '主色阶
            Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
            Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
            Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
            Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
            '副色阶（浅色、描边与背景）
            Color5 = New MyColor().FromHSL2(ColorHue2, ColorSat2, 80 + ColorLightAdjust2 * 0.4)
            Color6 = New MyColor().FromHSL2(ColorHue2, ColorSat2, 91 + ColorLightAdjust2 * 0.1)
            Color7 = New MyColor().FromHSL2(ColorHue2, ColorSat2, 95)
            Color8 = New MyColor().FromHSL2(ColorHue2, ColorSat2, 97)
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
            ColorBg1 = New MyColor(190, Color7)

            ColorSemiTransparent = New MyColor(1, Color8)
            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '顶部条背景（0 渐变 / 1 纯色 / 2 半透明）
            Dim Brush As LinearGradientBrush = Nothing
            Select Case Settings.Get(Of Integer)("UiTitleStyle")
                Case 1 '纯色
                    FrmMain.PanTitle.Background = New SolidColorBrush(CType(New MyColor().FromHSL2(ColorHue, ColorSat, 54 + ColorLightAdjust), Color))
                Case 2 '半透明，允许露出窗口背景
                    FrmMain.PanTitle.Background = New SolidColorBrush(CType(New MyColor().FromHSL2(ColorHue, ColorSat, 54 + ColorLightAdjust), Color)) With {.Opacity = 0.45}
                Case Else '渐变：主色 → 副色
                    Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
                    '两段式线性过渡，中点即主副色的 50% 混合，不会偏左或偏右
                    Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 48 + ColorLightAdjust)})
                    Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue2, ColorSat2, 48 + ColorLightAdjust2)})
                    FrmMain.PanTitle.Background = Brush
            End Select
            FrmMain.PanTitle.Background.Freeze()
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
    End Sub
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        Return True
    End Function
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        Return False
    End Function

#End Region

#Region "更新"

    Friend Sub UpdateCheckByButton()
        Hint("该版本中不包含更新功能……")
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", Sub() Logger.Info("该版本中不包含更新通知功能……"), Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region

End Module
