''' <summary>AI 助手的模型提供商预设。</summary>
Public Class AgentProviderPreset
    Public Id As String
    Public Name As String
    Public BaseUrl As String
    Public Models As String()
End Class

Public Module ModAgentProviders
    Public ReadOnly Presets As New List(Of AgentProviderPreset)

    Sub New()
        Presets.Add(New AgentProviderPreset With {.Id = "openai", .Name = "OpenAI", .BaseUrl = "https://api.openai.com/v1",
            .Models = {"gpt-4o-mini", "gpt-4o", "gpt-4.1-mini"}})
        Presets.Add(New AgentProviderPreset With {.Id = "deepseek", .Name = "DeepSeek", .BaseUrl = "https://api.deepseek.com/v1",
            .Models = {"deepseek-chat", "deepseek-reasoner"}})
        Presets.Add(New AgentProviderPreset With {.Id = "zhipu", .Name = "智谱 GLM", .BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            .Models = {"glm-4.5", "glm-4-flash"}})
        Presets.Add(New AgentProviderPreset With {.Id = "moonshot", .Name = "月之暗面", .BaseUrl = "https://api.moonshot.cn/v1",
            .Models = {"moonshot-v1-8k", "moonshot-v1-32k"}})
        Presets.Add(New AgentProviderPreset With {.Id = "siliconflow", .Name = "硅基流动", .BaseUrl = "https://api.siliconflow.cn/v1",
            .Models = {"deepseek-ai/DeepSeek-V3"}})
        Presets.Add(New AgentProviderPreset With {.Id = "openrouter", .Name = "OpenRouter", .BaseUrl = "https://openrouter.ai/api/v1",
            .Models = {"openai/gpt-4o-mini"}})
        Presets.Add(New AgentProviderPreset With {.Id = "ollama", .Name = "Ollama（本地）", .BaseUrl = "http://127.0.0.1:11434/v1",
            .Models = {"qwen2.5:7b", "llama3.1:8b"}})
        Presets.Add(New AgentProviderPreset With {.Id = "custom", .Name = "自定义", .BaseUrl = "", .Models = {}})
    End Sub

    ''' <summary>按 Id 取预设，找不到返回自定义。</summary>
    Public Function GetPreset(Index As Integer) As AgentProviderPreset
        If Index < 0 OrElse Index >= Presets.Count Then Return Presets(0)
        Return Presets(Index)
    End Function

    ''' <summary>当前生效的 BaseUrl（未填写时回落到预设）。</summary>
    Public Function CurrentBaseUrl() As String
        Dim Custom As String = Settings.Get(Of String)("AgentBaseUrl")
        If Not String.IsNullOrWhiteSpace(Custom) Then Return Custom.Trim().TrimEnd("/")
        Return GetPreset(Settings.Get(Of Integer)("AgentProvider")).BaseUrl.TrimEnd("/")
    End Function

    ''' <summary>当前生效的模型名（未填写时回落到预设第一个）。</summary>
    Public Function CurrentModel() As String
        Dim Custom As String = Settings.Get(Of String)("AgentModel")
        If Not String.IsNullOrWhiteSpace(Custom) Then Return Custom.Trim()
        Dim Models = GetPreset(Settings.Get(Of Integer)("AgentProvider")).Models
        If Models.Length > 0 Then Return Models(0)
        Return ""
    End Function
End Module
