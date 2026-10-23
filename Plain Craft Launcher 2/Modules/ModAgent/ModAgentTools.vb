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
            .Description = "Get launcher state: theme, selected instance and memory settings.",
            .Risk = 0,
            .Schema = New JObject From {{"type", "object"}, {"properties", New JObject()}},
            .Run = Function(Args) GetLauncherState()})

        '实例列表
        ToolList.Add(New AgentTool With {
            .Name = "list_instances",
            .Description = "List installed Minecraft instances.",
            .Risk = 0,
            .Schema = New JObject From {{"type", "object"}, {"properties", New JObject()}},
            .Run = Function(Args) ListInstances()})

        '读取日志
        Dim LogSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"lines", New JObject From {{"type", "integer"}, {"description", "Number of trailing lines to read (default 120)."}}}
            }}
        }
        ToolList.Add(New AgentTool With {
            .Name = "read_log",
            .Description = "Read the tail of launcher and game logs for troubleshooting.",
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
                {"url", New JObject From {{"type", "string"}, {"description", "Full URL to fetch."}}},
                {"method", New JObject From {{"type", "string"}, {"enum", New JArray From {"GET", "POST"}}}},
                {"body", New JObject From {{"type", "string"}, {"description", "POST body."}}}
            }},
            {"required", New JArray From {"url"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "web_fetch",
            .Description = "Fetch a URL and convert HTML to plain text.",
            .Risk = 1,
            .Schema = WebSchema,
            .Run = Function(Args) ModAgentWeb.Fetch(Args)})

        '修改内存
        Dim MemSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {{"mb", New JObject From {{"type", "integer"}, {"description", "Memory in MB, e.g. 4096."}}}}},
            {"required", New JArray From {"mb"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "set_memory",
            .Description = "Set memory allocation (MB) for the selected instance.",
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
            .Description = "Switch the launcher to a page.",
            .Risk = 1,
            .Schema = PageSchema,
            .Run = Function(Args) OpenPage(Args)})

        '启动游戏
        ToolList.Add(New AgentTool With {
            .Name = "launch_game",
            .Description = "Launch the selected Minecraft instance.",
            .Risk = 2,
            .Schema = New JObject From {{"type", "object"}, {"properties", New JObject()}},
            .Run = Function(Args) LaunchGame()})

        '安装版本
        Dim InstallSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"version", New JObject From {{"type", "string"}, {"description", "Vanilla version, e.g. 1.20.1."}}},
                {"name", New JObject From {{"type", "string"}, {"description", "Instance name; defaults to the version."}}},
                {"loader", New JObject From {{"type", "string"}, {"enum", New JArray From {"vanilla", "fabric", "forge", "neoforge"}}, {"description", "Loader type (default vanilla)."}}},
                {"loaderVersion", New JObject From {{"type", "string"}, {"description", "Loader version; defaults: Fabric stable, Forge recommended, NeoForge latest non-beta."}}},
                {"fabricApi", New JObject From {{"type", "boolean"}, {"description", "Also install Fabric API (default true)."}}}
            }},
            {"required", New JArray From {"version"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "install_instance",
            .Description = "Install an instance: vanilla, Fabric (with Fabric API), Forge or NeoForge.",
            .Risk = 2,
            .Schema = InstallSchema,
            .Run = Function(Args) InstallInstance(Args)})

        '为现有实例安装/更换加载器
        Dim ModifySchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"name", New JObject From {{"type", "string"}, {"description", "Instance name (default: selected instance)."}}},
                {"loader", New JObject From {{"type", "string"}, {"description", "forge/fabric/neoforge"}}},
                {"loaderVersion", New JObject From {{"type", "string"}, {"description", "Loader version (default recommended/stable)."}}}
            }},
            {"required", New JArray From {"loader"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "modify_instance_loader",
            .Description = "Add or change the Forge/Fabric/NeoForge loader of an existing instance, keeping mods and configs.",
            .Risk = 2,
            .Schema = ModifySchema,
            .Run = Function(Args) ModifyInstanceLoader(Args)})
        '删除实例
        Dim DelSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {{"name", New JObject From {{"type", "string"}, {"description", "Instance name to delete."}}}}},
            {"required", New JArray From {"name"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "delete_instance",
            .Description = "Delete an instance; its folder is moved to the recycle bin.",
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
            .Description = "Set Xiaose's expression: normal, happy, confused, angry or shy.",
            .Risk = 0,
            .Schema = MoodSchema,
            .Run = Function(Args) SetMascotMood(Args)})

        RegisterExtraTools()
        ModAgentContent.Register(ToolList)
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
                    Return "Launch the selected Minecraft instance."
                Case "set_memory"
                    Return "把内存分配设置为 " & Args("mb")?.ToString() & " MB"
                Case "open_page"
                    Return "切换启动器页面到「" & Args("page")?.ToString() & "」"
                Case "web_fetch"
                    Return "访问网页：" & Args("url")?.ToString()
                Case "read_log"
                    Return "读取启动器日志"
                Case "get_launcher_state"
                    Return "读取启动器状态"
                Case "list_instances"
                    Return "列出已安装版本"
                Case "list_accounts"
                    Return "列出已保存账号"
                Case "modify_instance_loader"
                    Return "为版本添加加载器：" & Args("loader")?.ToString() & If(Args("name") Is Nothing, "", "（" & Args("name").ToString() & "）")
                Case "install_modpack"
                    Return "安装整合包：" & Args("name")?.ToString()
                Case "list_worlds"
                    Return "列出存档"
                Case "search_content"
                    Return "搜索资源：" & If(Args("query") Is Nothing, "（全部）", Args("query").ToString()) & "（" & If(Args("type") Is Nothing, "mod", Args("type").ToString()) & "）"
                Case "list_content_versions"
                    Return "查询项目版本：" & Args("project")?.ToString()
                Case "install_projects"
                    Dim Count As Integer = If(Args("items") Is Nothing, 0, CType(Args("items"), JArray).Count)
                    Return "批量安装 " & Count & " 个资源（含必需前置）"
                Case "install_content"
                    Return "安装资源文件：" & Args("fileName")?.ToString() & "（" & If(Args("type") Is Nothing, "mod", Args("type").ToString()) & "）"
                Case "delete_content"
                    Return "删除资源文件：" & Args("fileName")?.ToString()
                Case "list_installed_content"
                    Return "列出已安装资源（" & If(Args("type") Is Nothing, "mod", Args("type").ToString()) & "）"
                Case "read_file"
                    Return "读取文件：" & Args("path")?.ToString()
                Case "write_file"
                    Return "写入文件：" & Args("path")?.ToString() & "（" & Args("content")?.ToString().Length & " 字符）"
                Case "list_dir"
                    Return "列出目录：" & Args("path")?.ToString()
                Case "run_pwsh"
                    Return "执行 PowerShell 命令：" & Args("command")?.ToString()
                Case "select_account"
                    Return "切换到账号：" & Args("name")?.ToString()
                Case "add_offline_account"
                    Return "添加离线账号：" & Args("name")?.ToString()
                Case "delete_account"
                    Return "移除账号：" & Args("name")?.ToString()
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
            Try
                State("theme") = Settings.Get(Of Integer)("UiLauncherTheme")
            Catch
                State("theme") = 0
            End Try
            Dim Ram As Integer = 0
            Try
                Ram = Settings.Get(Of Integer)("VersionRam")
            Catch
            End Try
            State("memory") = Ram
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
            '列表可能尚未加载，直接扫描 versions 目录兜底
            If Names.Count = 0 Then
                Dim Folder As String = McFolderSelected & "versions\"
                If Not IO.Directory.Exists(Folder) Then Return "没有找到版本目录：" & Folder
                For Each SubFolder In IO.Directory.GetDirectories(Folder)
                    Dim JsonPath As String = IO.Path.Combine(SubFolder, IO.Path.GetFileName(SubFolder) & ".json")
                    Dim Info As String = ""
                    If IO.File.Exists(JsonPath) Then
                        Try
                            Dim Json As JObject = JObject.Parse(IO.File.ReadAllText(JsonPath, System.Text.Encoding.UTF8))
                            Info = If(Json("id")?.ToString(), "")
                        Catch
                        End Try
                    End If
                    Dim Entry As New JObject From {{"name", IO.Path.GetFileName(SubFolder)}, {"info", Info}}
                    Names.Add(Entry)
                Next
            End If
            Logger.Info($"[AgentListInstances] count={Names.Count} folder={McFolderSelected}")
            If Names.Count = 0 Then Return "当前没有已安装的版本（版本目录：" & McFolderSelected & "versions\）"
            Return Names.ToString(Newtonsoft.Json.Formatting.None)
        Catch ex As Exception
            Return "读取版本列表失败：" & ex.Message
        End Try
    End Function

    Private Function ReadLog(Count As Integer) As String
        Try
            If Count <= 0 Then Count = 120
            Dim Path As String = Paths.Base & "PCL\Log1.txt"
            If Not IO.File.Exists(Path) Then Return "找不到日志文件：" & Path
            Dim All As String()
            Using Stream As New IO.FileStream(Path, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite)
                Using Reader As New IO.StreamReader(Stream, System.Text.Encoding.UTF8)
                    All = Reader.ReadToEnd().Split({vbCrLf, vbLf}, StringSplitOptions.None)
                End Using
            End Using
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
        Dim FabricApiEntry As ResourceVersion = Nothing
        If Loader = "forge" Then
            ForgeEntry = PickForgeVersion(Version, LoaderVersion)
            If ForgeEntry Is Nothing Then Return "没有找到适用于 Minecraft " & Version & " 的 Forge 版本" & If(String.IsNullOrWhiteSpace(LastInstallError), "", "：" & LastInstallError)
            Components = New McComponents(Version, Nothing, ForgeEntry.VersionName, Nothing, Nothing, Nothing, Nothing, Nothing)
        ElseIf Loader = "neoforge" Then
            NeoEntry = PickNeoForgeVersion(Version, LoaderVersion)
            If NeoEntry Is Nothing Then Return "没有找到适用于 Minecraft " & Version & " 的 NeoForge 版本" & If(String.IsNullOrWhiteSpace(LastInstallError), "", "：" & LastInstallError)
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
            Dim WantApi As Boolean = Args("fabricApi") Is Nothing OrElse Args("fabricApi").ToObject(Of Boolean)()
            If WantApi Then
                Try
                    If DlFabricApiLoader.State <> LoadState.Finished Then
                        DlFabricApiLoader.Start(0)
                        DlFabricApiLoader.WaitForExit(60000)
                    End If
                    If DlFabricApiLoader.State = LoadState.Finished AndAlso DlFabricApiLoader.Output IsNot Nothing Then
                        For Each Item In DlFabricApiLoader.Output
                            If Item.GameVersions IsNot Nothing AndAlso Item.GameVersions.Contains(Version) AndAlso Not String.IsNullOrWhiteSpace(Item.Version) Then
                                If FabricApiEntry Is Nothing OrElse Item.ReleaseType = ResourceVersion.ReleaseTypes.Release Then FabricApiEntry = Item
                            End If
                        Next
                    End If
                Catch
                End Try
            End If
            Components = New McComponents(Version, Nothing, Nothing, Nothing, Chosen, If(FabricApiEntry Is Nothing, Nothing, FabricApiEntry.Version), Nothing, Nothing)
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
                           .NeoForgeEntry = NeoEntry,
                           .FabricApi = FabricApiEntry
                       }
                       Success = McInstallStart(Request)
                   Catch
                   Finally
                       Done.Set()
                   End Try
               End Sub)
        Done.WaitOne(20000)
        If Success Then
            ModAgentChanges.Added("版本", Name)
            Return "已开始安装 " & Name & "，可在任务列表查看进度"
        End If
        Return "启动安装失败。可能原因：版本列表获取失败、磁盘空间不足或网络异常；" &
               "可以让用户稍后重试，或在 设置 → 启动器 → 日志 中查看具体错误。"
    End Function
    Private Function ModifyInstanceLoader(Args As JObject) As String
        Dim Loader As String = Args("loader")?.ToString().ToLower()
        If String.IsNullOrWhiteSpace(Loader) Then Return "缺少 loader 参数"
        Dim Name As String = Args("name")?.ToString()
        Dim Target As McInstance = Nothing
        If String.IsNullOrWhiteSpace(Name) Then
            Target = McInstanceSelected
        Else
            For Each Pair In McInstanceList
                For Each Item In Pair.Value
                    If String.Equals(Item.Name, Name, StringComparison.OrdinalIgnoreCase) Then Target = Item
                Next
            Next
        End If
        If Target Is Nothing Then Return "没有找到版本" & If(String.IsNullOrWhiteSpace(Name), "", "：" & Name)
        If McInstanceLock.IsLocked(Target) Then Return "该版本正在被修改，请稍后再试"
        Dim Info = Target.Version
        If Info Is Nothing OrElse Info.VanillaName = "Unknown" Then Return "无法确定该版本的 Minecraft 版本，无法修改"
        Dim Want As String = If(Args("loaderVersion") Is Nothing, "", Args("loaderVersion").ToString())
        Dim Comps As New McComponents(Info.VanillaName, Info.ModLoaderVersions)
        Dim ForgeEntry As DlForgeVersionEntry = Nothing
        Dim NeoEntry As DlNeoForgeListEntry = Nothing
        Dim FabricApiEntry As ResourceVersion = Nothing
        Select Case Loader
            Case "forge"
                ForgeEntry = PickForgeVersion(Info.VanillaName, Want)
                If ForgeEntry Is Nothing Then Return "没有找到适用于 " & Info.VanillaName & " 的 Forge 版本"
                Comps.ModLoaderVersions(ModLoaders.Forge) = ForgeEntry.VersionName
            Case "neoforge"
                NeoEntry = PickNeoForgeVersion(Info.VanillaName, Want)
                If NeoEntry Is Nothing Then Return "没有找到适用于 " & Info.VanillaName & " 的 NeoForge 版本"
                Comps.ModLoaderVersions(ModLoaders.NeoForge) = NeoEntry.VersionName
            Case "fabric"
                If DlFabricListLoader.State <> LoadState.Finished Then
                    DlFabricListLoader.Start(0)
                    DlFabricListLoader.WaitForExit(60000)
                End If
                If DlFabricListLoader.State <> LoadState.Finished Then Return "获取 Fabric 版本列表失败"
                Dim Loaders As JArray = TryCast(DlFabricListLoader.Output.Value("loader"), JArray)
                If Loaders Is Nothing OrElse Loaders.Count = 0 Then Return "获取 Fabric 版本列表失败"
                Dim Chosen As String = Nothing
                If Want <> "" Then
                    For Each Item In Loaders
                        If Item("version").ToString() = Want Then Chosen = Want
                    Next
                Else
                    For Each Item In Loaders
                        If Item("stable") IsNot Nothing AndAlso Item("stable").ToString().ToLower() = "true" Then Chosen = Item("version").ToString() : Exit For
                    Next
                    If Chosen Is Nothing Then Chosen = Loaders(0)("version").ToString()
                End If
                If Chosen Is Nothing Then Return "未找到指定的 Fabric 版本：" & Want
                Comps.ModLoaderVersions(ModLoaders.Fabric) = Chosen
                Try
                    If DlFabricApiLoader.State <> LoadState.Finished Then
                        DlFabricApiLoader.Start(0)
                        DlFabricApiLoader.WaitForExit(60000)
                    End If
                    If DlFabricApiLoader.State = LoadState.Finished AndAlso DlFabricApiLoader.Output IsNot Nothing Then
                        For Each Item In DlFabricApiLoader.Output
                            If Item.GameVersions IsNot Nothing AndAlso Item.GameVersions.Contains(Info.VanillaName) Then
                                If FabricApiEntry Is Nothing OrElse Item.ReleaseType = ResourceVersion.ReleaseTypes.Release Then FabricApiEntry = Item
                            End If
                        Next
                    End If
                Catch
                End Try
            Case Else
                Return "不支持的加载器：" & Loader
        End Select
        Dim Success As Boolean = False
        Dim Done As New Threading.ManualResetEvent(False)
        RunInUi(Sub()
                   Try
                       Dim Request As New McInstallRequest With {
                           .InstanceFolder = Target.PathVersion,
                           .Components = Comps,
                           .ForgeEntry = ForgeEntry,
                           .NeoForgeEntry = NeoEntry,
                           .FabricApi = FabricApiEntry
                       }
                       Success = McModificationStart(Request, Target)
                   Catch
                   Finally
                       Done.Set()
                   End Try
               End Sub)
        Done.WaitOne(20000)
        If Success Then
            ModAgentChanges.Added("加载器", Loader)
            Return "已开始为 " & Target.Name & " 安装 " & Loader & "，可在任务管理器查看进度"
        End If
        Return "启动修改失败，请查看日志或在下载页手动修改"
    End Function
    Private LastInstallError As String = ""

    Private Function PickForgeVersion(McVersion As String, Want As String) As DlForgeVersionEntry
        Try
            Dim Task As New LoaderTask(Of String, List(Of DlForgeVersionEntry))("AgentForgeList", AddressOf DlForgeVersionMain)
            Task.Start(McVersion)
            Task.WaitForExit(180000)
            Dim List As List(Of DlForgeVersionEntry) = Nothing
            If Task.State = LoadState.Finished Then List = Task.Output
            If List Is Nothing OrElse List.Count = 0 Then
                Logger.Warn(Task.Error, "启动器加载器未能获取 Forge 版本列表，改走 BMCLAPI 直连")
                Dim Fallback As DlForgeVersionEntry = FetchForgeFromApi(McVersion, Want)
                If Fallback Is Nothing Then
                    LastInstallError = "启动器与直连都没能获取 Forge 版本列表（加载状态：" & Task.State.ToString() & "）"
                    Return Nothing
                End If
                Return Fallback
            End If
            If Not String.IsNullOrWhiteSpace(Want) Then
                For Each Item In List
                    If String.Equals(Item.VersionName, Want, StringComparison.OrdinalIgnoreCase) Then Return Item
                Next
                LastInstallError = "版本列表中没有指定的 Forge 版本：" & Want
                Return Nothing
            End If
            For Each Item In List
                If Item.IsRecommended Then Return Item
            Next
            Return List(0)
        Catch ex As Exception
            Logger.Warn(ex, "获取 Forge 版本异常，改走 BMCLAPI 直连")
            Dim Fallback As DlForgeVersionEntry = FetchForgeFromApi(McVersion, Want)
            If Fallback Is Nothing Then LastInstallError = "获取 Forge 版本异常：" & ex.Message
            Return Fallback
        End Try
    End Function

    ''' <summary>直接查询 BMCLAPI 的 Forge 版本，用于启动器加载器失败时兜底。</summary>
    Private Function FetchForgeFromApi(McVersion As String, Want As String) As DlForgeVersionEntry
        Try
            Dim Url As String = "https://bmclapi2.bangbang93.com/forge/minecraft/" & McVersion
            Dim Json As JArray = NetRequestByClientRetry(Url, Encoding:=System.Text.Encoding.UTF8, RequireJson:=True).DeserializeJson()
            If Json Is Nothing OrElse Json.Count = 0 Then Return Nothing
            Dim Best As DlForgeVersionEntry = Nothing
            For Each Item In Json
                Dim Ver As String = Item("version")?.ToString()
                If String.IsNullOrWhiteSpace(Ver) Then Continue For
                Try
                    Dim Entry As New DlForgeVersionEntry(Ver, Item("branch")?.ToString(), McVersion) With {.Category = "installer"}
                    If Not String.IsNullOrWhiteSpace(Want) AndAlso String.Equals(Entry.VersionName, Want, StringComparison.OrdinalIgnoreCase) Then Return Entry
                    If Best Is Nothing OrElse Entry.Version > Best.Version Then Best = Entry
                Catch
                End Try
            Next
            Return Best
        Catch ex As Exception
            Logger.Warn(ex, "直连获取 Forge 版本失败")
            Return Nothing
        End Try
    End Function

    Private Function FetchNeoForgeFromApi(McVersion As String, Want As String) As DlNeoForgeListEntry
        Try
            Dim Best As DlNeoForgeListEntry = Nothing
            For Each Url In {
                "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge",
                "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/forge"}
                Dim Json As JObject = Nothing
                Try
                    Json = NetRequestByClientRetry(Url, Encoding:=System.Text.Encoding.UTF8, RequireJson:=True).DeserializeJson()
                Catch ex As Exception
                    Logger.Warn(ex, "获取 NeoForge Maven 版本失败：" & Url)
                    Continue For
                End Try
                Dim Versions As JArray = TryCast(Json("versions"), JArray)
                If Versions Is Nothing Then Continue For
                For Each Token In Versions
                    Dim ApiName As String = Token.ToString()
                    If String.IsNullOrWhiteSpace(ApiName) Then Continue For
                    Try
                        Dim Entry As New DlNeoForgeListEntry(ApiName)
                        If Entry.Inherit <> McVersion Then Continue For
                        If Not String.IsNullOrWhiteSpace(Want) AndAlso String.Equals(Entry.VersionName, Want, StringComparison.OrdinalIgnoreCase) Then Return Entry
                        If Best Is Nothing Then
                            Best = Entry
                        ElseIf Best.IsBeta AndAlso Not Entry.IsBeta Then
                            Best = Entry
                        ElseIf Best.IsBeta = Entry.IsBeta AndAlso Entry.Version > Best.Version Then
                            Best = Entry
                        End If
                    Catch
                    End Try
                Next
            Next
            If Best Is Nothing Then LastInstallError = "Maven 版本列表中没有适用于 " & McVersion & " 的 NeoForge 版本"
            Return Best
        Catch ex As Exception
            Logger.Warn(ex, "直连获取 NeoForge 版本失败")
            Return Nothing
        End Try
    End Function

    Private Function PickNeoForgeVersion(McVersion As String, Want As String) As DlNeoForgeListEntry
        Try
            If DlNeoForgeListLoader.State <> LoadState.Finished Then
                DlNeoForgeListLoader.Start(0)
                DlNeoForgeListLoader.WaitForExit(180000)
            End If
            Dim All As List(Of DlNeoForgeListEntry) = Nothing
            If DlNeoForgeListLoader.State = LoadState.Finished Then All = DlNeoForgeListLoader.Output.Value
            If All Is Nothing OrElse All.Count = 0 Then
                Logger.Warn(DlNeoForgeListLoader.Error, "启动器加载器未能获取 NeoForge 列表，改走 Maven 直连")
                Return FetchNeoForgeFromApi(McVersion, Want)
            End If
            Dim Matched As New List(Of DlNeoForgeListEntry)
            For Each Item In All
                If Item.Inherit = McVersion Then Matched.Add(Item)
            Next
            If Matched.Count = 0 Then
                Logger.Warn($"NeoForge 列表中没有 {McVersion}，改走 Maven 直连")
                Return FetchNeoForgeFromApi(McVersion, Want)
            End If
            If Not String.IsNullOrWhiteSpace(Want) Then
                For Each Item In Matched
                    If String.Equals(Item.VersionName, Want, StringComparison.OrdinalIgnoreCase) Then Return Item
                Next
                LastInstallError = "版本列表中没有指定的 NeoForge 版本：" & Want
                Return Nothing
            End If
            For Each Item In Matched
                If Not Item.IsBeta Then Return Item
            Next
            Return Matched(0)
        Catch ex As Exception
            Logger.Warn(ex, "获取 NeoForge 版本异常，改走 Maven 直连")
            Return FetchNeoForgeFromApi(McVersion, Want)
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
        ModAgentChanges.Removed("版本", Name)
        Return "已删除版本 " & Name & "（已放入回收站）"
    End Function
    Private Function LaunchGame() As String
        If McInstanceSelected Is Nothing Then Return "当前没有选中的版本，无法启动"
        RunInUi(Sub() McLaunchStart())
        Return "正在启动 " & McInstanceSelected.Name
    End Function


    Private Sub RegisterExtraTools()
        '文件读取
        Dim ReadFileSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"path", New JObject From {{"type", "string"}, {"description", "Path relative to .minecraft."}}},
                {"offset", New JObject From {{"type", "integer"}, {"description", "Start offset in characters (default 0)."}}},
                {"length", New JObject From {{"type", "integer"}, {"description", "Characters to read (default 65536)."}}}
            }},
            {"required", New JArray From {"path"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "read_file",
            .Description = "Read a text file under .minecraft, optionally with offset and length.",
            .Risk = 0,
            .Schema = ReadFileSchema,
            .Run = Function(Args) ReadTextFile(Args)})

        Dim WriteFileSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"path", New JObject From {{"type", "string"}, {"description", "Path relative to .minecraft."}}},
                {"content", New JObject From {{"type", "string"}, {"description", "Content to write."}}},
                {"append", New JObject From {{"type", "boolean"}, {"description", "Append instead of overwrite (default false)."}}}
            }},
            {"required", New JArray From {"path", "content"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "write_file",
            .Description = "Write a text file under .minecraft (overwrite or append).",
            .Risk = 2,
            .Schema = WriteFileSchema,
            .Run = Function(Args) WriteTextFile(Args)})

        Dim ListDirSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"path", New JObject From {{"type", "string"}, {"description", "Directory relative to .minecraft (default root)."}}}
            }}
        }
        ToolList.Add(New AgentTool With {
            .Name = "list_dir",
            .Description = "List files and subfolders of a directory under .minecraft.",
            .Risk = 0,
            .Schema = ListDirSchema,
            .Run = Function(Args) ListDirectory(Args)})

        Dim PwshSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"command", New JObject From {{"type", "string"}, {"description", "PowerShell command to run."}}},
                {"workdir", New JObject From {{"type", "string"}, {"description", "Working directory relative to .minecraft."}}}
            }},
            {"required", New JArray From {"command"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "run_pwsh",
            .Description = "Run a PowerShell command inside .minecraft (high risk).",
            .Risk = 2,
            .Schema = PwshSchema,
            .Run = Function(Args) RunPwsh(Args)})

        ToolList.Add(New AgentTool With {
            .Name = "list_accounts",
            .Description = "List saved Minecraft accounts and the current login type.",
            .Risk = 0,
            .Schema = New JObject From {{"type", "object"}, {"properties", New JObject()}},
            .Run = Function(Args) ListAccounts()})

        Dim SelectAccountSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"name", New JObject From {{"type", "string"}, {"description", "Account name (game name for Microsoft, username for offline)."}}}
            }},
            {"required", New JArray From {"name"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "select_account",
            .Description = "Switch the active account.",
            .Risk = 2,
            .Schema = SelectAccountSchema,
            .Run = Function(Args) SelectAccount(Args)})

        Dim AddLegacySchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"name", New JObject From {{"type", "string"}, {"description", "Offline username."}}}
            }},
            {"required", New JArray From {"name"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "add_offline_account",
            .Description = "Add an offline account and switch to it.",
            .Risk = 2,
            .Schema = AddLegacySchema,
            .Run = Function(Args) AddLegacyAccount(Args)})

        Dim DeleteAccountSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"name", New JObject From {{"type", "string"}, {"description", "Account name to remove."}}}
            }},
            {"required", New JArray From {"name"}}
        }
        ToolList.Add(New AgentTool With {
            .Name = "delete_account",
            .Description = "Remove a saved account.",
            .Risk = 2,
            .Schema = DeleteAccountSchema,
            .Run = Function(Args) DeleteAccount(Args)})
    End Sub

    ' ---- 文件与命令 ----

    Private Function ResolvePath(Relative As String) As String
        Dim Base As String = IO.Path.GetFullPath(McFolderSelected)
        Dim Target As String = IO.Path.GetFullPath(IO.Path.Combine(Base, If(Relative, "")))
        If Not Target.StartsWith(Base, StringComparison.OrdinalIgnoreCase) Then Return Nothing
        Return Target
    End Function

    Private Function ListDirectory(Args As JObject) As String
        Dim Relative As String = If(Args("path") Is Nothing, "", Args("path").ToString())
        Dim Target As String = ResolvePath(Relative)
        If Target Is Nothing Then Return "路径超出 .minecraft 目录"
        If Not IO.Directory.Exists(Target) Then Return "目录不存在：" & Relative
        Dim Builder As New Text.StringBuilder()
        For Each Folder In IO.Directory.GetDirectories(Target)
            Builder.AppendLine("[目录] " & IO.Path.GetFileName(Folder))
        Next
        For Each File In IO.Directory.GetFiles(Target)
            Builder.AppendLine("[文件] " & IO.Path.GetFileName(File) & "  " & New IO.FileInfo(File).Length & " B")
        Next
        Dim Result As String = Builder.ToString()
        If Result.Length = 0 Then Return "（空目录）"
        If Result.Length > 20000 Then Result = Result.Substring(0, 20000) & "…（已截断）"
        Return Result
    End Function

    Private Function ReadTextFile(Args As JObject) As String
        Dim Relative As String = Args("path")?.ToString()
        If String.IsNullOrWhiteSpace(Relative) Then Return "缺少 path 参数"
        Dim Target As String = ResolvePath(Relative)
        If Target Is Nothing Then Return "路径超出 .minecraft 目录"
        If Not IO.File.Exists(Target) Then Return "文件不存在：" & Relative
        Dim Offset As Integer = If(Args("offset") Is Nothing, 0, Math.Max(0, Args("offset").ToObject(Of Integer)()))
        Dim Length As Integer = If(Args("length") Is Nothing, 65536, Math.Max(1, Args("length").ToObject(Of Integer)()))
        Length = Math.Min(Length, 400000)
        Dim Text As String = IO.File.ReadAllText(Target, System.Text.Encoding.UTF8)
        If Offset >= Text.Length Then Return "（偏移超出文件末尾，文件共 " & Text.Length & " 字符）"
        Dim Take As Integer = Math.Min(Length, Text.Length - Offset)
        Dim Body As String = Text.Substring(Offset, Take)
        Dim Tail As String = If(Offset + Take < Text.Length, vbCrLf & "…（未完，下次 offset=" & (Offset + Take) & "）", "")
        Return Body & Tail
    End Function

    Private Function WriteTextFile(Args As JObject) As String
        Dim Relative As String = Args("path")?.ToString()
        If String.IsNullOrWhiteSpace(Relative) Then Return "缺少 path 参数"
        Dim Target As String = ResolvePath(Relative)
        If Target Is Nothing Then Return "路径超出 .minecraft 目录"
        Dim Content As String = If(Args("content") Is Nothing, "", Args("content").ToString())
        Dim Append As Boolean = Args("append") IsNot Nothing AndAlso Args("append").ToObject(Of Boolean)()
        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(Target))
        If Append Then
            IO.File.AppendAllText(Target, Content, System.Text.Encoding.UTF8)
        Else
            IO.File.WriteAllText(Target, Content, System.Text.Encoding.UTF8)
        End If
        ModAgentChanges.Added("本地文件", IO.Path.GetFileName(Relative))
        Return "已写入 " & Relative & "（" & Content.Length & " 字符）"
    End Function

    Private Function RunPwsh(Args As JObject) As String
        Dim Command As String = Args("command")?.ToString()
        If String.IsNullOrWhiteSpace(Command) Then Return "缺少 command 参数"
        Dim Relative As String = If(Args("workdir") Is Nothing, "", Args("workdir").ToString())
        Dim WorkDir As String = ResolvePath(Relative)
        If WorkDir Is Nothing Then Return "工作目录超出 .minecraft 目录"
        If Not IO.Directory.Exists(WorkDir) Then WorkDir = McFolderSelected
        Try
            Dim Exe As String = "pwsh"
            Dim Info As New Diagnostics.ProcessStartInfo(Exe, "-NoProfile -NonInteractive -Command " & Command) With {
                .WorkingDirectory = WorkDir,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .UseShellExecute = False,
                .CreateNoWindow = True}
            Using Proc = Diagnostics.Process.Start(Info)
                Dim Output As String = Proc.StandardOutput.ReadToEnd()
                Dim Err As String = Proc.StandardError.ReadToEnd()
                Proc.WaitForExit(60000)
                Dim Result As String = Output
                If Err.Length > 0 Then Result &= vbCrLf & "[stderr]" & vbCrLf & Err
                If Result.Length > 20000 Then Result = Result.Substring(Result.Length - 20000)
                Return Result
            End Using
        Catch ex As Exception
            If TypeOf ex Is ComponentModel.Win32Exception Then
                Try
                    Dim Info2 As New Diagnostics.ProcessStartInfo("powershell", "-NoProfile -NonInteractive -Command " & Command) With {
                        .WorkingDirectory = WorkDir,
                        .RedirectStandardOutput = True,
                        .RedirectStandardError = True,
                        .UseShellExecute = False,
                        .CreateNoWindow = True}
                    Using Proc2 = Diagnostics.Process.Start(Info2)
                        Dim Out2 As String = Proc2.StandardOutput.ReadToEnd()
                        Dim Err2 As String = Proc2.StandardError.ReadToEnd()
                        Proc2.WaitForExit(60000)
                        Dim Res2 As String = Out2
                        If Err2.Length > 0 Then Res2 &= vbCrLf & "[stderr]" & vbCrLf & Err2
                        If Res2.Length > 20000 Then Res2 = Res2.Substring(Res2.Length - 20000)
                        Return Res2
                    End Using
                Catch ex2 As Exception
                    Return "执行失败：" & ex2.Message
                End Try
            End If
            Return "执行失败：" & ex.Message
        End Try
    End Function

    ' ---- 账号 ----

    Private Function ListAccounts() As String
        Try
            Dim Result As New JObject()
            Result("currentType") = Settings.Get(Of McLoginType)("LoginType").ToString()
            Result("currentName") = McLoginName()
            Dim Ms As New JArray()
            Try
                Dim MsJson As JObject = Settings.Get(Of String)("LoginMsJson").DeserializeJson()
                If MsJson IsNot Nothing Then
                    For Each Item In MsJson.Properties
                        Ms.Add(Item.Name)
                    Next
                End If
            Catch
            End Try
            Result("microsoft") = Ms
            Dim Legacy As New JArray()
            Dim LegacyRaw As String = Settings.Get(Of String)("LoginLegacyName")
            If Not String.IsNullOrWhiteSpace(LegacyRaw) Then
                For Each Item In LegacyRaw.Split("¨"c)
                    If Item.Trim().Length > 0 Then Legacy.Add(Item.Trim())
                Next
            End If
            Result("offline") = Legacy
            Return Result.ToString(Newtonsoft.Json.Formatting.Indented)
        Catch ex As Exception
            Return "读取账号失败：" & ex.Message
        End Try
    End Function

    Private Function SelectAccount(Args As JObject) As String
        Dim Name As String = Args("name")?.ToString()
        If String.IsNullOrWhiteSpace(Name) Then Return "缺少 name 参数"
        Name = Name.Trim()
        Try
            Dim MsJson As JObject = Settings.Get(Of String)("LoginMsJson").DeserializeJson()
            If MsJson IsNot Nothing AndAlso MsJson(Name) IsNot Nothing Then
                Settings.Set("LoginType", CInt(McLoginType.Ms))
                Settings.Set("CacheMsV2Name", Name)
                Settings.Set("CacheMsV2OAuthRefresh", MsJson(Name).ToString())
                Return "已切换到微软账号 " & Name
            End If
            Dim LegacyRaw As String = Settings.Get(Of String)("LoginLegacyName")
            Dim Names As New List(Of String)
            If Not String.IsNullOrWhiteSpace(LegacyRaw) Then Names.AddRange(LegacyRaw.Split("¨"c))
            Dim Hit As String = Names.FirstOrDefault(Function(X) String.Equals(X.Trim(), Name, StringComparison.OrdinalIgnoreCase))
            If Hit Is Nothing Then Return "没有找到账号：" & Name
            Names.Remove(Hit)
            Names.Insert(0, Hit)
            Settings.Set("LoginLegacyName", String.Join("¨", Names))
            Settings.Set("LoginType", CInt(McLoginType.Legacy))
            Return "已切换到离线账号 " & Name
        Catch ex As Exception
            Return "切换账号失败：" & ex.Message
        End Try
    End Function

    Private Function AddLegacyAccount(Args As JObject) As String
        Dim Name As String = Args("name")?.ToString()
        If String.IsNullOrWhiteSpace(Name) Then Return "缺少 name 参数"
        Name = Name.Trim()
        If Name.Contains("¨") Then Return "账号名不能包含特殊分隔符"
        Dim LegacyRaw As String = Settings.Get(Of String)("LoginLegacyName")
        Dim Names As New List(Of String)
        If Not String.IsNullOrWhiteSpace(LegacyRaw) Then Names.AddRange(LegacyRaw.Split("¨"c))
        Names.RemoveAll(Function(X) String.Equals(X.Trim(), Name, StringComparison.OrdinalIgnoreCase))
        Names.Insert(0, Name)
        Settings.Set("LoginLegacyName", String.Join("¨", Names))
        Settings.Set("LoginType", CInt(McLoginType.Legacy))
        Return "已添加并切换到离线账号 " & Name
    End Function

    Private Function DeleteAccount(Args As JObject) As String
        Dim Name As String = Args("name")?.ToString()
        If String.IsNullOrWhiteSpace(Name) Then Return "缺少 name 参数"
        Name = Name.Trim()
        Try
            Dim MsJson As JObject = Settings.Get(Of String)("LoginMsJson").DeserializeJson()
            If MsJson IsNot Nothing AndAlso MsJson(Name) IsNot Nothing Then
                MsJson.Remove(Name)
                Settings.Set("LoginMsJson", MsJson.ToString(Newtonsoft.Json.Formatting.None))
                Return "已移除微软账号 " & Name
            End If
            Dim LegacyRaw As String = Settings.Get(Of String)("LoginLegacyName")
            If String.IsNullOrWhiteSpace(LegacyRaw) Then Return "没有找到账号：" & Name
            Dim Names As New List(Of String)
            Names.AddRange(LegacyRaw.Split("¨"c))
            Dim Removed As Integer = Names.RemoveAll(Function(X) String.Equals(X.Trim(), Name, StringComparison.OrdinalIgnoreCase))
            If Removed = 0 Then Return "没有找到账号：" & Name
            Settings.Set("LoginLegacyName", String.Join("¨", Names))
            Return "已移除离线账号 " & Name
        Catch ex As Exception
            Return "移除账号失败：" & ex.Message
        End Try
    End Function

End Module