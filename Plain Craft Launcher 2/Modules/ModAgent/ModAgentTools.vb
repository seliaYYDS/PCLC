Imports Newtonsoft.Json.Linq

''' <summary>可被 AI 调用的工具定义。</summary>
Public Class AgentTool
    Public Name As String
    Public Description As String
    Public Risk As Integer '0 只读，1 中风险，2 高风险
    Public Schema As JObject
    Public Run As Func(Of JObject, String)
End Class

Public Module ModAgentTools

    Public ReadOnly ToolList As New List(Of AgentTool)

    Sub New()
        '启动器状态
        ToolList.Add(New AgentTool With {
            .Name = "get_launcher_state",
            .Description = "获取启动器当前状态：主题、选中的版本、内存设置等",
            .Risk = 0,
            .Schema = New JObject From {{"type", "object"}, {"properties", New JObject()}},
            .Run = Function(Args) GetLauncherState()})

        '实例列表
        ToolList.Add(New AgentTool With {
            .Name = "list_instances",
            .Description = "列出所有已安装的 Minecraft 版本",
            .Risk = 0,
            .Schema = New JObject From {{"type", "object"}, {"properties", New JObject()}},
            .Run = Function(Args) ListInstances()})

        '读取日志
        Dim LogSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"lines", New JObject From {{"type", "integer"}, {"description", "读取最后多少行，默认 120"}}}
            }}
        }
        ToolList.Add(New AgentTool With {
            .Name = "read_log",
            .Description = "读取启动器与游戏日志的末尾内容，用于排查启动失败等问题",
            .Risk = 0,
            .Schema = LogSchema,
            .Run = Function(Args)
                       Dim Count As Integer = If(Args("lines") Is Nothing, 120, Args("lines").ToObject(Of Integer)())
                       Return ReadLog(Count)
                   End Function})

        '抓取网页
        Dim WebSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"url", New JObject From {{"type", "string"}, {"description", "要抓取的完整 URL"}}},
                {"method", New JObject From {{"type", "string"}, {"enum", New JArray From {"GET", "POST"}}}},
                {"body", New JObject From {{"type", "string"}, {"description", "POST 请求体"}}}
            }},
            {"required", New JArray From {"url"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "web_fetch",
            .Description = "抓取指定网页或接口内容（HTML 会转成纯文本），用于查询资料",
            .Risk = 1,
            .Schema = WebSchema,
            .Run = Function(Args) ModAgentWeb.Fetch(Args)})

        '修改内存
        Dim MemSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {{"mb", New JObject From {{"type", "integer"}, {"description", "分配给游戏的 MB 数，例如 4096"}}}}},
            {"required", New JArray From {"mb"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "set_memory",
            .Description = "设置当前选中版本的内存分配（MB）",
            .Risk = 1,
            .Schema = MemSchema,
            .Run = Function(Args) SetMemory(Args)})

        '打开页面
        Dim PageSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {{"page", New JObject From {{"type", "string"}, {"enum", New JArray From {"启动", "下载", "设置", "更多"}}}}}},
            {"required", New JArray From {"page"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "open_page",
            .Description = "切换启动器到指定页面",
            .Risk = 1,
            .Schema = PageSchema,
            .Run = Function(Args) OpenPage(Args)})

        '启动游戏
        ToolList.Add(New AgentTool With {
            .Name = "launch_game",
            .Description = "启动当前选中的 Minecraft 版本",
            .Risk = 2,
            .Schema = New JObject From {{"type", "object"}, {"properties", New JObject()}},
            .Run = Function(Args) LaunchGame()})

        '安装版本
        Dim InstallSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"version", New JObject From {{"type", "string"}, {"description", "原版版本号，例如 1.20.1"}}},
                {"name", New JObject From {{"type", "string"}, {"description", "安装后的版本名称，留空则与版本号相同"}}},
                {"loader", New JObject From {{"type", "string"}, {"enum", New JArray From {"vanilla", "fabric", "forge", "neoforge"}}, {"description", "加载器类型，默认 vanilla"}}},
                {"loaderVersion", New JObject From {{"type", "string"}, {"description", "Fabric 加载器版本，留空自动选择稳定版"}}}
            }},
            {"required", New JArray From {"version"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "install_instance",
            .Description = "安装 Minecraft 版本，支持原版与 Fabric（Forge/NeoForge 请让用户去下载页手动选择）",
            .Risk = 2,
            .Schema = InstallSchema,
            .Run = Function(Args) InstallInstance(Args)})
    End Sub

    ''' <summary>把工具定义转成 OpenAI tools 数组。</summary>
    Public Function BuildToolsJson() As JArray
        Dim Result As New JArray()
        For Each Item In ToolList
            Result.Add(New JObject From {
                {"type", "function"},
                {"function", New JObject From {
                    {"name", Item.Name},
                    {"description", Item.Description},
                    {"parameters", Item.Schema}
                }}
            })
        Next
        Return Result
    End Function

    Public Function FindTool(Name As String) As AgentTool
        For Each Item In ToolList
            If Item.Name = Name Then Return Item
        Next
        Return Nothing
    End Function

    '实现

    Private Function GetLauncherState() As String
        Try
            Dim State As New JObject()
            State("version") = VersionBranchMain
            State("theme") = Settings.Get(Of Integer)("UiLauncherTheme")
            State("memory") = Settings.Get(Of Integer)("VersionRam")
            State("java") = Settings.Get(Of String)("LaunchArgumentJavaExe")
            State("selectedInstance") = If(McInstanceSelected Is Nothing, Nothing, McInstanceSelected.Name)
            Return State.ToString(Newtonsoft.Json.Formatting.None)
        Catch ex As Exception
            Return "读取启动器状态失败：" & ex.Message
        End Try
    End Function

    Private Function ListInstances() As String
        Try
            Dim Names As New JArray()
            For Each Pair In McInstanceList
                For Each Item In Pair.Value
                    Dim Entry As New JObject From {{"name", Item.Name}, {"info", Item.Info}}
                    Names.Add(Entry)
                Next
            Next
            If Names.Count = 0 Then Return "当前没有已安装的版本"
            Return Names.ToString(Newtonsoft.Json.Formatting.None)
        Catch ex As Exception
            Return "读取版本列表失败：" & ex.Message
        End Try
    End Function

    Private Function ReadLog(Count As Integer) As String
        Try
            If Count <= 0 Then Count = 120
            Dim Path As String = Paths.Base & "PCLLog1.txt"
            If Not IO.File.Exists(Path) Then Return "找不到日志文件：" & Path
            Dim All As String() = IO.File.ReadAllLines(Path)
            Dim Start As Integer = Math.Max(0, All.Length - Count)
            Dim Builder As New Text.StringBuilder()
            For Index As Integer = Start To All.Length - 1
                Builder.AppendLine(All(Index))
            Next
            Dim Text As String = Builder.ToString()
            If Text.Length > 20000 Then Text = Text.Substring(Text.Length - 20000)
            Return Text
        Catch ex As Exception
            Return "读取日志失败：" & ex.Message
        End Try
    End Function


    Private Function SetMemory(Args As JObject) As String
        If McInstanceSelected Is Nothing Then Return "当前没有选中的版本"
        Dim Mb As Integer = Args("mb").ToObject(Of Integer)()
        If Mb < 512 Then Mb = 512
        If Mb > 65536 Then Mb = 65536
        Settings.Set("VersionRamType", 2, Instance:=McInstanceSelected)
        Settings.Set("VersionRamCustom", Mb, Instance:=McInstanceSelected)
        Return $"已将 {McInstanceSelected.Name} 的内存设置为 {Mb} MB"
    End Function

    Private Function OpenPage(Args As JObject) As String
        Dim Name As String = Args("page")?.ToString()
        Dim Target As FormMain.PageType
        Select Case Name
            Case "启动" : Target = FormMain.PageType.Launch
            Case "下载" : Target = FormMain.PageType.Download
            Case "设置" : Target = FormMain.PageType.Setup
            Case "更多" : Target = FormMain.PageType.Other
            Case Else : Return "未知页面：" & Name
        End Select
        RunInUi(Sub() FrmMain.PageChange(New FormMain.PageStackData With {.Page = Target}))
        Return "已切换到" & Name
    End Function

    Private Function InstallInstance(Args As JObject) As String
        Dim Version As String = Args("version")?.ToString()
        If String.IsNullOrWhiteSpace(Version) Then Return "缺少 version 参数（例如 1.20.1）"
        Version = Version.Trim()
        Dim Name As String = If(Args("name") Is Nothing, Version, Args("name").ToString().Trim())
        If Name.Length = 0 Then Name = Version
        If Name.IndexOfAny(IO.Path.GetInvalidFileNameChars()) >= 0 Then Return "版本名称包含非法字符"
        Dim Folder As String = McFolderSelected & "versions\" & Name & "\"
        If IO.Directory.Exists(Folder) Then Return "目标版本文件夹已存在：" & Folder
        Dim Url As String = DlClientListGet(Version)
        If String.IsNullOrWhiteSpace(Url) Then Return "未在版本列表中找到 " & Version
        Dim Loader As String = If(Args("loader") Is Nothing, "vanilla", Args("loader").ToString().ToLower())
        Dim LoaderVersion As String = If(Args("loaderVersion") Is Nothing, "", Args("loaderVersion").ToString())
        Dim Components As McComponents
        If Loader = "forge" OrElse Loader = "neoforge" Then
            Return "暂不支持自动安装 Forge/NeoForge，请到 下载 → 安装 页面手动选择加载器版本"
        End If
        If Loader = "fabric" Then
            If DlFabricListLoader.State <> LoadState.Finished Then
                DlFabricListLoader.Start(0)
                DlFabricListLoader.WaitForExit(0)
            End If
            Dim GameList As JArray = TryCast(DlFabricListLoader.Output.Value("game"), JArray)
            If GameList IsNot Nothing Then
                Dim Supported As Boolean = False
                For Each Item In GameList
                    If Item("version").ToString() = Version Then Supported = True
                Next
                If Not Supported Then Return "Fabric 不支持 Minecraft " & Version
            End If
            Dim Loaders As JArray = TryCast(DlFabricListLoader.Output.Value("loader"), JArray)
            If Loaders Is Nothing OrElse Loaders.Count = 0 Then Return "获取 Fabric 版本列表失败"
            Dim Chosen As String = Nothing
            If LoaderVersion <> "" Then
                For Each Item In Loaders
                    If Item("version").ToString() = LoaderVersion Then Chosen = LoaderVersion
                Next
            Else
                For Each Item In Loaders
                    If Item("stable") IsNot Nothing AndAlso Item("stable").ToString().ToLower() = "true" Then
                        Chosen = Item("version").ToString()
                        Exit For
                    End If
                Next
                If Chosen Is Nothing Then Chosen = Loaders(0)("version").ToString()
            End If
            If Chosen Is Nothing Then Return "未找到指定的 Fabric 版本：" & LoaderVersion
            Components = New McComponents(Version, Nothing, Nothing, Nothing, Chosen, Nothing, Nothing, Nothing)
        Else
            Components = New McComponents(Version, Nothing, Nothing, Nothing, Nothing, Nothing, Nothing, Nothing)
        End If
        Dim Success As Boolean = False
        Dim Done As New Threading.ManualResetEvent(False)
        RunInUi(Sub()
                   Try
                       Dim Request As New McInstallRequest With {
                           .NewInstanceName = Name,
                           .InstanceFolder = Folder,
                           .Components = Components,
                           .MinecraftJson = Url
                       }
                       Success = McInstallStart(Request)
                   Catch
                   Finally
                       Done.Set()
                   End Try
               End Sub)
        Done.WaitOne(20000)
        If Success Then Return "已开始安装 " & Name & "，可在任务列表查看进度"
        Return "启动安装失败。可能原因：版本列表获取失败、磁盘空间不足或网络异常；" &
               "可以让用户稍后重试，或在 设置 → 启动器 → 日志 中查看具体错误。"
    End Function
    Private Function LaunchGame() As String
        If McInstanceSelected Is Nothing Then Return "当前没有选中的版本，无法启动"
        RunInUi(Sub() McLaunchStart())
        Return "正在启动 " & McInstanceSelected.Name
    End Function

End Module
