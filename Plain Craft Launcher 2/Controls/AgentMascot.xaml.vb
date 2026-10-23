Imports System.Windows.Media.Imaging

''' <summary>AI 助手「小瑟」的悬浮人偶与对话气泡。</summary>
Public Class AgentMascot

    Private IsDragging As Boolean = False
    Private DragOffset As Point
    Private DragStart As Point

    Public Sub New()
        InitializeComponent()
        AddHandler BtnAgentClose.Click, Sub() PanBubble.Visibility = Visibility.Collapsed
        AddHandler BtnAgentClear.Click, Sub() ClearMessages()
        AddHandler BtnSend.Click, Sub() SendMessage()
        AddHandler ImgMascot.MouseLeftButtonDown, AddressOf Mascot_MouseDown
        AddHandler ImgMascot.MouseMove, AddressOf Mascot_MouseMove
        AddHandler ImgMascot.MouseLeftButtonUp, AddressOf Mascot_MouseUp
        AddHandler SizeChanged, Sub() PositionMascot()
        ModAgent.Instance.ConfirmHandler = AddressOf ConfirmTool
    End Sub

    '工具执行前的确认（在 UI 线程弹窗，等待用户选择）
    Private Function ConfirmTool(Name As String, Args As String) As Boolean
        Dim Result As Boolean = False
        Dim Finished As New Threading.ManualResetEvent(False)
        RunInUi(Sub()
                   Dim Detail As String = Args
                   If Detail.Length > 300 Then Detail = Detail.Substring(0, 300) & "…"
                   Result = MyMsgBox("小瑟想要执行操作：" & Name & vbCrLf & vbCrLf & Detail, "AI 助理", "允许", "拒绝") = 1
                   Finished.Set()
               End Sub)
        Finished.WaitOne(120000)
        Return Result
    End Function
    ''' <summary>按设置刷新显示状态。</summary>
    Public Sub RefreshState()
        Visibility = If(Settings.Get(Of Boolean)("AgentEnabled"), Visibility.Visible, Visibility.Collapsed)
        If Visibility = Visibility.Visible Then
            SetMood("normal")
            PositionMascot()
        End If
    End Sub

    ''' <summary>切换表情：normal / happy / confused / angry / shy。</summary>
    Public Sub SetMood(Mood As String)
        Try
            ImgMascot.Source = New BitmapImage(New Uri("pack://application:,,,/PCLC;component/Images/Celia/celia_" & Mood & ".png"))
        Catch
        End Try
    End Sub

    '位置

    Private Sub PositionMascot()
        If ActualWidth <= 0 Then Return
        Dim X As Double = Settings.Get(Of Integer)("AgentPosX")
        Dim Y As Double = Settings.Get(Of Integer)("AgentPosY")
        If X < 0 Then X = ActualWidth - 120
        If Y < 0 Then Y = ActualHeight - 128
        X = Math.Max(0, Math.Min(ActualWidth - 96, X))
        Y = Math.Max(0, Math.Min(ActualHeight - 96, Y))
        Canvas.SetLeft(ImgMascot, X)
        Canvas.SetTop(ImgMascot, Y)
        PositionBubble()
    End Sub

    Private Sub PositionBubble()
        If PanBubble Is Nothing Then Return
        Dim X As Double = Canvas.GetLeft(ImgMascot)
        Dim Y As Double = Canvas.GetTop(ImgMascot)
        Dim MascotSize As Double = If(ImgMascot.ActualWidth > 0, ImgMascot.ActualWidth, 96)
        Dim BubbleWidth As Double = If(PanBubble.ActualWidth > 0, PanBubble.ActualWidth, 380)
        Dim BubbleHeight As Double = If(PanBubble.ActualHeight > 0, PanBubble.ActualHeight, 470)
        '优先放在人偶右侧，空间不足则放左侧
        Dim BubbleLeft As Double
        If X + MascotSize + 10 + BubbleWidth <= ActualWidth - 8 Then
            BubbleLeft = X + MascotSize + 10
        Else
            BubbleLeft = Math.Max(8, X - BubbleWidth - 10)
        End If
        '底边与人偶底部对齐
        Dim BubbleTop As Double = Y + MascotSize - BubbleHeight
        BubbleTop = Math.Max(8, Math.Min(ActualHeight - BubbleHeight - 8, BubbleTop))
        Canvas.SetLeft(PanBubble, Math.Max(8, Math.Min(ActualWidth - BubbleWidth - 8, BubbleLeft)))
        Canvas.SetTop(PanBubble, Math.Max(8, BubbleTop))
    End Sub

    '拖拽

    Private Sub Mascot_MouseDown(sender As Object, e As MouseButtonEventArgs)
        DragStart = e.GetPosition(Me)
        DragOffset = New Point(Canvas.GetLeft(ImgMascot), Canvas.GetTop(ImgMascot))
        IsDragging = False
        ImgMascot.CaptureMouse()
    End Sub

    Private Sub Mascot_MouseMove(sender As Object, e As MouseEventArgs)
        If Not ImgMascot.IsMouseCaptured Then Return
        Dim Now As Point = e.GetPosition(Me)
        If Not IsDragging AndAlso (Math.Abs(Now.X - DragStart.X) > 3 OrElse Math.Abs(Now.Y - DragStart.Y) > 3) Then IsDragging = True
        If Not IsDragging Then Return
        Dim X As Double = DragOffset.X + Now.X - DragStart.X
        Dim Y As Double = DragOffset.Y + Now.Y - DragStart.Y
        X = Math.Max(0, Math.Min(ActualWidth - 96, X))
        Y = Math.Max(0, Math.Min(ActualHeight - 96, Y))
        Canvas.SetLeft(ImgMascot, X)
        Canvas.SetTop(ImgMascot, Y)
        PositionBubble()
    End Sub

    Private Sub Mascot_MouseUp(sender As Object, e As MouseButtonEventArgs)
        ImgMascot.ReleaseMouseCapture()
        If IsDragging Then
            Settings.Set("AgentPosX", CInt(Canvas.GetLeft(ImgMascot)))
            Settings.Set("AgentPosY", CInt(Canvas.GetTop(ImgMascot)))
        Else
            ToggleBubble()
        End If
        IsDragging = False
    End Sub

    '气泡

    Private Sub ToggleBubble()
        If PanBubble.Visibility = Visibility.Visible Then
            PanBubble.Visibility = Visibility.Collapsed
            Return
        End If
        PanBubble.Visibility = Visibility.Visible
        PositionBubble()
        If PanMessages.Children.Count = 0 Then
            RenderHistory()
            If PanMessages.Children.Count = 0 Then
                AddBubble("你好，我是小瑟。可以帮你安装版本、排查问题，也可以直接聊天。", False)
            End If
        End If
        TxtInput.Focus()
    End Sub

    Private Sub ClearMessages()
        PanMessages.Children.Clear()
        ModAgent.Instance.Clear()
        AddBubble("对话已清空。", False)
    End Sub

    Private Function AddBubble(Text As String, IsUser As Boolean) As TextBlock
        Dim Content As New TextBlock With {.Text = Text, .TextWrapping = TextWrapping.Wrap, .FontSize = 13, .LineHeight = 20}
        Dim Box As New Border With {
            .Child = Content,
            .CornerRadius = New CornerRadius(6),
            .Padding = New Thickness(10, 7, 10, 7),
            .Margin = New Thickness(0, 4, 0, 4),
            .MaxWidth = 330,
            .HorizontalAlignment = If(IsUser, HorizontalAlignment.Right, HorizontalAlignment.Left),
            .Background = New MyColor(If(IsUser, "#E8EAFF", "#F4F5F8")),
            .VerticalAlignment = VerticalAlignment.Top
        }
        PanMessages.Children.Add(Box)
        PanScroll.ScrollToEnd()
        Return Content
    End Function

    Private Sub SendMessage()
        Dim Text As String = TxtInput.Text.Trim()
        If Text.Length = 0 Then Return
        If ModAgent.Instance.IsWaiting Then Return
        TxtInput.Text = ""
        AddBubble(Text, True)
        Dim Reply As TextBlock = AddBubble("", False)
        SetMood("confused")
        Dim Worker As New Threading.Thread(AddressOf SendWorker)
        Worker.IsBackground = True
        Worker.Start(New Object() {Text, Reply})
    End Sub

    Private Sub SendWorker(State As Object)
        Dim Args As Object() = CType(State, Object())
        Dim Text As String = CStr(Args(0))
        Dim Reply As TextBlock = CType(Args(1), TextBlock)
        ModAgent.Instance.Send(Text,
            Sub(Full)
                RunInUi(Sub() UpdateReply(Reply, Full))
            End Sub,
            Sub(ToolName)
                RunInUi(Sub() SetStatus("正在使用 " & ToolName & " …"))
            End Sub,
            Sub()
                RunInUi(Sub()
                            SetMood("happy")
                            SetStatus("")
                        End Sub)
            End Sub,
            Sub(Message)
                RunInUi(Sub()
                            Reply.Text = "出错了：" & Message
                            SetMood("angry")
                        End Sub)
            End Sub)
    End Sub

    Private Sub SetStatus(Text As String)
        If LabAgentStatus Is Nothing Then Return
        LabAgentStatus.Text = Text
        LabAgentStatus.Visibility = If(String.IsNullOrEmpty(Text), Visibility.Collapsed, Visibility.Visible)
    End Sub

    '把已保存的历史消息画出来
    Public Sub RenderHistory()
        ModAgent.Instance.EnsureInit()
        For Each Item In ModAgent.Instance.Raw
            Dim Role As String = Item("role")?.ToString()
            If Role = "user" Then
                AddBubble(Item("content")?.ToString(), True)
            ElseIf Role = "assistant" AndAlso Item("tool_calls") Is Nothing Then
                Dim Text As String = Item("content")?.ToString()
                If Not String.IsNullOrEmpty(Text) Then AddBubble(Text, False)
            End If
        Next
    End Sub
    Private Sub UpdateReply(Reply As TextBlock, Text As String)
        Reply.Text &= Text
        PanScroll.ScrollToEnd()
    End Sub

    Private Sub TxtInput_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter AndAlso Keyboard.Modifiers <> ModifierKeys.Shift Then
            e.Handled = True
            SendMessage()
        End If
    End Sub

End Class
