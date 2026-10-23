Imports System.Windows.Media.Animation
Imports System.Windows.Media.Imaging

''' <summary>AI 助手「小瑟」：可拖拽人偶 + 独立浮动消息气泡。</summary>
Public Class AgentMascot

    Private IsDragging As Boolean = False
    Private DragOffset As Point
    Private DragStart As Point

    Private ReadOnly Messages As New List(Of Border)
    Private ReadOnly MessageIsUser As New List(Of Boolean)
    Private ReadOnly MascotScale As New ScaleTransform(1, 1)
    Private ReadOnly WheelCatcher As New Border()
    Private HiddenNewest As Integer = 0

    Private AnchorX As Double = 0
    Private AnchorY As Double = 0
    Private CurrentX As Double = 0
    Private CurrentY As Double = 0
    Private TargetX As Double = 0
    Private TargetY As Double = 0
    Private HasPosition As Boolean = False
    Private RenderingHooked As Boolean = False
    Private LastFrame As TimeSpan = TimeSpan.Zero

    Private Structure OffsetSample
        Public Stamp As TimeSpan
        Public Offset As Point
    End Structure
    Private ReadOnly Samples As New List(Of OffsetSample)

    Public Sub New()
        InitializeComponent()
        AddHandler BtnAgentClose.Click, Sub() HideBubble()
        AddHandler BtnAgentClear.Click, Sub() ClearMessages()
        AddHandler BtnSend.Click, Sub() SendMessage()
        AddHandler ImgMascot.MouseLeftButtonDown, AddressOf Mascot_MouseDown
        AddHandler ImgMascot.MouseMove, AddressOf Mascot_MouseMove
        AddHandler ImgMascot.MouseLeftButtonUp, AddressOf Mascot_MouseUp
        AddHandler Me.PreviewMouseWheel, AddressOf OnWheel
        AddHandler SizeChanged, Sub()
                                    PositionMascot()
                                    PanMessageLayer.Width = Math.Max(0, ActualWidth)
                                    PanMessageLayer.Height = Math.Max(0, ActualHeight)
                                End Sub
        AddHandler Me.Loaded, Sub() RefreshState()
        ModAgent.Instance.ConfirmHandler = AddressOf ConfirmTool
        AddSway(PanInput, 3.0, 3.0)
        Dim MascotGroup As New TransformGroup()
        MascotGroup.Children.Add(TransMascot)
        MascotGroup.Children.Add(MascotScale)
        ImgMascot.RenderTransform = MascotGroup
        WheelCatcher.Background = Brushes.Transparent
        AddHandler WheelCatcher.PreviewMouseWheel, AddressOf OnWheel
        Panel.SetZIndex(WheelCatcher, -1)
        PanMessageLayer.Children.Add(WheelCatcher)
    End Sub

    Public Sub RefreshState()
        Visibility = If(Settings.Get(Of Boolean)("AgentEnabled"), Visibility.Visible, Visibility.Collapsed)
        If Visibility = Visibility.Visible Then
            SetMood("normal")
            PositionMascot()
        End If
    End Sub

    Public Sub SetMood(Mood As String)
        Try
            ImgMascot.Source = New BitmapImage(New Uri("pack://application:,,,/PCLC;component/Images/Celia/celia_" & Mood & ".png"))
        Catch
        End Try
    End Sub

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

    Private Sub StartFollow()
        If RenderingHooked Then Return
        AddHandler CompositionTarget.Rendering, AddressOf OnRendering
        RenderingHooked = True
        LastFrame = TimeSpan.Zero
    End Sub

    Private Sub StopFollow()
        If Not RenderingHooked Then Return
        RemoveHandler CompositionTarget.Rendering, AddressOf OnRendering
        RenderingHooked = False
    End Sub

    Private Sub OnRendering(sender As Object, e As EventArgs)
        If PanInput.Visibility <> Visibility.Visible Then Return
        Dim Args = TryCast(e, RenderingEventArgs)
        Dim Now As TimeSpan = If(Args Is Nothing, TimeSpan.FromMilliseconds(TimeSpan.FromTicks(DateTime.Now.Ticks).TotalMilliseconds), Args.RenderingTime)
        Dim Delta As Double = 1 / 60.0
        If LastFrame <> TimeSpan.Zero Then Delta = Math.Max(0.001, Math.Min(0.1, (Now - LastFrame).TotalSeconds))
        LastFrame = Now

        If Not HasPosition Then
            CurrentX = TargetX
            CurrentY = TargetY
            AnchorX = TargetX
            AnchorY = TargetY
            HasPosition = True
            Samples.Clear()
        Else
            Dim Factor As Double = 1 - Math.Exp(-Delta / 0.09)
            CurrentX += (TargetX - CurrentX) * Factor
            CurrentY += (TargetY - CurrentY) * Factor
        End If
        Canvas.SetLeft(PanInput, CurrentX)
        Canvas.SetTop(PanInput, CurrentY)

        Dim Offset As New Point(CurrentX - AnchorX, CurrentY - AnchorY)
        Samples.Add(New OffsetSample With {.Stamp = Now, .Offset = Offset})
        While Samples.Count > 0 AndAlso (Now - Samples(0).Stamp).TotalSeconds > 2
            Samples.RemoveAt(0)
        End While

        For Index As Integer = 0 To Messages.Count - 1
            Dim Age As Integer = Messages.Count - 1 - Index
            Dim Delay As Double = 0.05 + Math.Min(Age, 8) * 0.045
            Dim WantTime As TimeSpan = Now - TimeSpan.FromSeconds(Delay)
            Dim Sample As Point = Offset
            For i As Integer = Samples.Count - 1 To 0 Step -1
                If Samples(i).Stamp <= WantTime Then
                    Sample = Samples(i).Offset
                    Exit For
                End If
            Next
            Dim Group As TransformGroup = TryCast(Messages(Index).RenderTransform, TransformGroup)
            If Group IsNot Nothing AndAlso Group.Children.Count > 0 Then
                Dim Follow As TranslateTransform = TryCast(Group.Children(0), TranslateTransform)
                If Follow IsNot Nothing Then
                    Follow.X = Sample.X
                    Follow.Y = Sample.Y
                End If
            End If
        Next
    End Sub

    Private Sub SquashMascot(Scale As Double)
        Dim Anim As New DoubleAnimation(Scale, New Duration(TimeSpan.FromMilliseconds(110))) With {
            .EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseInOut}}
        MascotScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim)
    End Sub

    Private Sub FadeIn(Target As UIElement, Milliseconds As Integer, Delay As Integer)
        If Target Is Nothing Then Return
        Target.BeginAnimation(UIElement.OpacityProperty, Nothing)
        Target.Opacity = 0
        Dim Anim As New DoubleAnimation(1, New Duration(TimeSpan.FromMilliseconds(Milliseconds))) With {
            .BeginTime = TimeSpan.FromMilliseconds(Delay),
            .EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseInOut}}
        Target.BeginAnimation(UIElement.OpacityProperty, Anim)
    End Sub

    Private Sub FadeOut(Target As UIElement, Milliseconds As Integer, OnDone As Action)
        If Target Is Nothing Then Return
        Dim Anim As New DoubleAnimation(0, New Duration(TimeSpan.FromMilliseconds(Milliseconds))) With {
            .EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseInOut}}
        If OnDone IsNot Nothing Then AddHandler Anim.Completed, Sub(s, e) OnDone()
        Target.BeginAnimation(UIElement.OpacityProperty, Anim)
    End Sub
    Private Sub Mascot_MouseDown(sender As Object, e As MouseButtonEventArgs)
        DragStart = e.GetPosition(Me)
        DragOffset = New Point(Canvas.GetLeft(ImgMascot), Canvas.GetTop(ImgMascot))
        IsDragging = False
        SquashMascot(0.9)
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
        SquashMascot(1)
        If IsDragging Then
            Settings.Set("AgentPosX", CInt(Canvas.GetLeft(ImgMascot)))
            Settings.Set("AgentPosY", CInt(Canvas.GetTop(ImgMascot)))
        Else
            ToggleBubble()
        End If
        IsDragging = False
    End Sub

    Private Sub ToggleBubble()
        If PanInput.Visibility = Visibility.Visible Then
            HideBubble()
            Return
        End If
        UpdateTargets()
        CurrentX = TargetX
        CurrentY = TargetY
        AnchorX = TargetX
        AnchorY = TargetY
        HasPosition = True
        Samples.Clear()
        PanInput.Visibility = Visibility.Visible
        PanMessageLayer.Visibility = Visibility.Visible
        Canvas.SetLeft(PanInput, CurrentX)
        Canvas.SetTop(PanInput, CurrentY)
        PanMessageLayer.Width = Math.Max(0, ActualWidth)
        PanMessageLayer.Height = Math.Max(0, ActualHeight)
        If Messages.Count = 0 Then
            RenderHistory()
            If Messages.Count = 0 Then AddBubble("你好，我是小瑟。可以帮你安装版本、排查问题，也可以直接聊天。", False)
        End If
        LayoutMessages()
        StartFollow()
        FadeIn(PanInput, 150, 0)
        For Index As Integer = 0 To Messages.Count - 1
            FadeIn(Messages(Index), 160, Math.Min(Index, 6) * 25)
        Next
        TxtInput.Focus()
    End Sub

    Private Sub HideBubble()
        StopFollow()
        For Index As Integer = 0 To Messages.Count - 1
            FadeOut(Messages(Index), 100, Nothing)
        Next
        FadeOut(PanInput, 100, Sub()
                                  PanInput.Visibility = Visibility.Collapsed
                                  PanMessageLayer.Visibility = Visibility.Collapsed
                              End Sub)
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
        MessageIsUser.Add(IsUser)
        PanMessageLayer.Children.Add(Box)
        AddSway(Box, 3.0 + (Messages.Count Mod 3) * 0.6, 2.6 + (Messages.Count Mod 4) * 0.4)
        HiddenNewest = Math.Min(HiddenNewest, Math.Max(0, Messages.Count - 1))
        LayoutMessages()
        Return Content
    End Function

    Private Sub LayoutMessages(Optional Animate As Boolean = False)
        Dim BaseX As Double = AnchorX
        Dim Bottom As Double = AnchorY - 8
        For Index As Integer = Messages.Count - 1 To 0 Step -1
            Dim Box As Border = Messages(Index)
            If Messages.Count - 1 - Index < HiddenNewest Then
                Box.Visibility = Visibility.Collapsed
                Continue For
            End If
            Box.Visibility = Visibility.Visible
            Box.Measure(New Size(330, Double.PositiveInfinity))
            Dim Height As Double = Math.Max(20, Box.DesiredSize.Height)
            Dim Width As Double = Math.Min(320, Math.Max(60, Box.DesiredSize.Width))
            Bottom -= Height
            Dim IsUser As Boolean = If(Index < MessageIsUser.Count, MessageIsUser(Index), False)
            Canvas.SetLeft(Box, If(IsUser, BaseX + (320 - Width), BaseX))
            If Animate Then
                Box.BeginAnimation(Canvas.TopProperty, New DoubleAnimation(Bottom, New Duration(TimeSpan.FromMilliseconds(150))) With {
                    .EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseInOut}})
            Else
                Box.BeginAnimation(Canvas.TopProperty, Nothing)
                Canvas.SetTop(Box, Bottom)
            End If
            Bottom -= 6
        Next
        PanMessageLayer.Width = Math.Max(0, ActualWidth)
        PanMessageLayer.Height = Math.Max(0, ActualHeight)
        Canvas.SetLeft(PanMessageLayer, 0)
        Canvas.SetTop(PanMessageLayer, 0)
        '滚轮捕获层覆盖整块消息区域
        Dim AreaTop As Double = Math.Min(Bottom, AnchorY - 8)
        Dim AreaHeight As Double = Math.Max(0, (AnchorY - 8) - AreaTop)
        WheelCatcher.Width = 320
        WheelCatcher.Height = AreaHeight
        Canvas.SetLeft(WheelCatcher, BaseX)
        Canvas.SetTop(WheelCatcher, AreaTop)
    End Sub

    Private Sub OnWheel(sender As Object, e As MouseWheelEventArgs)
        If PanInput.Visibility <> Visibility.Visible Then Return
        If Messages.Count <= 1 Then Return
        If e.Delta > 0 Then
            HiddenNewest = Math.Min(Messages.Count - 1, HiddenNewest + 1)
        Else
            HiddenNewest = Math.Max(0, HiddenNewest - 1)
        End If
        LayoutMessages(True)
        e.Handled = True
    End Sub

    Private Sub ClearMessages()
        Dim Pending As Integer = Messages.Count
        If Pending = 0 Then
            FinishClear()
            Return
        End If
        For Each Box In Messages
            FadeOut(Box, 120, Sub()
                                      Pending -= 1
                                      If Pending <= 0 Then FinishClear()
                                  End Sub)
        Next
    End Sub

    Private Sub FinishClear()
        For Each Box In Messages
            PanMessageLayer.Children.Remove(Box)
        Next
        Messages.Clear()
        MessageIsUser.Clear()
        Samples.Clear()
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
            Dim Follow As New TranslateTransform()
            Dim SwayTransform As New TranslateTransform()
            Dim Group As New TransformGroup()
            Group.Children.Add(Follow)
            Group.Children.Add(SwayTransform)
            Target.RenderTransform = Group
            Dim Sway As New DoubleAnimation(Amplitude, -Amplitude, New Duration(TimeSpan.FromSeconds(Seconds))) With {
                .AutoReverse = True,
                .RepeatBehavior = RepeatBehavior.Forever,
                .EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseInOut},
                .BeginTime = TimeSpan.FromMilliseconds(New Random().Next(0, 1500))}
            SwayTransform.BeginAnimation(TranslateTransform.XProperty, Sway)
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
