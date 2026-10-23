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

        '删除实例
        Dim DelSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {{"name", New JObject From {{"type", "string"}, {"description", "要删除的版本名称"}}}}},
            {"required", New JArray From {"name"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "delete_instance",
            .Description = "删除指定的 Minecraft 版本（移动文件夹到回收站）",
            .Risk = 2,
            .Schema = DelSchema,
            .Run = Function(Args) DeleteInstance(Args)})
        '切换小瑟的表情
        Dim MoodSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {{"mood", New JObject From {{"type", "string"}, {"enum", New JArray From {"normal", "happy", "confused", "angry", "shy"}}}}}},
            {"required", New JArray From {"mood"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "set_mascot_mood",
            .Description = "设置小瑟的表情来表达情绪：normal 平静、happy 开心、confused 疑惑、angry 生气、shy 害羞",
            .Risk = 0,
            .Schema = MoodSchema,
            .Run = Function(Args) SetMascotMood(Args)})
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

    Private Function SetMascotMood(Args As JObject) As String
        Dim Mood As String = Args("mood")?.ToString()
        If String.IsNullOrWhiteSpace(Mood) Then Return "缺少 mood 参数"
        Mood = Mood.ToLower()
        Select Case Mood
            Case "normal", "happy", "confused", "angry", "shy"
            Case Else
                Return "不支持的表情：" & Mood
        End Select
        RunInUi(Sub()
                   If FrmMain IsNot Nothing AndAlso FrmMain.FrmAgent IsNot Nothing Then FrmMain.FrmAgent.SetMood(Mood)
               End Sub)
        Return "已将小瑟的表情切换为 " & Mood
    End Function
    ''' <summary>把工具调用翻译成中文说明，用于确认弹窗。</summary>
    Public Function DescribeAction(Name As String, Args As JObject, RawArgs As String) As String
        Try
            Select Case Name
                Case "install_instance"
                    Dim Loader As String = If(Args("loader") Is Nothing, "vanilla", Args("loader").ToString())
                    Dim LoaderText As String = If(Loader = "vanilla", "原版", Loader)
                    Return "安装 Minecraft " & Args("version")?.ToString() & "（" & LoaderText & "）" &
                           If(Args("name") Is Nothing, "", "，版本名：" & Args("name").ToString())
                Case "delete_instance"
                    Return "删除版本「" & Args("name")?.ToString() & "」，文件夹会被移入回收站"
                Case "launch_game"
                    Return "启动当前选中的 Minecraft 版本"
                Case "set_memory"
                    Return "把内存分配设置为 " & Args("mb")?.ToString() & " MB"
                Case "open_page"
                    Return "切换启动器页面到「" & Args("page")?.ToString() & "」"
                Case "web_fetch"
                    Return "访问网页：" & Args("url")?.ToString()
                Case "read_log"
                    Return "读取启动器日志"
                Case "set_mascot_mood"
                    Return "切换小瑟的表情为「" & Args("mood")?.ToString() & "」"
                Case Else
                    Dim Text As String = If(RawArgs, "")
                    If Text.Length > 300 Then Text = Text.Substring(0, 300) & "…"
                    Return Name & If(String.IsNullOrWhiteSpace(Text), "", "：" & Text)
            End Select
        Catch
            Return Name
        End Try
    End Function
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
        Dim ForgeEntry As DlForgeVersionEntry = Nothing
        Dim NeoEntry As DlNeoForgeListEntry = Nothing
        If Loader = "forge" Then
            ForgeEntry = PickForgeVersion(Version, LoaderVersion)
            If ForgeEntry Is Nothing Then Return "没有找到适用于 Minecraft " & Version & " 的 Forge 版本"
            Components = New McComponents(Version, Nothing, ForgeEntry.VersionName, Nothing, Nothing, Nothing, Nothing, Nothing)
        ElseIf Loader = "neoforge" Then
            NeoEntry = PickNeoForgeVersion(Version, LoaderVersion)
            If NeoEntry Is Nothing Then Return "没有找到适用于 Minecraft " & Version & " 的 NeoForge 版本"
            Components = New McComponents(Version, Nothing, Nothing, NeoEntry.VersionName, Nothing, Nothing, Nothing, Nothing)
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
        ElseIf Loader <> "forge" AndAlso Loader <> "neoforge" Then
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
                           .MinecraftJson = Url,
                           .ForgeEntry = ForgeEntry,
                           .NeoForgeEntry = NeoEntry
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
    Private Function PickForgeVersion(McVersion As String, Want As String) As DlForgeVersionEntry
        Try
            Dim Task As New LoaderTask(Of String, List(Of DlForgeVersionEntry))("AgentForgeList", AddressOf DlForgeVersionMain)
            Task.Start(McVersion)
            Task.WaitForExit(60000)
            If Task.State <> LoadState.Finished OrElse Task.Output Is Nothing Then Return Nothing
            Dim List As List(Of DlForgeVersionEntry) = Task.Output
            If List.Count = 0 Then Return Nothing
            If Not String.IsNullOrWhiteSpace(Want) Then
                For Each Item In List
                    If String.Equals(Item.VersionName, Want, StringComparison.OrdinalIgnoreCase) Then Return Item
                Next
                Return Nothing
            End If
            For Each Item In List
                If Item.IsRecommended Then Return Item
            Next
            Return List(0)
        Catch
            Return Nothing
        End Try
    End Function

    Private Function PickNeoForgeVersion(McVersion As String, Want As String) As DlNeoForgeListEntry
        Try
            If DlNeoForgeListLoader.State <> LoadState.Finished Then
                DlNeoForgeListLoader.Start(0)
                DlNeoForgeListLoader.WaitForExit(60000)
            End If
            If DlNeoForgeListLoader.State <> LoadState.Finished Then Return Nothing
            Dim All As List(Of DlNeoForgeListEntry) = DlNeoForgeListLoader.Output.Value
            If All Is Nothing Then Return Nothing
            Dim Matched As New List(Of DlNeoForgeListEntry)
            For Each Item In All
                If Item.Inherit = McVersion Then Matched.Add(Item)
            Next
            If Matched.Count = 0 Then Return Nothing
            If Not String.IsNullOrWhiteSpace(Want) Then
                For Each Item In Matched
                    If String.Equals(Item.VersionName, Want, StringComparison.OrdinalIgnoreCase) Then Return Item
                Next
                Return Nothing
            End If
            For Each Item In Matched
                If Not Item.IsBeta Then Return Item
            Next
            Return Matched(0)
        Catch
            Return Nothing
        End Try
    End Function

    Private Function DeleteInstance(Args As JObject) As String
        Dim Name As String = Args("name")?.ToString()
        If String.IsNullOrWhiteSpace(Name) Then Return "缺少 name 参数"
        Dim Target As McInstance = Nothing
        For Each Pair In McInstanceList
            For Each Item In Pair.Value
                If String.Equals(Item.Name, Name, StringComparison.OrdinalIgnoreCase) Then Target = Item
            Next
        Next
        If Target Is Nothing Then Return "没有找到版本：" & Name
        If McInstanceLock.IsLocked(Target) Then Return "该版本正在被修改，请稍后再删除"
        Try
            DirectoryUtils.Delete(Target.PathVersion, True)
        Catch ex As Exception
            Return "删除失败：" & ex.Message
        End Try
        RunInUi(Sub()
                   LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
                   FrmMain.PageChange(New FormMain.PageStackData With {.Page = FormMain.PageType.Launch})
               End Sub)
        Return "已删除版本 " & Name & "（已放入回收站）"
    End Function
    Private Function LaunchGame() As String
        If McInstanceSelected Is Nothing Then Return "当前没有选中的版本，无法启动"
        RunInUi(Sub() McLaunchStart())
        Return "正在启动 " & McInstanceSelected.Name
    End Function

End Module
