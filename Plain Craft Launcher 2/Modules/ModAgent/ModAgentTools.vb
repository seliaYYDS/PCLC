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

End Module
