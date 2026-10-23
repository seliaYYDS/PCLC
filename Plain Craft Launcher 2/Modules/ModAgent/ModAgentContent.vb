Imports System.Net
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>AI 助理的社区资源工具：搜索、安装、删除（基于 Modrinth 开放接口）。</summary>
Public Module ModAgentContent

    Private Const ModrinthApi As String = "https://api.modrinth.com/v2/"
    Private Const UserAgent As String = "PCLC/2.14 (AI Assistant; +https://github.com/Meloong-Git/PCL)"

    Public Sub Register(List As List(Of AgentTool))
        '搜索
        Dim SearchSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"query", New JObject From {{"type", "string"}, {"description", "搜索关键词，可为空"}}},
                {"type", New JObject From {{"type", "string"}, {"description", "资源类型：mod/resourcepack/shader/datapack/modpack，默认 mod"}}},
                {"gameVersion", New JObject From {{"type", "string"}, {"description", "筛选 Minecraft 版本，如 1.20.1"}}},
                {"loader", New JObject From {{"type", "string"}, {"description", "筛选加载器：fabric/forge/neoforge/quilt，仅对 mod 有效"}}},
                {"limit", New JObject From {{"type", "integer"}, {"description", "返回数量，默认 10，最多 30"}}},
                {"offset", New JObject From {{"type", "integer"}, {"description", "跳过前多少条，用于翻页"}}},
                {"sort", New JObject From {{"type", "string"}, {"description", "排序：relevance/downloads/newest/updated，默认 relevance"}}},
                {"source", New JObject From {{"type", "string"}, {"description", "来源：modrinth/curseforge/both，默认 modrinth"}}}
            }}
        }
        List.Add(New AgentTool With {
            .Name = "search_content",
            .Description = "搜索 Mod、资源包、光影包、数据包、整合包（来源：Modrinth）",
            .Risk = 0,
            .Schema = SearchSchema,
            .Run = Function(Args) SearchContent(Args)})

        Dim VersionsSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"project", New JObject From {{"type", "string"}, {"description", "项目 slug 或 ID"}}},
                {"gameVersion", New JObject From {{"type", "string"}, {"description", "筛选 Minecraft 版本"}}},
                {"loader", New JObject From {{"type", "string"}, {"description", "筛选加载器"}}},
                {"limit", New JObject From {{"type", "integer"}, {"description", "返回数量，默认 10"}}},
                {"source", New JObject From {{"type", "string"}, {"description", "来源：modrinth/curseforge，默认按 project 形式：数字为 CurseForge"}}}
            }},
            {"required", New JArray From {"project"}}
        }
        List.Add(New AgentTool With {
            .Name = "list_content_versions",
            .Description = "列出某个项目可下载的版本，用于挑选后再安装",
            .Risk = 0,
            .Schema = VersionsSchema,
            .Run = Function(Args) ListVersions(Args)})

        Dim InstallSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"url", New JObject From {{"type", "string"}, {"description", "文件下载地址（来自 list_content_versions）"}}},
                {"fileName", New JObject From {{"type", "string"}, {"description", "保存的文件名，需带扩展名"}}},
                {"type", New JObject From {{"type", "string"}, {"description", "资源类型：mod/resourcepack/shader/datapack，默认 mod"}}},
                {"world", New JObject From {{"type", "string"}, {"description", "数据包安装到的存档名，仅 datapack 需要"}}},
                {"instance", New JObject From {{"type", "string"}, {"description", "目标版本名称，默认当前选中版本"}}}
            }},
            {"required", New JArray From {"url", "fileName"}}
        }
        List.Add(New AgentTool With {
            .Name = "install_content",
            .Description = "把资源文件下载并安装到当前版本的对应目录（自动考虑版本隔离）",
            .Risk = 2,
            .Schema = InstallSchema,
            .Run = Function(Args) InstallContent(Args)})

        Dim BatchSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"items", New JObject From {{"type", "array"}, {"items", New JObject From {{"type", "string"}}}, {"description", "项目 slug 或 ID 列表（Modrinth）"}}},
                {"gameVersion", New JObject From {{"type", "string"}, {"description", "目标 Minecraft 版本，默认取当前实例的版本"}}},
                {"loader", New JObject From {{"type", "string"}, {"description", "加载器：fabric/forge/neoforge/quilt，默认取当前实例"}}},
                {"type", New JObject From {{"type", "string"}, {"description", "资源类型，默认 mod"}}},
                {"instance", New JObject From {{"type", "string"}, {"description", "目标版本名称，默认当前选中版本"}}},
                {"withDependencies", New JObject From {{"type", "boolean"}, {"description", "是否自动安装必需前置，默认 true"}}}
            }},
            {"required", New JArray From {"items"}}
        }
        List.Add(New AgentTool With {
            .Name = "install_projects",
            .Description = "按项目批量安装资源并自动补全必需前置，一次可装多个；自动挑选匹配目标版本与加载器的文件",
            .Risk = 2,
            .Schema = BatchSchema,
            .Run = Function(Args) InstallProjects(Args)})

        Dim DeleteSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"fileName", New JObject From {{"type", "string"}, {"description", "要删除的文件名"}}},
                {"type", New JObject From {{"type", "string"}, {"description", "资源类型，默认 mod"}}},
                {"world", New JObject From {{"type", "string"}, {"description", "存档名，仅 datapack 需要"}}}
            }},
            {"required", New JArray From {"fileName"}}
        }
        List.Add(New AgentTool With {
            .Name = "delete_content",
            .Description = "删除当前版本目录下的资源文件",
            .Risk = 2,
            .Schema = DeleteSchema,
            .Run = Function(Args) DeleteContent(Args)})

        Dim PackSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"url", New JObject From {{"type", "string"}, {"description", "整合包文件下载地址（.mrpack/.zip）"}}},
                {"name", New JObject From {{"type", "string"}, {"description", "安装后的版本名称"}}}
            }},
            {"required", New JArray From {"url", "name"}}
        }
        List.Add(New AgentTool With {
            .Name = "install_modpack",
            .Description = "下载整合包并安装为一个新版本（自动处理加载器、模组与配置）",
            .Risk = 2,
            .Schema = PackSchema,
            .Run = Function(Args) InstallModpack(Args)})

        List.Add(New AgentTool With {
            .Name = "list_worlds",
            .Description = "列出当前版本目录下的存档，用于数据包安装",
            .Risk = 0,
            .Schema = New JObject From {{"type", "object"}, {"properties", New JObject From {{"instance", New JObject From {{"type", "string"}, {"description", "目标版本名称，默认当前选中版本"}}}}}},
            .Run = Function(Args) ListWorlds(Args)})
        Dim ListInstalledSchema As New JObject From {
            {"type", "object"},
            {"properties", New JObject From {
                {"type", New JObject From {{"type", "string"}, {"description", "资源类型，默认 mod"}}},
                {"world", New JObject From {{"type", "string"}, {"description", "存档名，仅 datapack 需要"}}},
                {"instance", New JObject From {{"type", "string"}, {"description", "目标版本名称，默认当前选中版本"}}}
            }}
        }
        List.Add(New AgentTool With {
            .Name = "list_installed_content",
            .Description = "列出当前版本目录下已安装的资源文件",
            .Risk = 0,
            .Schema = ListInstalledSchema,
            .Run = Function(Args) ListInstalled(Args)})
    End Sub

    Private Function ModKindName(Type As String) As String
        Select Case Type.ToLower()
            Case "resourcepack" : Return "资源包"
            Case "shader" : Return "光影包"
            Case "datapack" : Return "数据包"
            Case "modpack" : Return "整合包"
            Case Else : Return "模组"
        End Select
    End Function
    ' ---- 目录 ----

    Private Function FindInstance(Name As String) As McInstance
        If String.IsNullOrWhiteSpace(Name) Then Return McInstanceSelected
        For Each Pair In McInstanceList
            For Each Item In Pair.Value
                If String.Equals(Item.Name, Name.Trim(), StringComparison.OrdinalIgnoreCase) Then Return Item
            Next
        Next
        Return Nothing
    End Function

    ''' <summary>解析实例的独立目录；实例列表尚未加载时直接按 versions 目录兜底。</summary>
    Private Function ResolveBase(InstanceName As String) As String
        Dim Instance As McInstance = FindInstance(InstanceName)
        If Instance IsNot Nothing Then
            Return If(String.IsNullOrWhiteSpace(Instance.PathIndie), McFolderSelected, Instance.PathIndie)
        End If
        If Not String.IsNullOrWhiteSpace(InstanceName) Then
            Dim Folder As String = IO.Path.Combine(McFolderSelected, "versions", InstanceName.Trim())
            If IO.Directory.Exists(Folder) Then Return Folder
        End If
        Return McFolderSelected
    End Function

    Private Function ContentFolder(Type As String, World As String, InstanceName As String) As String
        Dim Base As String = ResolveBase(InstanceName)
        Select Case Type.ToLower()
            Case "mod" : Return IO.Path.Combine(Base, "mods")
            Case "resourcepack" : Return IO.Path.Combine(Base, "resourcepacks")
            Case "shader" : Return IO.Path.Combine(Base, "shaderpacks")
            Case "datapack"
                If String.IsNullOrWhiteSpace(World) Then Return Nothing
                Return IO.Path.Combine(Base, "saves", World, "datapacks")
            Case Else : Return Nothing
        End Select
    End Function

    ' ---- 网络 ----

    Private Function GetJson(Url As String) As JToken
        Dim Request As HttpWebRequest = CType(WebRequest.Create(Url), HttpWebRequest)
        Request.UserAgent = UserAgent
        Request.Accept = "application/json"
        Request.Timeout = 20000
        Request.ReadWriteTimeout = 20000
        Using Response = CType(Request.GetResponse(), HttpWebResponse)
            Using Reader As New IO.StreamReader(Response.GetResponseStream(), Encoding.UTF8)
                Return JToken.Parse(Reader.ReadToEnd())
            End Using
        End Using
    End Function

    Private Function TypeToProjectType(Type As String) As String
        Select Case Type.ToLower()
            Case "shader" : Return "shader"
            Case "resourcepack" : Return "resourcepack"
            Case "datapack" : Return "datapack"
            Case "modpack" : Return "modpack"
            Case Else : Return "mod"
        End Select
    End Function

    ' ---- 搜索 ----

    Private Function SearchContent(Args As JObject) As String
        Try
            Dim Source As String = If(Args("source") Is Nothing, "modrinth", Args("source").ToString().ToLower())
            If Source = "curseforge" Then Return SearchCurseForge(Args)
            Dim Query As String = If(Args("query") Is Nothing, "", Args("query").ToString())
            Dim Type As String = If(Args("type") Is Nothing, "mod", Args("type").ToString())
            Dim GameVersion As String = If(Args("gameVersion") Is Nothing, "", Args("gameVersion").ToString())
            Dim Loader As String = If(Args("loader") Is Nothing, "", Args("loader").ToString())
            Dim Limit As Integer = Math.Min(30, Math.Max(1, If(Args("limit") Is Nothing, 10, Args("limit").ToObject(Of Integer)())))
            Dim Offset As Integer = Math.Max(0, If(Args("offset") Is Nothing, 0, Args("offset").ToObject(Of Integer)()))
            Dim Sort As String = If(Args("sort") Is Nothing, "relevance", Args("sort").ToString())

            Dim Facets As New JArray()
            Facets.Add(New JArray From {"project_type:" & TypeToProjectType(Type)})
            If GameVersion <> "" Then Facets.Add(New JArray From {"versions:" & GameVersion})
            If Loader <> "" AndAlso TypeToProjectType(Type) = "mod" Then Facets.Add(New JArray From {"categories:" & Loader.ToLower()})

            Dim Url As String = ModrinthApi & "search?limit=" & Limit & "&offset=" & Offset & "&index=" & Uri.EscapeDataString(Sort) &
                                "&query=" & Uri.EscapeDataString(Query) &
                                "&facets=" & Uri.EscapeDataString(Facets.ToString(Newtonsoft.Json.Formatting.None))
            Dim Json As JToken = GetJson(Url)
            Dim Hits As JArray = TryCast(Json("hits"), JArray)
            If Hits Is Nothing Then Return "搜索失败：返回格式异常"
            Dim Result As New JArray()
            For Each Hit In Hits
                Result.Add(New JObject From {
                    {"slug", Hit("slug")?.ToString()},
                    {"title", Hit("title")?.ToString()},
                    {"description", Hit("description")?.ToString()},
                    {"downloads", Hit("downloads")},
                    {"author", Hit("author")?.ToString()},
                    {"versions", Hit("versions")}
                })
            Next
            Dim Wrapper As New JObject From {{"total", Json("total_hits")}, {"offset", Offset}, {"hits", Result}}
            Return Wrapper.ToString(Newtonsoft.Json.Formatting.Indented)
        Catch ex As Exception
            Return "搜索失败：" & ex.Message
        End Try
    End Function

    Private Function ListVersions(Args As JObject) As String
        Try
            Dim Project As String = Args("project")?.ToString()
            If String.IsNullOrWhiteSpace(Project) Then Return "缺少 project 参数"
            Dim Source As String = If(Args("source") Is Nothing, If(Project.All(Function(C) Char.IsDigit(C)), "curseforge", "modrinth"), Args("source").ToString().ToLower())
            If Source = "curseforge" Then Return ListCurseForgeFiles(Project, Args)
            Dim GameVersion As String = If(Args("gameVersion") Is Nothing, "", Args("gameVersion").ToString())
            Dim Loader As String = If(Args("loader") Is Nothing, "", Args("loader").ToString())
            Dim Limit As Integer = Math.Min(30, Math.Max(1, If(Args("limit") Is Nothing, 10, Args("limit").ToObject(Of Integer)())))

            Dim Url As String = ModrinthApi & "project/" & Uri.EscapeDataString(Project) & "/version"
            Dim Query As New List(Of String)
            If GameVersion <> "" Then Query.Add("game_versions=" & Uri.EscapeDataString("[""" & GameVersion & """]"))
            If Loader <> "" Then Query.Add("loaders=" & Uri.EscapeDataString("[""" & Loader.ToLower() & """]"))
            If Query.Count > 0 Then Url &= "?" & String.Join("&", Query)

            Dim Arr As JArray = TryCast(GetJson(Url), JArray)
            If Arr Is Nothing Then Return "获取版本失败"
            Dim Result As New JArray()
            For Each Item In Arr
                If Result.Count >= Limit Then Exit For
                Dim Files As JArray = TryCast(Item("files"), JArray)
                If Files Is Nothing OrElse Files.Count = 0 Then Continue For
                Dim Primary = Files(0)
                Result.Add(New JObject From {
                    {"version", Item("version_number")?.ToString()},
                    {"name", Item("name")?.ToString()},
                    {"gameVersions", Item("game_versions")},
                    {"loaders", Item("loaders")},
                    {"url", Primary("url")?.ToString()},
                    {"fileName", Primary("filename")?.ToString()},
                    {"projectId", Item("project_id")?.ToString()},
                    {"dependencies", Item("dependencies")}
                })
            Next
            Return Result.ToString(Newtonsoft.Json.Formatting.Indented)
        Catch ex As Exception
            Return "获取版本失败：" & ex.Message
        End Try
    End Function

    ' ---- CurseForge ----

    Private Function CurseForgeClassId(Type As String) As Integer
        Select Case TypeToProjectType(Type)
            Case "modpack" : Return 4471
            Case "datapack" : Return 6945
            Case "shader" : Return 6552
            Case "resourcepack" : Return 12
            Case Else : Return 6
        End Select
    End Function

    Private Function CurseForgeLoaderId(Loader As String) As Integer
        Select Case Loader.ToLower()
            Case "forge" : Return 1
            Case "fabric" : Return 4
            Case "quilt" : Return 5
            Case "neoforge" : Return 6
            Case Else : Return 0
        End Select
    End Function

    Private Function SearchCurseForge(Args As JObject) As String
        Try
            Dim Query As String = If(Args("query") Is Nothing, "", Args("query").ToString())
            Dim Type As String = If(Args("type") Is Nothing, "mod", Args("type").ToString())
            Dim GameVersion As String = If(Args("gameVersion") Is Nothing, "", Args("gameVersion").ToString())
            Dim Loader As String = If(Args("loader") Is Nothing, "", Args("loader").ToString())
            Dim Limit As Integer = Math.Min(30, Math.Max(1, If(Args("limit") Is Nothing, 10, Args("limit").ToObject(Of Integer)())))
            Dim Offset As Integer = Math.Max(0, If(Args("offset") Is Nothing, 0, Args("offset").ToObject(Of Integer)()))
            Dim Url As String = "https://api.curseforge.com/v1/mods/search?gameId=432&pageSize=" & Limit & "&index=" & Offset &
                                "&classId=" & CurseForgeClassId(Type)
            If Query <> "" Then Url &= "&searchFilter=" & Uri.EscapeDataString(Query)
            If GameVersion <> "" Then Url &= "&gameVersion=" & Uri.EscapeDataString(GameVersion)
            If Loader <> "" Then
                Dim LoaderId As Integer = CurseForgeLoaderId(Loader)
                If LoaderId > 0 Then Url &= "&modLoaderType=" & LoaderId
            End If
            Dim Json As JToken = DlModRequest(Url)
            Dim Data As JArray = TryCast(Json("data"), JArray)
            If Data Is Nothing Then Return "搜索失败：返回格式异常"
            Dim Hits As New JArray()
            For Each Item In Data
                Hits.Add(New JObject From {
                    {"id", Item("id")},
                    {"slug", Item("slug")?.ToString()},
                    {"title", Item("name")?.ToString()},
                    {"description", Item("summary")?.ToString()},
                    {"downloads", Item("downloadCount")},
                    {"source", "curseforge"}
                })
            Next
            Dim Total As JToken = Json("pagination")?.Item("totalCount")
            Dim Wrapper As New JObject From {{"source", "curseforge"}, {"total", If(Total, JValue.CreateNull())}, {"offset", Offset}, {"hits", Hits}}
            Return Wrapper.ToString(Newtonsoft.Json.Formatting.Indented)
        Catch ex As Exception
            Return "搜索失败（CurseForge）：" & ex.Message
        End Try
    End Function

    Private Function ListCurseForgeFiles(Project As String, Args As JObject) As String
        Try
            Dim GameVersion As String = If(Args("gameVersion") Is Nothing, "", Args("gameVersion").ToString())
            Dim Loader As String = If(Args("loader") Is Nothing, "", Args("loader").ToString())
            Dim Limit As Integer = Math.Min(30, Math.Max(1, If(Args("limit") Is Nothing, 10, Args("limit").ToObject(Of Integer)())))
            Dim Url As String = "https://api.curseforge.com/v1/mods/" & Uri.EscapeDataString(Project) & "/files?pageSize=" & Limit
            If GameVersion <> "" Then Url &= "&gameVersion=" & Uri.EscapeDataString(GameVersion)
            If Loader <> "" Then
                Dim LoaderId As Integer = CurseForgeLoaderId(Loader)
                If LoaderId > 0 Then Url &= "&modLoaderType=" & LoaderId
            End If
            Dim Json As JToken = DlModRequest(Url)
            Dim Data As JArray = TryCast(Json("data"), JArray)
            If Data Is Nothing Then Return "获取版本失败"
            Dim Result As New JArray()
            For Each Item In Data
                Result.Add(New JObject From {
                    {"fileId", Item("id")},
                    {"version", Item("displayName")?.ToString()},
                    {"fileName", Item("fileName")?.ToString()},
                    {"url", Item("downloadUrl")?.ToString()},
                    {"gameVersions", Item("gameVersions")},
                    {"releaseType", Item("releaseType")},
                    {"source", "curseforge"}
                })
            Next
            Return Result.ToString(Newtonsoft.Json.Formatting.Indented)
        Catch ex As Exception
            Return "获取版本失败（CurseForge）：" & ex.Message
        End Try
    End Function
    ' ---- 安装 ----

    Private Function InstallContent(Args As JObject) As String
        Try
            Dim Url As String = Args("url")?.ToString()
            Dim FileName As String = Args("fileName")?.ToString()
            If String.IsNullOrWhiteSpace(Url) OrElse String.IsNullOrWhiteSpace(FileName) Then Return "缺少 url 或 fileName 参数"
            If Url.StartsWith("null") Then Return "该文件未提供直接下载地址（作者可能关闭了第三方下载），请改为在启动器下载页安装"
            If Not Url.StartsWith("http") Then Return "下载地址无效：" & Url
            If IO.Path.GetFileName(FileName) <> FileName Then Return "fileName 不能包含路径"
            Dim Type As String = If(Args("type") Is Nothing, "mod", Args("type").ToString())
            Dim World As String = If(Args("world") Is Nothing, "", Args("world").ToString())
            Dim InstanceName As String = If(Args("instance") Is Nothing, "", Args("instance").ToString())
            Dim Folder As String = ContentFolder(Type, World, InstanceName)
            If Folder Is Nothing Then Return "datapack 需要提供 world 参数" & If(Type.ToLower() = "datapack", "", "，或资源类型不受支持")
            IO.Directory.CreateDirectory(Folder)
            Dim Target As String = IO.Path.Combine(Folder, FileName)
            If IO.File.Exists(Target) Then Return "文件已存在：" & FileName

            DownloadFile(Url, Target)
            ModAgentChanges.Added(ModKindName(Type), FileName)
            Return "已安装到 " & Target & "（" & New IO.FileInfo(Target).Length & " 字节）"
        Catch ex As Exception
            Return "安装失败：" & ex.Message
        End Try
    End Function

    Private Sub DownloadFile(Url As String, Target As String)
        Dim Request As HttpWebRequest = CType(WebRequest.Create(Url), HttpWebRequest)
        Request.UserAgent = UserAgent
        Request.Timeout = 120000
        Request.ReadWriteTimeout = 120000
        Using Response = CType(Request.GetResponse(), HttpWebResponse)
            Using Input = Response.GetResponseStream()
                Using Output = IO.File.Create(Target)
                    Input.CopyTo(Output)
                End Using
            End Using
        End Using
    End Sub

    Private Function InstallModpack(Args As JObject) As String
        Try
            Dim Url As String = Args("url")?.ToString()
            Dim Name As String = Args("name")?.ToString()
            If String.IsNullOrWhiteSpace(Url) OrElse String.IsNullOrWhiteSpace(Name) Then Return "缺少 url 或 name 参数"
            Name = Name.Trim()
            If Name.IndexOfAny(IO.Path.GetInvalidFileNameChars()) >= 0 Then Return "版本名称包含非法字符"
            If IO.Directory.Exists(McFolderSelected & "versions\" & Name & "") Then Return "版本已存在：" & Name
            Dim Extension As String = If(Url.ToLower().EndsWith(".zip"), ".zip", ".mrpack")
            Dim Temp As String = IO.Path.Combine(IO.Path.GetTempPath(), "PCLC_pack_" & Guid.NewGuid().ToString("N") & Extension)
            DownloadFile(Url, Temp)
            Dim Loader = ModpackInstall(Temp, Name)
            If Loader Is Nothing Then Return "解析整合包失败，可能该文件不是受支持的整合包格式"
            Loader.Start(McFolderSelected & "versions\" & Name & "")
            LoaderTaskbarAdd(Loader)
            RunInUi(Sub() FrmMain.PageChange(FormMain.PageType.TaskManager))
            ModAgentChanges.Added("整合包", Name)
            Return "已开始安装整合包 " & Name & "，可在任务管理器中查看进度"
        Catch ex As Exception
            Return "整合包安装失败：" & ex.Message
        End Try
    End Function

    Private Function ListWorlds(Args As JObject) As String
        Try
            Dim InstanceName As String = If(Args("instance") Is Nothing, "", Args("instance").ToString())
            Dim Base As String = ResolveBase(InstanceName)
            Dim Saves As String = IO.Path.Combine(Base, "saves")
            If Not IO.Directory.Exists(Saves) Then Return "当前没有存档目录：" & Saves
            Dim Items As New JArray()
            For Each Folder In IO.Directory.GetDirectories(Saves)
                Items.Add(IO.Path.GetFileName(Folder))
            Next
            Return Items.ToString(Newtonsoft.Json.Formatting.Indented)
        Catch ex As Exception
            Return "读取存档失败：" & ex.Message
        End Try
    End Function
    Private Function FetchVersions(Project As String, GameVersion As String, Loader As String) As JArray
        Dim Url As String = ModrinthApi & "project/" & Uri.EscapeDataString(Project) & "/version"
        Dim Query As New List(Of String)
        If GameVersion <> "" Then Query.Add("game_versions=" & Uri.EscapeDataString("[""" & GameVersion & """]"))
        If Loader <> "" Then Query.Add("loaders=" & Uri.EscapeDataString("[""" & Loader.ToLower() & """]"))
        If Query.Count > 0 Then Url &= "?" & String.Join("&", Query)
        Return TryCast(GetJson(Url), JArray)
    End Function

    Private Function PickRelease(Versions As JArray) As JToken
        If Versions Is Nothing OrElse Versions.Count = 0 Then Return Nothing
        For Each Item In Versions
            If Item("version_type")?.ToString() = "release" Then Return Item
        Next
        Return Versions(0)
    End Function

    Private ReadOnly SlugCache As New Dictionary(Of String, String)

    Private Function ResolveSlug(ProjectId As String) As String
        If String.IsNullOrWhiteSpace(ProjectId) Then Return Nothing
        SyncLock SlugCache
            If SlugCache.ContainsKey(ProjectId) Then Return SlugCache(ProjectId)
        End SyncLock
        Try
            Dim Json As JToken = GetJson(ModrinthApi & "project/" & Uri.EscapeDataString(ProjectId))
            Dim Slug As String = Json("slug")?.ToString()
            SyncLock SlugCache
                SlugCache(ProjectId) = Slug
            End SyncLock
            Return Slug
        Catch
            Return Nothing
        End Try
    End Function

    Private Function InstallProjects(Args As JObject) As String
        Try
            Dim Items As JArray = TryCast(Args("items"), JArray)
            If Items Is Nothing OrElse Items.Count = 0 Then Return "缺少 items 参数"
            Dim InstanceName As String = If(Args("instance") Is Nothing, "", Args("instance").ToString())
            Dim Target As McInstance = FindInstance(InstanceName)
            If Target Is Nothing Then Return "没有找到版本：" & InstanceName
            Dim GameVersion As String = If(Args("gameVersion") Is Nothing, "", Args("gameVersion").ToString())
            If GameVersion = "" AndAlso Target.Version IsNot Nothing Then GameVersion = Target.Version.VanillaName
            Dim Loader As String = If(Args("loader") Is Nothing, "", Args("loader").ToString())
            If Loader = "" AndAlso Target.Version IsNot Nothing Then
                If Target.Version.ModLoaderVersions.ContainsKey(ModLoaders.Fabric) Then Loader = "fabric"
                If Target.Version.ModLoaderVersions.ContainsKey(ModLoaders.Forge) Then Loader = "forge"
                If Target.Version.ModLoaderVersions.ContainsKey(ModLoaders.NeoForge) Then Loader = "neoforge"
            End If
            Dim TypeName As String = If(Args("type") Is Nothing, "mod", Args("type").ToString())
            Dim WithDeps As Boolean = Args("withDependencies") Is Nothing OrElse Args("withDependencies").ToObject(Of Boolean)()
            Dim Folder As String = ContentFolder(TypeName, "", InstanceName)
            If Folder Is Nothing Then Return "无法确定目标目录"
            IO.Directory.CreateDirectory(Folder)

            Dim Queue As New Queue(Of String)
            For Each Item In Items
                Dim Slug As String = Item.ToString().Trim()
                If Slug <> "" Then Queue.Enqueue(Slug)
            Next
            Dim Visited As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim Installed As New JArray()
            Dim Failed As New JArray()
            While Queue.Count > 0 AndAlso Visited.Count < 40
                Dim Slug As String = Queue.Dequeue()
                If Not Visited.Add(Slug) Then Continue While
                Dim Versions As JArray = FetchVersions(Slug, GameVersion, Loader)
                Dim Version As JToken = PickRelease(Versions)
                If Version Is Nothing Then
                    Failed.Add(New JObject From {{"project", Slug}, {"reason", "没有匹配 " & GameVersion & "/" & Loader & " 的版本"}})
                    Continue While
                End If
                Dim Files As JArray = TryCast(Version("files"), JArray)
                If Files Is Nothing OrElse Files.Count = 0 Then
                    Failed.Add(New JObject From {{"project", Slug}, {"reason", "版本缺少文件"}})
                    Continue While
                End If
                Dim Primary As JToken = Files(0)
                Dim FileName As String = Primary("filename")?.ToString()
                Dim FileUrl As String = Primary("url")?.ToString()
                If String.IsNullOrWhiteSpace(FileName) OrElse String.IsNullOrWhiteSpace(FileUrl) Then
                    Failed.Add(New JObject From {{"project", Slug}, {"reason", "文件地址无效"}})
                    Continue While
                End If
                Dim TargetFile As String = IO.Path.Combine(Folder, IO.Path.GetFileName(FileName))
                If IO.File.Exists(TargetFile) Then
                    Installed.Add(New JObject From {{"project", Slug}, {"file", FileName}, {"note", "已存在"}})
                Else
                    Try
                        DownloadFile(FileUrl, TargetFile)
                        Installed.Add(New JObject From {{"project", Slug}, {"file", FileName}})
                    Catch ex As Exception
                        Failed.Add(New JObject From {{"project", Slug}, {"reason", "下载失败：" & ex.Message}})
                        Continue While
                    End Try
                    ModAgentChanges.Added(ModKindName(TypeName), FileName)
                End If
                If WithDeps Then
                    For Each Dep In CType(If(Version("dependencies"), New JArray()), JArray)
                        If Dep("dependency_type")?.ToString() <> "required" Then Continue For
                        Dim DepId As String = Dep("project_id")?.ToString()
                        Dim DepSlug As String = ResolveSlug(DepId)
                        If Not String.IsNullOrWhiteSpace(DepSlug) AndAlso Not Visited.Contains(DepSlug) Then Queue.Enqueue(DepSlug)
                    Next
                End If
            End While
            Dim Result As New JObject From {
                {"gameVersion", GameVersion}, {"loader", Loader}, {"folder", Folder},
                {"installed", Installed}, {"failed", Failed}
            }
            Return Result.ToString(Newtonsoft.Json.Formatting.Indented)
        Catch ex As Exception
            Return "批量安装失败：" & ex.Message
        End Try
    End Function

    Private Function DeleteContent(Args As JObject) As String
        Try
            Dim FileName As String = Args("fileName")?.ToString()
            If String.IsNullOrWhiteSpace(FileName) Then Return "缺少 fileName 参数"
            Dim Type As String = If(Args("type") Is Nothing, "mod", Args("type").ToString())
            Dim World As String = If(Args("world") Is Nothing, "", Args("world").ToString())
            Dim InstanceName As String = If(Args("instance") Is Nothing, "", Args("instance").ToString())
            Dim Folder As String = ContentFolder(Type, World, InstanceName)
            If Folder Is Nothing Then Return "无法确定目标目录"
            Dim Target As String = IO.Path.Combine(Folder, IO.Path.GetFileName(FileName))
            If Not IO.File.Exists(Target) Then Return "文件不存在：" & FileName
            IO.File.Delete(Target)
            ModAgentChanges.Removed(ModKindName(Type), FileName)
            Return "已删除 " & FileName
        Catch ex As Exception
            Return "删除失败：" & ex.Message
        End Try
    End Function

    Private Function ListInstalled(Args As JObject) As String
        Try
            Dim Type As String = If(Args("type") Is Nothing, "mod", Args("type").ToString())
            Dim World As String = If(Args("world") Is Nothing, "", Args("world").ToString())
            Dim InstanceName As String = If(Args("instance") Is Nothing, "", Args("instance").ToString())
            Dim Folder As String = ContentFolder(Type, World, InstanceName)
            If Folder Is Nothing Then Return "没有找到版本或目录不受支持"
            If Not IO.Directory.Exists(Folder) Then Return "目录不存在：" & Folder
            Dim Items As New JArray()
            For Each File In IO.Directory.GetFiles(Folder)
                Items.Add(New JObject From {
                    {"name", IO.Path.GetFileName(File)},
                    {"size", New IO.FileInfo(File).Length}
                })
            Next
            Return Items.ToString(Newtonsoft.Json.Formatting.Indented)
        Catch ex As Exception
            Return "列出失败：" & ex.Message
        End Try
    End Function

End Module
