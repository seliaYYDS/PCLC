Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Windows.Documents
Imports System.Windows.Media.Animation
Imports System.Windows.Media.Imaging

''' <summary>AI 助手「小瑟」：可拖拽人偶 + 独立浮动消息气泡（支持 Markdown）。</summary>
Public Class AgentMascot

    Friend Class BubbleEntry
        Public Box As Border
        Public Host As StackPanel
        Public Raw As New StringBuilder()
        Public IsUser As Boolean
        Public Notes As New List(Of String)
        Public Shift As New TranslateTransform()
        Public ScrollOffset As Double = 0
        Public NeedScroll As Boolean = False
        Public RenderedBlocks As Integer = 0
    End Class

    Private IsDragging As Boolean = False
    Private DragOffset As Point
    Private DragStart As Point

    Private ReadOnly Entries As New List(Of BubbleEntry)
    Private HiddenNewest As Integer = 0
    Private ReadOnly MessageShown As New List(Of Boolean)
    Private ReadOnly MascotScale As New ScaleTransform(1, 1)
    Private ReadOnly WheelCatcher As New Border()

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
        Dim MascotGroup As New TransformGroup()
        MascotGroup.Children.Add(TransMascot)
        MascotGroup.Children.Add(MascotScale)
        ImgMascot.RenderTransform = MascotGroup
        WheelCatcher.Background = Brushes.Transparent
        AddHandler WheelCatcher.PreviewMouseWheel, AddressOf OnWheel
        Panel.SetZIndex(WheelCatcher, -1)
        PanMessageLayer.Children.Add(WheelCatcher)
        AddSway(PanInput, 3.0, 3.0)
    End Sub

    Private ReadOnly Property MascotWidth As Double
        Get
            If ImgMascot IsNot Nothing AndAlso ImgMascot.ActualWidth > 0 Then Return ImgMascot.ActualWidth
            Return 92
        End Get
    End Property
    Private ReadOnly Property MascotHeight As Double
        Get
            If ImgMascot IsNot Nothing AndAlso ImgMascot.ActualHeight > 0 Then Return ImgMascot.ActualHeight
            Return 140
        End Get
    End Property

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
        If X < 0 Then X = ActualWidth - MascotWidth - 20
        If Y < 0 Then Y = ActualHeight - MascotHeight - 24
        X = Math.Max(0, Math.Min(ActualWidth - MascotWidth, X))
        Y = Math.Max(0, Math.Min(ActualHeight - MascotHeight, Y))
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
        If MascotX + MascotWidth + 10 + InputWidth <= ActualWidth - 8 Then
            TargetX = MascotX + MascotWidth + 10
        Else
            TargetX = Math.Max(8, MascotX - InputWidth - 10)
        End If
        TargetX = Math.Max(8, Math.Min(ActualWidth - InputWidth - 8, TargetX))
        TargetY = Math.Max(8, Math.Min(ActualHeight - InputHeight - 8, MascotY + MascotHeight - InputHeight))
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
        Dim Now As TimeSpan = TimeSpan.FromSeconds(Environment.TickCount / 1000.0)
        Dim Args = TryCast(e, RenderingEventArgs)
        If Args IsNot Nothing Then Now = Args.RenderingTime
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

        For Index As Integer = 0 To Entries.Count - 1
            Dim Age As Integer = Entries.Count - 1 - Index
            Dim Delay As Double = 0.02 + Math.Min(Age, 6) * 0.02
            Dim WantTime As TimeSpan = Now - TimeSpan.FromSeconds(Delay)
            Dim Sample As Point = Offset
            For i As Integer = Samples.Count - 1 To 0 Step -1
                If Samples(i).Stamp <= WantTime Then
                    Sample = Samples(i).Offset
                    Exit For
                End If
            Next
            Dim Group As TransformGroup = TryCast(Entries(Index).Box.RenderTransform, TransformGroup)
            If Group IsNot Nothing AndAlso Group.Children.Count > 0 Then
                Dim Follow As TranslateTransform = TryCast(Group.Children(0), TranslateTransform)
                If Follow IsNot Nothing Then
                    Follow.X = Sample.X
                    Follow.Y = Sample.Y
                End If
            End If
        Next
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
        X = Math.Max(0, Math.Min(ActualWidth - MascotWidth, X))
        Y = Math.Max(0, Math.Min(ActualHeight - MascotHeight, Y))
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

    Private Sub SquashMascot(Scale As Double)
        Dim Anim As New DoubleAnimation(Scale, New Duration(TimeSpan.FromMilliseconds(110))) With {
            .EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseInOut}}
        MascotScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim)
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
        If Entries.Count = 0 Then
            RenderHistory()
        End If
        LayoutMessages()
        StartFollow()
        FadeIn(PanInput, 150, 0)
        For Index As Integer = 0 To Entries.Count - 1
            FadeIn(Entries(Index).Box, 160, Math.Min(Index, 6) * 25)
        Next
        TxtInput.Focus()
    End Sub

    Private Sub HideBubble()
        StopFollow()
        For Index As Integer = 0 To Entries.Count - 1
            FadeOut(Entries(Index).Box, 100, Nothing)
        Next
        FadeOut(PanInput, 100, Sub()
                                  PanInput.Visibility = Visibility.Collapsed
                                  PanMessageLayer.Visibility = Visibility.Collapsed
                              End Sub)
    End Sub

    Private Function AddBubble(Text As String, IsUser As Boolean) As BubbleEntry
        Dim Host As New StackPanel()
        Host.RenderTransform = New TranslateTransform()

        Dim Box As New Border With {
            .Child = Host,
            .CornerRadius = New CornerRadius(8),
            .Padding = New Thickness(10, 7, 10, 7),
            .MaxWidth = 320,
            .Background = New MyColor(If(IsUser, "#E8EAFF", "#FFFFFF")),
            .BorderBrush = New MyColor(If(IsUser, "#D6DAFF", "#D8DCE8")),
            .BorderThickness = New Thickness(1)
        }
        Dim Entry As New BubbleEntry With {.Box = Box, .Host = Host, .IsUser = IsUser}
        Entry.Shift = CType(Host.RenderTransform, TranslateTransform)
        Entry.Raw.Append(Text)
        AddHandler Box.PreviewMouseWheel, AddressOf OnWheel
        Entries.Add(Entry)
        MessageShown.Add(True)
        PanMessageLayer.Children.Add(Box)
        AddSway(Box, 3.0 + (Entries.Count Mod 3) * 0.6, 2.6 + (Entries.Count Mod 4) * 0.4)
        RenderEntry(Entry)
        HiddenNewest = Math.Min(HiddenNewest, Math.Max(0, Entries.Count - 1))
        LayoutMessages()
        Return Entry
    End Function

    Private Sub RenderEntry(Entry As BubbleEntry)
        Entry.Host.Children.Clear()
        Entry.RenderedBlocks = 0
        If Entry.IsUser Then
            Entry.Host.Children.Add(New TextBlock With {
                .Text = Entry.Raw.ToString(),
                .TextWrapping = TextWrapping.Wrap,
                .FontSize = 13,
                .LineHeight = 20,
                .Foreground = New MyColor(51, 51, 51)})
            Return
        End If
        If Entry.Raw.Length = 0 Then
            Entry.Host.Children.Add(BuildHintText("思考中…", 12))
        Else
            Dim Previous As Integer = Entry.RenderedBlocks
            Dim Index As Integer = 0
            For Each Block In ParseMarkdown(Entry.Raw.ToString())
                Entry.Host.Children.Add(Block)
                If Index >= Previous Then AnimateBlockAppear(Block)
                Index += 1
            Next
            Entry.RenderedBlocks = Index
        End If
        For Each Note In Entry.Notes
            Entry.Host.Children.Add(BuildHintText(Note, 11))
        Next
    End Sub

    Private Shared Sub AnimateBlockAppear(Block As UIElement)
        Try
            Dim Element = TryCast(Block, FrameworkElement)
            If Element Is Nothing Then Return
            Element.Opacity = 0
            Dim Shift As New TranslateTransform(0, 6)
            Element.RenderTransform = Shift
            Dim Ease As New CubicEase With {.EasingMode = EasingMode.EaseInOut}
            Element.BeginAnimation(UIElement.OpacityProperty, New DoubleAnimation(1, New Duration(TimeSpan.FromMilliseconds(180))) With {.EasingFunction = Ease})
            Shift.BeginAnimation(TranslateTransform.YProperty, New DoubleAnimation(0, New Duration(TimeSpan.FromMilliseconds(180))) With {.EasingFunction = Ease})
        Catch
        End Try
    End Sub
    Private Shared Function ParseMarkdown(Text As String) As List(Of UIElement)
        Dim Result As New List(Of UIElement)
        Dim Lines As String() = Text.Replace(vbCrLf, vbLf).Split(ChrW(10))
        Dim Code As New StringBuilder()
        Dim InCode As Boolean = False
        Dim Fence3 As String = New String(ChrW(96), 3)
        For Each Line In Lines
            If Line.TrimStart().StartsWith(Fence3) Then
                If InCode Then
                    Result.Add(BuildCodeBlock(Code.ToString()))
                    Code.Clear()
                End If
                InCode = Not InCode
                Continue For
            End If
            If InCode Then
                Code.AppendLine(Line)
                Continue For
            End If
            If String.IsNullOrWhiteSpace(Line) Then Continue For
            Dim Trimmed As String = Line.TrimStart()
            Dim Indent As Double = Line.Length - Trimmed.Length
            If Trimmed = "---" OrElse Trimmed = "***" Then
                Result.Add(New Border With {.Height = 1, .Background = New MyColor(226, 229, 238), .Margin = New Thickness(0, 6, 0, 6)})
                Continue For
            End If
            If Trimmed.StartsWith("|") Then
                Dim Stripped As String = Trimmed.Replace("|", "").Replace("-", "").Trim()
                If Stripped.Length = 0 Then Continue For
                Dim Cells = Trimmed.Trim("|"c).Split("|"c)
                Dim Row As New TextBlock With {
                    .Text = String.Join("   ", Array.ConvertAll(Cells, Function(C) C.Trim())),
                    .FontSize = 12,
                    .Foreground = New MyColor(70, 74, 90),
                    .TextWrapping = TextWrapping.Wrap,
                    .Margin = New Thickness(0, 1, 0, 1)}
                Result.Add(Row)
                Continue For
            End If
            Dim Paragraph As New TextBlock With {
                .TextWrapping = TextWrapping.Wrap,
                .FontSize = 13,
                .LineHeight = 20,
                .Foreground = New MyColor(51, 51, 51),
                .Margin = New Thickness(Math.Min(Indent * 4, 16), 1, 0, 1)}
            If Trimmed.StartsWith(">") Then
                Paragraph.Foreground = New MyColor(110, 116, 135)
                Paragraph.Padding = New Thickness(8, 0, 0, 0)
                AddInline(Paragraph, Trimmed.TrimStart(">"c).Trim())
            ElseIf Trimmed.StartsWith("#") Then
                Dim Level As Integer = 0
                While Level < Trimmed.Length AndAlso Trimmed(Level) = "#"c
                    Level += 1
                End While
                Paragraph.FontSize = If(Level <= 1, 16, 14.5)
                Paragraph.FontWeight = FontWeights.SemiBold
                Paragraph.Margin = New Thickness(0, 5, 0, 2)
                AddInline(Paragraph, Trimmed.TrimStart("#"c).Trim())
            ElseIf Trimmed.StartsWith("- ") OrElse Trimmed.StartsWith("* ") Then
                Paragraph.Margin = New Thickness(4 + Math.Min(Indent * 4, 16), 1, 0, 1)
                AddInline(Paragraph, "• " & Trimmed.Substring(2))
            ElseIf Regex.IsMatch(Trimmed, "^\d+\.\s") Then
                Paragraph.Margin = New Thickness(4 + Math.Min(Indent * 4, 16), 1, 0, 1)
                AddInline(Paragraph, Trimmed)
            Else
                AddInline(Paragraph, Trimmed)
            End If
            Result.Add(Paragraph)
        Next
        If Code.Length > 0 Then Result.Add(BuildCodeBlock(Code.ToString()))
        Return Result
    End Function

    Private Shared Function BuildCodeBlock(Code As String) As UIElement
        Return New Border With {
            .Background = New MyColor(245, 246, 250),
            .CornerRadius = New CornerRadius(4),
            .Padding = New Thickness(8, 6, 8, 6),
            .Margin = New Thickness(0, 3, 0, 3),
            .Child = New TextBlock With {
                .Text = Code.TrimEnd(),
                .FontFamily = New FontFamily("Consolas"),
                .FontSize = 12,
                .TextWrapping = TextWrapping.Wrap,
                .Foreground = New MyColor(60, 64, 80)}}
    End Function

    Private Shared Function BuildHintText(Text As String, Size As Double) As TextBlock
        Dim Block As New TextBlock With {
            .Text = Text,
            .FontSize = Size,
            .Margin = New Thickness(0, 2, 0, 0),
            .TextWrapping = TextWrapping.Wrap}
        Try
            Dim BaseColor As Color = CType(New MyColor(160, 165, 180), Color)
            Dim HighColor As Color = CType(New MyColor(85, 95, 135), Color)
            Dim Brush As New LinearGradientBrush With {.StartPoint = New Point(0, 0), .EndPoint = New Point(1, 0)}
            Brush.GradientStops.Add(New GradientStop(HighColor, 0))
            Brush.GradientStops.Add(New GradientStop(BaseColor, 0.35))
            Brush.GradientStops.Add(New GradientStop(BaseColor, 0.65))
            Brush.GradientStops.Add(New GradientStop(HighColor, 1))
            Dim Shift As New TranslateTransform(-1, 0)
            Brush.RelativeTransform = Shift
            Block.Foreground = Brush
            Dim Sweep As New DoubleAnimation(-1, 1, New Duration(TimeSpan.FromSeconds(2.4))) With {
                .RepeatBehavior = RepeatBehavior.Forever}
            Shift.BeginAnimation(TranslateTransform.XProperty, Sweep)
        Catch
        End Try
        Return Block
    End Function

    Private Shared Sub AddInline(Target As TextBlock, Text As String)
        Dim Fence As Char = ChrW(96)
        Dim Pattern As New Regex("(\*\*.+?\*\*|__.+?__|~~.+?~~|\*[^*" & vbLf & "]+?\*|" & Fence & "[^" & Fence & "]+?" & Fence & "|\[[^\]]+\]\([^)]+\))")
        Dim Last As Integer = 0
        For Each M As Match In Pattern.Matches(Text)
            If M.Index > Last Then Target.Inlines.Add(New Run(Text.Substring(Last, M.Index - Last)))
            Dim Piece As String = M.Value
            If Piece.StartsWith("**") Then
                Target.Inlines.Add(New Bold(New Run(Piece.Substring(2, Piece.Length - 4))))
            ElseIf Piece.StartsWith("__") Then
                Target.Inlines.Add(New Bold(New Run(Piece.Substring(2, Piece.Length - 4))))
            ElseIf Piece.StartsWith("~~") Then
                Target.Inlines.Add(New Run(Piece.Substring(2, Piece.Length - 4)) With {.TextDecorations = TextDecorations.Strikethrough})
            ElseIf Piece.StartsWith("[") Then
                Dim Close As Integer = Piece.IndexOf("](")
                Dim Label As String = Piece.Substring(1, Close - 1)
                Target.Inlines.Add(New Run(Label) With {.Foreground = New MyColor(70, 100, 200)})
            ElseIf Piece.StartsWith(Fence) Then
                Target.Inlines.Add(New Run(Piece.Trim(Fence)) With {
                    .FontFamily = New FontFamily("Consolas"),
                    .Foreground = New MyColor(90, 96, 120)})
            Else
                Dim Inner As String = Piece.Trim("*"c)
                Target.Inlines.Add(New Italic(New Run(Inner)))
            End If
            Last = M.Index + M.Length
        Next
        If Last < Text.Length Then Target.Inlines.Add(New Run(Text.Substring(Last)))
    End Sub

    Private Sub LayoutMessages(Optional Animate As Boolean = False)
        Dim BaseX As Double = AnchorX
        Dim Bottom As Double = AnchorY - 8
        Dim Available As Double = Math.Max(80, AnchorY - 16)
        For Index As Integer = Entries.Count - 1 To 0 Step -1
            Dim Entry As BubbleEntry = Entries(Index)
            Dim Box As Border = Entry.Box
            Dim WantVisible As Boolean = (Entries.Count - 1 - Index) >= HiddenNewest
            Dim WasVisible As Boolean = If(Index < MessageShown.Count, MessageShown(Index), True)
            If WantVisible <> WasVisible Then
                While MessageShown.Count <= Index
                    MessageShown.Add(True)
                End While
                MessageShown(Index) = WantVisible
                If WantVisible Then
                    Box.Visibility = Visibility.Visible
                    FadeIn(Box, 140, 0)
                Else
                    FadeOut(Box, 120, Sub() Box.Visibility = Visibility.Collapsed)
                End If
            End If
            If Not WantVisible Then Continue For
            Box.Visibility = Visibility.Visible
            '内容高度与视口
            Dim ContentHeight As Double = MeasureContent(Entry)
            Entry.NeedScroll = ContentHeight > Available
            If Entry.NeedScroll Then
                Box.Height = Available
                Box.ClipToBounds = True
                Entry.ScrollOffset = Math.Max(0, Math.Min(Entry.ScrollOffset, ContentHeight - Available))
                Entry.Shift.Y = -Entry.ScrollOffset
            Else
                Box.Height = Double.NaN
                Box.ClipToBounds = False
                Entry.ScrollOffset = 0
                Entry.Shift.Y = 0
            End If
            Box.Measure(New Size(330, If(Entry.NeedScroll, Available, Double.PositiveInfinity)))
            Dim Height As Double = If(Entry.NeedScroll, Available, Math.Max(20, Box.DesiredSize.Height))
            Dim Width As Double = Math.Min(320, Math.Max(60, Box.DesiredSize.Width))
            Bottom -= Height
            Dim X As Double = If(Entry.IsUser, BaseX + (320 - Width), BaseX)
            Canvas.SetLeft(Box, X)
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
        Dim AreaTop As Double = Math.Min(Bottom, AnchorY - 8)
        WheelCatcher.Width = 320
        WheelCatcher.Height = Math.Max(0, (AnchorY - 8) - AreaTop)
        Canvas.SetLeft(WheelCatcher, BaseX)
        Canvas.SetTop(WheelCatcher, AreaTop)
    End Sub

    Private Shared Function MeasureContent(Entry As BubbleEntry) As Double
        Try
            Entry.Host.Measure(New Size(300, Double.PositiveInfinity))
            Return Entry.Host.DesiredSize.Height + 14
        Catch
            Return 0
        End Try
    End Function

    Private Sub OnWheel(sender As Object, e As MouseWheelEventArgs)
        If PanInput.Visibility <> Visibility.Visible Then Return
        Dim Topmost As BubbleEntry = Nothing
        For Each Entry In Entries
            If Entry.Box.Visibility = Visibility.Visible Then
                Topmost = Entry
                Exit For
            End If
        Next
        If Topmost IsNot Nothing AndAlso Topmost.NeedScroll Then
            Dim MaxOffset As Double = Math.Max(0, MeasureContent(Topmost) - Topmost.Box.Height)
            If e.Delta > 0 AndAlso Topmost.ScrollOffset <= 0.5 Then
                '已到顶部，继续上滚则交给隐藏逻辑
            Else
                Topmost.ScrollOffset = Math.Max(0, Math.Min(MaxOffset, Topmost.ScrollOffset - e.Delta / 3.0))
                Topmost.Shift.Y = -Topmost.ScrollOffset
                e.Handled = True
                Return
            End If
        End If
        If Entries.Count <= 1 Then Return
        If e.Delta > 0 Then
            HiddenNewest = Math.Min(Entries.Count - 1, HiddenNewest + 1)
        Else
            HiddenNewest = Math.Max(0, HiddenNewest - 1)
        End If
        LayoutMessages(True)
        e.Handled = True
    End Sub

    Private Sub ClearMessages()
        SetMood("normal")
        Dim Pending As Integer = Entries.Count
        If Pending = 0 Then
            FinishClear()
            Return
        End If
        Dim Snapshot As New List(Of BubbleEntry)(Entries)
        For Each Entry In Snapshot
            FadeOut(Entry.Box, 120, Sub()
                                       Pending -= 1
                                       If Pending <= 0 Then FinishClear()
                                   End Sub)
        Next
    End Sub

    Private Sub FinishClear()
        For Each Entry In Entries
            PanMessageLayer.Children.Remove(Entry.Box)
        Next
        Entries.Clear()
        MessageShown.Clear()
        Samples.Clear()
        HiddenNewest = 0
        ModAgent.Instance.Clear()
        ShowTemporaryMessage()
    End Sub

    Private Sub ShowTemporaryMessage()
        Dim Entry = AddBubble("对话已清空", False)
        FadeIn(Entry.Box, 140, 0)
        Dim Timer As New Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(1400)}
        AddHandler Timer.Tick,
            Sub(sender, args)
                Timer.Stop()
                FadeOut(Entry.Box, 220, Sub()
                                             PanMessageLayer.Children.Remove(Entry.Box)
                                             Entries.Remove(Entry)
                                             LayoutMessages(True)
                                         End Sub)
            End Sub
        Timer.Start()
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
        Dim Reply As BubbleEntry = AddBubble("", False)
        HiddenNewest = 0
        LayoutMessages()
        Dim Worker As New Threading.Thread(AddressOf SendWorker)
        Worker.IsBackground = True
        Worker.Start(New Object() {Text, Reply})
    End Sub

    Private Sub SendWorker(State As Object)
        Dim Args As Object() = CType(State, Object())
        Dim Text As String = CStr(Args(0))
        Dim Reply As BubbleEntry = CType(Args(1), BubbleEntry)
        ModAgent.Instance.Send(Text,
            Sub(Full)
                RunInUi(Sub()
                           Reply.Raw.Append(Full)
                           If Reply.Notes.Count > 0 Then
                               Reply.Notes.Clear()
                               RenderEntry(Reply)
                           End If
                           LayoutMessages()
                       End Sub)
            End Sub,
            Sub(ToolName)
                RunInUi(Sub()
                           Reply.Notes.Add("·  正在调用 " & ToolName)
                           RenderEntry(Reply)
                           LayoutMessages()
                       End Sub)
            End Sub,
            Sub()
                RunInUi(Sub()
                           If Reply.Notes.Count > 0 Then
                               Reply.Notes.Clear()
                               RenderEntry(Reply)
                           End If
                           LayoutMessages()
                       End Sub)
            End Sub,
            Sub(Message)
                RunInUi(Sub()
                           Reply.Raw.Append("出错了：" & Message)
                           RenderEntry(Reply)
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
