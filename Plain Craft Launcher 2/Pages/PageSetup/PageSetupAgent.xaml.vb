Public Class PageSetupAgent

    Private IsInited As Boolean = False

    Private Sub PageSetupAgent_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        SettingService.RefreshSettings(Me)
        If IsInited Then Return
        IsInited = True
        ComboAgentModel.Text = Settings.Get(Of String)("AgentModel")
        AddHandler ComboAgentModel.TextChanged, Sub(s, args) Settings.Set("AgentModel", If(ComboAgentModel.Text, ""))
        AddHandler BtnGetModels.Click, Sub() FetchModels()
    End Sub

    '从接口拉取可用模型列表
    Private Sub FetchModels()
        BtnGetModels.IsEnabled = False
        Hint("正在获取模型列表……", Log:=False)
        Dim Worker As New Threading.Thread(
            Sub()
                Try
                    Dim Models As List(Of String) = ModAgentProviders.FetchModels()
                    RunInUi(Sub() ApplyModels(Models))
                Catch ex As Exception
                    Dim Message As String = ex.Message
                    RunInUi(Sub() Hint("获取模型失败：" & Message, HintType.Red))
                Finally
                    RunInUi(Sub() BtnGetModels.IsEnabled = True)
                End Try
            End Sub) With {.IsBackground = True}
        Worker.Start()
    End Sub

    Private Sub ApplyModels(Models As List(Of String))
        Try
            ComboAgentModel.Items.Clear()
            For Each Id In Models
                ComboAgentModel.Items.Add(New MyComboBoxItem With {.Content = Id})
            Next
            If Models.Count = 0 Then
                Hint("接口没有返回可用模型", HintType.Red)
                Return
            End If
            ComboAgentModel.SelectedIndex = 0
            Settings.Set("AgentModel", Models(0))
            Hint($"已获取 {Models.Count} 个模型", HintType.Green)
        Catch ex As Exception
            Logger.Warn(ex, "填充模型列表失败")
        End Try
    End Sub

End Class
