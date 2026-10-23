Imports System.Windows.Media.Animation
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
        FollowTimer.Start()
        AddSway(PanInput, 4.0, 3.0)
        AddHandler Me.Loaded, Sub() RefreshState()
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
        If X < 0 Then X = ActualWidth - 164
        If Y < 0 Then Y = ActualHeight - 172
        X = Math.Max(0, Math.Min(ActualWidth - 140, X))
        Y = Math.Max(0, Math.Min(ActualHeight - 140, Y))
        Canvas.SetLeft(ImgMascot, X)
        Canvas.SetTop(ImgMascot, Y)
        PositionBubble()
    End Sub

    '气泡跟随：定时插值，避免每帧重启动画造成卡顿
    Private ReadOnly FollowTimer As New Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(16)}
    Private TargetLeft As Double = -1
    Private TargetTop As Double = -1
    Private HasPosition As Boolean = False

    Private Sub PositionBubble()
        If PanBubble Is Nothing Then Return
        Dim X As Double = Canvas.GetLeft(ImgMascot)
        Dim Y As Double = Canvas.GetTop(ImgMascot)
        Dim MascotSize As Double = If(ImgMascot.ActualWidth > 0, ImgMascot.ActualWidth, 140)
        Dim BubbleWidth As Double = If(PanBubble.ActualWidth > 0, PanBubble.ActualWidth, 380)
        Dim BubbleHeight As Double = If(PanBubble.ActualHeight > 0, PanBubble.ActualHeight, 470)
        Dim BubbleLeft As Double
        If X + MascotSize + 10 + BubbleWidth <= ActualWidth - 8 Then
            BubbleLeft = X + MascotSize + 10
        Else
            BubbleLeft = Math.Max(8, X - BubbleWidth - 10)
        End If
        Dim BubbleTop As Double = Y + MascotSize - BubbleHeight
        BubbleTop = Math.Max(8, Math.Min(ActualHeight - BubbleHeight - 8, BubbleTop))
        TargetLeft = Math.Max(8, Math.Min(ActualWidth - BubbleWidth - 8, BubbleLeft))
        TargetTop = Math.Max(8, BubbleTop)
    End Sub

    Private Sub FollowTimer_Tick(sender As Object, e As EventArgs)
        If PanBubble Is Nothing OrElse PanBubble.Visibility <> Visibility.Visible Then Return
        If TargetLeft < 0 Then Return
        If Not HasPosition Then
            Canvas.SetLeft(PanBubble, TargetLeft)
            Canvas.SetTop(PanBubble, TargetTop)
            HasPosition = True
            Return
        End If
        Dim CurrentLeft As Double = Canvas.GetLeft(PanBubble)
        Dim CurrentTop As Double = Canvas.GetTop(PanBubble)
        If Double.IsNaN(CurrentLeft) Then CurrentLeft = TargetLeft
        If Double.IsNaN(CurrentTop) Then CurrentTop = TargetTop
        Dim DeltaX As Double = (TargetLeft - CurrentLeft) * 0.18
        Dim DeltaY As Double = (TargetTop - CurrentTop) * 0.18
        If Math.Abs(DeltaX) < 0.4 AndAlso Math.Abs(DeltaY) < 0.4 Then
            Canvas.SetLeft(PanBubble, TargetLeft)
            Canvas.SetTop(PanBubble, TargetTop)
        Else
            Canvas.SetLeft(PanBubble, CurrentLeft + DeltaX)
            Canvas.SetTop(PanBubble, CurrentTop + DeltaY)
        End If
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
        X = Math.Max(0, Math.Min(ActualWidth - 140, X))
        Y = Math.Max(0, Math.Min(ActualHeight - 140, Y))
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
        HasPosition = False
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
        AddSway(Box, 3.0 + (PanMessages.Children.Count Mod 3) * 0.6, 2.4 + (PanMessages.Children.Count Mod 4) * 0.3)
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

    Private Shared Sub AddSway(Target As FrameworkElement, Amplitude As Double, Seconds As Double)
        Try
            If Target Is Nothing Then Return
            Dim Transform As New TranslateTransform()
            Target.RenderTransform = Transform
            Dim Sway As New DoubleAnimation(Amplitude, -Amplitude, New Duration(TimeSpan.FromSeconds(Seconds))) With {
                .AutoReverse = True,
                .RepeatBehavior = RepeatBehavior.Forever,
                .EasingFunction = New SineEase With {.EasingMode = EasingMode.EaseInOut},
                .BeginTime = TimeSpan.FromMilliseconds(New Random().Next(0, 1500))}
            Transform.BeginAnimation(TranslateTransform.XProperty, Sway)
        Catch
        End Try
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
