Imports System.Windows.Media.Animation
Imports System.Windows.Media.Imaging

''' <summary>AI 助手「小瑟」：可拖拽人偶 + 独立浮动消息气泡。</summary>
Public Class AgentMascot

    Private IsDragging As Boolean = False
    Private DragOffset As Point
    Private DragStart As Point

    '消息：每条独立容器，不参与任何统一滚动
    Private ReadOnly Messages As New List(Of Border)
    Private HiddenNewest As Integer = 0

    '跟随
    Private WithEvents FollowTimer As New Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(16)}
    Private AnchorX As Double = 0
    Private AnchorY As Double = 0
    Private CurrentX As Double = 0
    Private CurrentY As Double = 0
    Private TargetX As Double = 0
    Private TargetY As Double = 0
    Private HasPosition As Boolean = False
    Private ReadOnly LagX As New List(Of Double)
    Private ReadOnly LagY As New List(Of Double)

    Public Sub New()
        InitializeComponent()
        AddHandler BtnAgentClose.Click, Sub() HideBubble()
        AddHandler BtnAgentClear.Click, Sub() ClearMessages()
        AddHandler BtnSend.Click, Sub() SendMessage()
        AddHandler ImgMascot.MouseLeftButtonDown, AddressOf Mascot_MouseDown
        AddHandler ImgMascot.MouseMove, AddressOf Mascot_MouseMove
        AddHandler ImgMascot.MouseLeftButtonUp, AddressOf Mascot_MouseUp
        AddHandler Me.PreviewMouseWheel, AddressOf OnWheel
        AddHandler SizeChanged, Sub() PositionMascot()
        AddHandler Me.Loaded, Sub() RefreshState()
        ModAgent.Instance.ConfirmHandler = AddressOf ConfirmTool
        FollowTimer.Start()
        AddSway(PanInput, 3.0, 3.0)
    End Sub

    ''' <summary>按设置刷新显示状态。</summary>
    Public Sub RefreshState()
        Visibility = If(Settings.Get(Of Boolean)("AgentEnabled"), Visibility.Visible, Visibility.Collapsed)
        If Visibility = Visibility.Visible Then
            SetMood("normal")
            PositionMascot()
        End If
    End Sub

    ''' <summary>切换表情。</summary>
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
        UpdateTargets()
    End Sub

    Private Sub UpdateTargets()
        Dim MascotX As Double = Canvas.GetLeft(ImgMascot)
        Dim MascotY As Double = Canvas.GetTop(ImgMascot)
        If Double.IsNaN(MascotX) Then MascotX = 0
        If Double.IsNaN(MascotY) Then MascotY = 0
        Dim InputWidth As Double = If(PanInput.ActualWidth > 0, PanInput.ActualWidth, 380)
        Dim InputHeight As Double = If(PanInput.ActualHeight > 0, PanInput.ActualHeight, 44)
        If MascotX + 140 + 10 + InputWidth <= ActualWidth - 8 Then
            TargetX = MascotX + 140 + 10
        Else
            TargetX = Math.Max(8, MascotX - InputWidth - 10)
        End If
        TargetX = Math.Max(8, Math.Min(ActualWidth - InputWidth - 8, TargetX))
        TargetY = Math.Max(8, Math.Min(ActualHeight - InputHeight - 8, MascotY + 140 - InputHeight))
    End Sub

    Private Sub FollowTimer_Tick(sender As Object, e As EventArgs) Handles FollowTimer.Tick
        If PanInput.Visibility <> Visibility.Visible Then Return
        If Not HasPosition Then
            CurrentX = TargetX
            CurrentY = TargetY
            AnchorX = TargetX
            AnchorY = TargetY
            HasPosition = True
        Else
            CurrentX += (TargetX - CurrentX) * 0.22
            CurrentY += (TargetY - CurrentY) * 0.22
        End If
        Canvas.SetLeft(PanInput, CurrentX)
        Canvas.SetTop(PanInput, CurrentY)
        Dim OffsetX As Double = CurrentX - AnchorX
        Dim OffsetY As Double = CurrentY - AnchorY
        While LagX.Count < Messages.Count
            LagX.Add(0)
            LagY.Add(0)
        End While
        For Index As Integer = 0 To Messages.Count - 1
            Dim Speed As Double = Math.Max(0.35, 0.9 - Index * 0.06)
            LagX(Index) += (OffsetX - LagX(Index)) * Speed
            LagY(Index) += (OffsetY - LagY(Index)) * Speed
            Dim Transform As TranslateTransform = TryCast(Messages(Index).RenderTransform, TranslateTransform)
            If Transform IsNot Nothing Then
                Transform.X = OffsetX - LagX(Index)
                Transform.Y = OffsetY - LagY(Index)
            End If
        Next
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
        UpdateTargets()
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
        If PanInput.Visibility = Visibility.Visible Then
            HideBubble()
            Return
        End If
        HasPosition = False
        PanInput.Visibility = Visibility.Visible
        PanMessageLayer.Visibility = Visibility.Visible
        UpdateTargets()
        If Messages.Count = 0 Then
            RenderHistory()
            If Messages.Count = 0 Then AddBubble("你好，我是小瑟。可以帮你安装版本、排查问题，也可以直接聊天。", False)
        End If
        LayoutMessages()
        TxtInput.Focus()
    End Sub

    Private Sub HideBubble()
        PanInput.Visibility = Visibility.Collapsed
        PanMessageLayer.Visibility = Visibility.Collapsed
    End Sub

    Private Function AddBubble(Text As String, IsUser As Boolean) As TextBlock
        Dim Content As New TextBlock With {.Text = Text, .TextWrapping = TextWrapping.Wrap, .FontSize = 13, .LineHeight = 20}
        Dim Box As New Border With {
            .Child = Content,
            .CornerRadius = New CornerRadius(8),
            .Padding = New Thickness(10, 7, 10, 7),
            .MaxWidth = 320,
            .Background = New MyColor(If(IsUser, "#E8EAFF", "#FFFFFF")),
            .BorderBrush = New MyColor(If(IsUser, "#D6DAFF", "#D8DCE8")),
            .BorderThickness = New Thickness(1)
        }
        AddHandler Box.PreviewMouseWheel, AddressOf OnWheel
        Messages.Add(Box)
        PanMessageLayer.Children.Add(Box)
        AddSway(Box, 3.0 + (Messages.Count Mod 3) * 0.6, 2.6 + (Messages.Count Mod 4) * 0.4)
        HiddenNewest = Math.Min(HiddenNewest, Math.Max(0, Messages.Count - 1))
        LayoutMessages()
        Return Content
    End Function

    Private Sub LayoutMessages()
        Dim BaseX As Double = CurrentX
        Dim Bottom As Double = CurrentY - 8
        For Index As Integer = Messages.Count - 1 To 0 Step -1
            Dim Box As Border = Messages(Index)
            If Messages.Count - 1 - Index < HiddenNewest Then
                Box.Visibility = Visibility.Collapsed
                Continue For
            End If
            Box.Visibility = Visibility.Visible
            Box.Measure(New Size(330, Double.PositiveInfinity))
            Dim Height As Double = Math.Max(20, Box.DesiredSize.Height)
            Bottom -= Height
            Canvas.SetLeft(Box, BaseX)
            Canvas.SetTop(Box, Bottom)
            Bottom -= 6
        Next
        Dim Top As Double = Math.Max(0, Bottom)
        PanMessageLayer.Width = 340
        PanMessageLayer.Height = Math.Max(0, CurrentY - 8 - Top)
        Canvas.SetLeft(PanMessageLayer, BaseX)
        Canvas.SetTop(PanMessageLayer, Top)
    End Sub

    Private Sub OnWheel(sender As Object, e As MouseWheelEventArgs)
        If PanInput.Visibility <> Visibility.Visible Then Return
        If Messages.Count <= 1 Then Return
        If e.Delta > 0 Then
            HiddenNewest = Math.Min(Messages.Count - 1, HiddenNewest + 1)
        Else
            HiddenNewest = Math.Max(0, HiddenNewest - 1)
        End If
        LayoutMessages()
        e.Handled = True
    End Sub

    Private Sub ClearMessages()
        PanMessageLayer.Children.Clear()
        Messages.Clear()
        LagX.Clear()
        LagY.Clear()
        HiddenNewest = 0
        ModAgent.Instance.Clear()
        AddBubble("对话已清空。", False)
    End Sub

    Private Sub RenderHistory()
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

    '发送

    Private Sub SendMessage()
        Dim Text As String = TxtInput.Text.Trim()
        If Text.Length = 0 Then Return
        If ModAgent.Instance.IsWaiting Then Return
        TxtInput.Text = ""
        AddBubble(Text, True)
        Dim Reply As TextBlock = AddBubble("", False)
        HiddenNewest = 0
        LayoutMessages()
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
                RunInUi(Sub()
                           Reply.Text &= Full
                           LayoutMessages()
                       End Sub)
            End Sub,
            Sub(ToolName)
                RunInUi(Sub()
                           Reply.Text = ""
                           LayoutMessages()
                       End Sub)
            End Sub,
            Sub()
                RunInUi(Sub()
                           SetMood("happy")
                           LayoutMessages()
                       End Sub)
            End Sub,
            Sub(Message)
                RunInUi(Sub()
                           Reply.Text = "出错了：" & Message
                           SetMood("angry")
                           LayoutMessages()
                       End Sub)
            End Sub)
    End Sub

    Private Sub TxtInput_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter AndAlso Keyboard.Modifiers <> ModifierKeys.Shift Then
            e.Handled = True
            SendMessage()
        End If
    End Sub

    Private Sub AddSway(Target As FrameworkElement, Amplitude As Double, Seconds As Double)
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

End Class
