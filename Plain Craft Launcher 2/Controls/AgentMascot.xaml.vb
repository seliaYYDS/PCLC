Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Windows.Documents
Imports System.Windows.Media.Animation
Imports System.Windows.Media.Imaging

''' <summary>AI 助手「小瑟」：可拖拽人偶 + 独立浮动消息气泡（支持 Markdown）。</summary>
Public Class AgentMascot

    Friend Class AgentBlock
        Public Kind As Integer
        Public Text As String
        Public Level As Integer
    End Class

    Friend Class BubbleEntry
        Public Box As Border
        Public Host As StackPanel
        Public Raw As New StringBuilder()
        Public IsUser As Boolean
        Public Notes As New List(Of String)
        Public Viewer As ScrollViewer
        Public Thumb As Border
        Public Track As FrameworkElement
        Public DragStartY As Double = 0
        Public DragStartOffset As Double = 0
        Public Shift As New TranslateTransform()
        Public ScrollOffset As Double = 0
        Public TargetOffset As Double = 0
        Public LastAppliedOffset As Double = 0
        Public ContentCache As Double = 0
        Public DirtyContent As Boolean = True
        Public StickBottom As Boolean = True
        Public NeedScroll As Boolean = False
        Public RenderedBlocks As Integer = 0
        Public ShowingThinking As Boolean = False
    End Class

    Private IsDragging As Boolean = False
    Private DragOffset As Point
    Private DragStart As Point

    Private ReadOnly Entries As New List(Of BubbleEntry)
    Private HiddenNewest As Integer = 0
    Private ReadOnly MessageShown As New List(Of Boolean)
    Private ReadOnly MascotScale As New ScaleTransform(1, 1)
    Private ReadOnly WheelCatcher As New Border()
    Private WithEvents LayoutTimer As New Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(50)}
    Private LayoutRequested As Boolean = False

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
        AddHandler PanSendBusy.MouseEnter, Sub()
                                                If PanSendBusy.Visibility = Visibility.Visible Then
                                                    PanRing.Visibility = Visibility.Collapsed
                                                    RecStop.Visibility = Visibility.Visible
                                                End If
                                            End Sub
        AddHandler PanSendBusy.MouseLeave, Sub()
                                                PanRing.Visibility = Visibility.Visible
                                                RecStop.Visibility = Visibility.Collapsed
                                            End Sub
        AddHandler PanSendBusy.MouseLeftButtonUp, Sub()
                                                      ModAgent.Instance.Cancel()
                                                      PanRing.Visibility = Visibility.Collapsed
                                                      RecStop.Visibility = Visibility.Visible
                                                  End Sub
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
        LayoutTimer.Start()
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

        '平滑滚动长消息
        For Each Entry In Entries
            If Entry.Viewer Is Nothing Then Continue For
            '用户拖动滚动条：位置与上次程序设置的不一致，则同步目标
            If Math.Abs(Entry.Viewer.VerticalOffset - Entry.LastAppliedOffset) > 1 Then
                Entry.TargetOffset = Entry.Viewer.VerticalOffset
                Entry.LastAppliedOffset = Entry.Viewer.VerticalOffset
            End If
            If Math.Abs(Entry.TargetOffset - Entry.Viewer.VerticalOffset) < 0.5 Then Continue For
            Dim NextOffset As Double = Entry.Viewer.VerticalOffset + (Entry.TargetOffset - Entry.Viewer.VerticalOffset) * 0.25
            Entry.Viewer.ScrollToVerticalOffset(NextOffset)
            Entry.LastAppliedOffset = NextOffset
        Next
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

    Private Sub RequestLayout()
        LayoutRequested = True
    End Sub

    Private Sub LayoutTimer_Tick(sender As Object, e As EventArgs) Handles LayoutTimer.Tick
        If LayoutRequested Then
            LayoutRequested = False
            LayoutMessages()
        End If
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
        Dim Viewer As New ScrollViewer With {
            .Content = Host,
            .VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            .Background = Brushes.Transparent}
        Dim Thumb As New Border With {
            .Width = 4,
            .CornerRadius = New CornerRadius(2),
            .Background = New MyColor("#C9CDDB"),
            .VerticalAlignment = VerticalAlignment.Top,
            .HorizontalAlignment = HorizontalAlignment.Right,
            .Visibility = Visibility.Collapsed,
            .Margin = New Thickness(0, 0, 1, 0)}
        Dim Track As New Canvas With {.Width = 6, .Background = Brushes.Transparent, .ClipToBounds = True}
        Track.Children.Add(Thumb)
        Dim RootGrid As New Grid()
        RootGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        RootGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        RootGrid.Children.Add(Viewer)
        Grid.SetColumn(Track, 1)
        RootGrid.Children.Add(Track)

        Dim Box As New Border With {
            .Child = RootGrid,
            .CornerRadius = New CornerRadius(8),
            .Padding = New Thickness(10, 7, 10, 7),
            .MaxWidth = 320,
            .Background = New MyColor(If(IsUser, "#E8EAFF", "#FFFFFF")),
            .BorderBrush = New MyColor(If(IsUser, "#D6DAFF", "#D8DCE8")),
            .BorderThickness = New Thickness(1)
        }
        Dim Entry As New BubbleEntry With {.Box = Box, .Host = Host, .IsUser = IsUser}
        Entry.Viewer = Viewer
        Entry.Thumb = Thumb
        Entry.Track = Track
        AddHandler Viewer.ScrollChanged, Sub(s, args) UpdateScrollBar(Entry)
        AddHandler Thumb.MouseLeftButtonDown, Sub(s, args)
                                                   Entry.DragStartY = args.GetPosition(Track).Y
                                                   Entry.DragStartOffset = Viewer.VerticalOffset
                                                   Thumb.CaptureMouse()
                                               End Sub
        AddHandler Thumb.MouseMove, Sub(s, args)
                                        If Not Thumb.IsMouseCaptured Then Return
                                        Dim Range As Double = Math.Max(0, MeasureContent(Entry) - Viewer.ViewportHeight)
                                        Dim Space As Double = Math.Max(1, Track.ActualHeight - Thumb.ActualHeight)
                                        Dim Delta As Double = args.GetPosition(Track).Y - Entry.DragStartY
                                        Dim Target As Double = Entry.DragStartOffset + Delta * Range / Space
                                        Viewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(Range, Target)))
                                    End Sub
        AddHandler Thumb.MouseLeftButtonUp, Sub(s, args)
                                                 Thumb.ReleaseMouseCapture()
                                             End Sub
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
        If Entry.IsUser Then
            Entry.Host.Children.Clear()
            Entry.Host.Children.Add(New TextBlock With {
                .Text = Entry.Raw.ToString(),
                .TextWrapping = TextWrapping.Wrap,
                .FontSize = 13,
                .LineHeight = 20,
                .Foreground = New MyColor(51, 51, 51)})
            Return
        End If
        If Entry.Raw.Length = 0 Then
            Entry.Host.Children.Clear()
            Entry.Host.Children.Add(BuildHintText("思考中…", 12))
            For Each Note In Entry.Notes
                Entry.Host.Children.Add(BuildHintText(Note, 11))
            Next
            Entry.ShowingThinking = True
            Return
        End If
        If Entry.ShowingThinking Then
            '正文到达，移除“思考中”占位
            Entry.Host.Children.Clear()
            Entry.ShowingThinking = False
        End If
        Dim Models As List(Of AgentBlock) = ParseBlocks(Entry.Raw.ToString())
        While Entry.Host.Children.Count > Models.Count
            Entry.Host.Children.RemoveAt(Entry.Host.Children.Count - 1)
        End While
        For Index As Integer = 0 To Models.Count - 1
            If Index >= Entry.Host.Children.Count Then
                Dim Element As UIElement = CreateBlockElement(Models(Index))
                Entry.Host.Children.Add(Element)
                AnimateBlockAppear(Element)
            End If
            Dim Existing As UIElement = Entry.Host.Children(Index)
            If TypeOf Existing Is WrapPanel Then
                UpdateBlockElement(Existing, Models(Index))
            ElseIf Models(Index).Kind = 6 Then
                Dim Holder = TryCast(Existing, FrameworkElement)
                If Holder Is Nothing OrElse Not String.Equals(TryCast(Holder.Tag, String), Models(Index).Text) Then
                    Entry.Host.Children(Index) = CreateBlockElement(Models(Index))
                End If
            End If
        Next
        Entry.DirtyContent = True
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
    Private Shared Function ParseBlocks(Text As String) As List(Of AgentBlock)
        Dim Result As New List(Of AgentBlock)
        Dim Code As New StringBuilder()
        Dim InCode As Boolean = False
        Dim Fence3 As String = New String(ChrW(96), 3)
        Dim Lines As String() = Text.Replace(vbCrLf, vbLf).Split(ChrW(10))
        Dim Index As Integer = 0
        While Index < Lines.Length
            Dim Line As String = Lines(Index)
            Index += 1
            If Line.TrimStart().StartsWith(Fence3) Then
                If InCode Then
                    Result.Add(New AgentBlock With {.Kind = 4, .Text = Code.ToString().TrimEnd()})
                    Code.Clear()
                End If
                InCode = Not InCode
                Continue While
            End If
            If InCode Then
                Code.AppendLine(Line)
                Continue While
            End If
            If String.IsNullOrWhiteSpace(Line) Then Continue While
            Dim Trimmed As String = Line.TrimStart()
            Dim Indent As Integer = Line.Length - Trimmed.Length
            If Trimmed.StartsWith("|") Then
                Dim Rows As New List(Of String)
                Rows.Add(Trimmed.TrimEnd())
                While Index < Lines.Length AndAlso Lines(Index).TrimStart().StartsWith("|")
                    Rows.Add(Lines(Index).TrimStart().TrimEnd())
                    Index += 1
                End While
                Result.Add(New AgentBlock With {.Kind = 6, .Text = String.Join(vbLf, Rows)})
                Continue While
            End If
            If Trimmed = "---" OrElse Trimmed = "***" Then
                Result.Add(New AgentBlock With {.Kind = 5, .Text = ""})
                Continue While
            End If
            If Trimmed.StartsWith("#") Then
                Dim Level As Integer = 0
                While Level < Trimmed.Length AndAlso Trimmed(Level) = "#"c
                    Level += 1
                End While
                Result.Add(New AgentBlock With {.Kind = 1, .Level = Level, .Text = Trimmed.TrimStart("#"c).Trim()})
            ElseIf Trimmed.StartsWith(">") Then
                Result.Add(New AgentBlock With {.Kind = 3, .Text = Trimmed.TrimStart(">"c).Trim(), .Level = Math.Min(Indent \ 2, 6)})
            ElseIf Trimmed.StartsWith("- ") OrElse Trimmed.StartsWith("* ") Then
                Result.Add(New AgentBlock With {.Kind = 2, .Text = "• " & Trimmed.Substring(2), .Level = Math.Min(Indent \ 2, 6)})
            Else
                Result.Add(New AgentBlock With {.Kind = 0, .Text = Trimmed, .Level = Math.Min(Indent \ 2, 6)})
            End If
        End While
        If Code.Length > 0 Then Result.Add(New AgentBlock With {.Kind = 4, .Text = Code.ToString().TrimEnd()})
        Return Result
    End Function

    Private Shared Function CreateBlockElement(Model As AgentBlock) As UIElement
        If Model.Kind = 4 Then
            Return New Border With {
                .Background = New MyColor(245, 246, 250),
                .CornerRadius = New CornerRadius(4),
                .Padding = New Thickness(8, 6, 8, 6),
                .Margin = New Thickness(0, 3, 0, 3),
                .Child = New TextBlock With {
                    .Text = Model.Text,
                    .FontFamily = New FontFamily("Consolas"),
                    .FontSize = 12,
                    .TextWrapping = TextWrapping.Wrap,
                    .Foreground = New MyColor(60, 64, 80)}}
        End If
        If Model.Kind = 5 Then
            Return New Border With {.Height = 1, .Background = New MyColor(226, 229, 238), .Margin = New Thickness(0, 6, 0, 6)}
        End If
        If Model.Kind = 6 Then
            Return BuildTable(Model.Text)
        End If
        Dim Panel As New WrapPanel With {
            .Margin = New Thickness(If(Model.Kind = 2, 4 + Math.Min(Model.Level * 3, 24), Math.Min(Model.Level * 3, 24)), 1, 0, 1),
            .HorizontalAlignment = HorizontalAlignment.Left}
        Panel.Tag = 0
        Return Panel
    End Function

    Private Shared Function BuildTable(Text As String) As UIElement
        Dim Rows As New List(Of List(Of String))
        For Each Line In Text.Replace(vbCrLf, vbLf).Split(ChrW(10))
            Dim Trimmed As String = Line.Trim()
            If Trimmed.Length = 0 Then Continue For
            Dim Cells As New List(Of String)
            For Each Cell In Trimmed.Trim("|"c).Split("|"c)
                Cells.Add(Cell.Trim())
            Next
            Dim IsSeparator As Boolean = Cells.All(Function(C) C.Length > 0 AndAlso C.All(Function(Ch) Ch = "-"c OrElse Ch = ":"c))
            If IsSeparator Then Continue For
            Rows.Add(Cells)
        Next
        If Rows.Count = 0 Then Return New Border With {.Height = 1}
        Dim Columns As Integer = Rows.Max(Function(R) R.Count)
        Dim Grid As New Grid()
        For C As Integer = 0 To Columns - 1
            Grid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Next
        For R As Integer = 0 To Rows.Count - 1
            Grid.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
            For C As Integer = 0 To Columns - 1
                Dim Content As String = If(C < Rows(R).Count, Rows(R)(C), "")
                Dim Cell As New TextBlock With {
                    .Text = Content,
                    .FontSize = 12,
                    .FontWeight = If(R = 0, FontWeights.SemiBold, FontWeights.Normal),
                    .Foreground = New MyColor(60, 64, 80),
                    .Margin = New Thickness(6, 3, 6, 3),
                    .TextWrapping = TextWrapping.Wrap,
                    .MaxWidth = 200}
                Dim Holder As New Border With {
                    .Child = Cell,
                    .BorderBrush = New MyColor(228, 231, 240),
                    .BorderThickness = New Thickness(0, 0, If(C = Columns - 1, 0, 1), If(R = Rows.Count - 1, 0, 1)),
                    .Background = If(R = 0, CType(New MyColor(246, 247, 251), Brush), Brushes.Transparent)}
                Grid.SetRow(Holder, R)
                Grid.SetColumn(Holder, C)
                Grid.Children.Add(Holder)
            Next
        Next
        Dim Wrapper As New Border With {
            .Child = Grid,
            .BorderBrush = New MyColor(228, 231, 240),
            .BorderThickness = New Thickness(1),
            .CornerRadius = New CornerRadius(4),
            .Margin = New Thickness(0, 3, 0, 3),
            .HorizontalAlignment = HorizontalAlignment.Left,
            .Tag = Text}
        Return Wrapper
    End Function
    Private Shared Sub UpdateBlockElement(Element As UIElement, Model As AgentBlock)
        Dim Panel As WrapPanel = TryCast(Element, WrapPanel)
        If Panel Is Nothing Then Return
        Dim Display As String = ""
        Dim Styles As List(Of Integer) = BuildInlineStyles(Model.Text, Display)
        Dim Rendered As Integer = If(Panel.Tag Is Nothing, 0, CInt(Panel.Tag))
        If Rendered >= Display.Length Then Return
        Dim Size As Double = If(Model.Kind = 1, If(Model.Level <= 1, 16.0, 14.5), 13.0)
        Dim Weight As FontWeight = If(Model.Kind = 1, FontWeights.SemiBold, FontWeights.Normal)
        Dim ForeColor As Color = CType(If(Model.Kind = 3, New MyColor(110, 116, 135), New MyColor(51, 51, 51)), Color)
        Dim Index As Integer = Rendered
        While Index < Display.Length
            Dim Length As Integer = NextUnit(Display, Index)
            Dim Piece As String = Display.Substring(Index, Length)
            Dim Style As Integer = If(Index < Styles.Count, Styles(Index), 0)
            Dim CharBlock As New TextBlock With {
                .Text = Piece,
                .FontSize = Size,
                .FontWeight = If(Style = 1, FontWeights.Bold, Weight),
                .Foreground = New SolidColorBrush(ForeColor),
                .TextWrapping = TextWrapping.NoWrap}
            If Style = 2 Then
                CharBlock.FontFamily = New FontFamily("Consolas")
                CharBlock.Foreground = New SolidColorBrush(CType(New MyColor(90, 96, 120), Color))
            End If
            Panel.Children.Add(CharBlock)
            AnimateCharAppear(CharBlock)
            Index += Length
        End While
        CompactPanel(Panel)
        Panel.Tag = Display.Length
    End Sub

    Private Shared Function BuildInlineStyles(Text As String, ByRef Display As String) As List(Of Integer)
        Dim Styles As New List(Of Integer)
        Dim Builder As New StringBuilder()
        Dim Bold As Boolean = False
        Dim Code As Boolean = False
        Dim Index As Integer = 0
        While Index < Text.Length
            If Not Code AndAlso Index + 1 < Text.Length AndAlso Text(Index) = "*"c AndAlso Text(Index + 1) = "*"c Then
                Bold = Not Bold
                Index += 2
                Continue While
            End If
            If Text(Index) = ChrW(96) Then
                Code = Not Code
                Index += 1
                Continue While
            End If
            Builder.Append(Text(Index))
            Styles.Add(If(Code, 2, If(Bold, 1, 0)))
            Index += 1
        End While
        Display = Builder.ToString()
        Return Styles
    End Function
    Private Shared Function NextUnit(Text As String, Start As Integer) As Integer
        Dim C As Char = Text(Start)
        If Char.IsLetterOrDigit(C) AndAlso AscW(C) < 128 Then
            Dim Index As Integer = Start
            While Index < Text.Length
                Dim Current As Char = Text(Index)
                If Char.IsLetterOrDigit(Current) AndAlso AscW(Current) < 128 Then
                    Index += 1
                Else
                    Exit While
                End If
            End While
            Return Index - Start
        End If
        Return 1
    End Function

    Private Shared Sub CompactPanel(Panel As WrapPanel)
        Try
            If Panel.Children.Count <= 400 Then Return
            Dim Builder As New StringBuilder()
            Dim RemoveCount As Integer = 250
            For Index As Integer = 0 To RemoveCount - 1
                Dim Block = TryCast(Panel.Children(Index), TextBlock)
                If Block IsNot Nothing Then Builder.Append(Block.Text)
            Next
            Dim Merged As New TextBlock With {
                .Text = Builder.ToString(),
                .FontSize = 13,
                .Foreground = New MyColor(51, 51, 51),
                .TextWrapping = TextWrapping.NoWrap}
            For Index As Integer = RemoveCount - 1 To 0 Step -1
                Panel.Children.RemoveAt(Index)
            Next
            Panel.Children.Insert(0, Merged)
        Catch
        End Try
    End Sub
    Private Shared Sub AnimateCharAppear(CharBlock As TextBlock)
        Try
            CharBlock.Opacity = 0
            Dim Ease As New CubicEase With {.EasingMode = EasingMode.EaseInOut}
            CharBlock.BeginAnimation(UIElement.OpacityProperty, New DoubleAnimation(1, New Duration(TimeSpan.FromMilliseconds(120))) With {.EasingFunction = Ease})
        Catch
        End Try
    End Sub

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
        Dim AreaTop As Double = 8
        Dim AreaBottom As Double = Math.Max(AreaTop + 40, AnchorY - 8)
        Dim Available As Double = AreaBottom - AreaTop
        Dim LongMax As Double = Math.Max(60, Math.Min(Available * 0.62, Available))
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
            Entry.NeedScroll = ContentHeight > LongMax - 18
            If Entry.NeedScroll Then
                Box.Height = LongMax
                Box.ClipToBounds = True
            Else
                Box.Height = Double.NaN
                Box.ClipToBounds = False
            End If
            Box.Measure(New Size(330, If(Entry.NeedScroll, LongMax, Double.PositiveInfinity)))
            Dim Height As Double = If(Entry.NeedScroll, LongMax, Math.Max(20, Box.DesiredSize.Height))
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
        PanMessageLayer.Clip = New RectangleGeometry(New Rect(0, AreaTop, Math.Max(0, ActualWidth), Math.Max(0, AreaBottom - AreaTop)))
        WheelCatcher.Width = 320
        WheelCatcher.Height = Math.Max(0, AreaBottom - Math.Min(Bottom, AreaBottom))
        Canvas.SetLeft(WheelCatcher, BaseX)
        Canvas.SetTop(WheelCatcher, Math.Min(Bottom, AreaBottom))
    End Sub

    Private Sub UpdateScrollBar(Entry As BubbleEntry)
        Try
            If Entry.Thumb Is Nothing OrElse Entry.Track Is Nothing OrElse Entry.Viewer Is Nothing Then Return
            If Entry.DirtyContent Then
                Entry.ContentCache = MeasureContent(Entry)
                Entry.DirtyContent = False
            End If
            Dim Content As Double = Entry.ContentCache
            Dim Viewport As Double = Entry.Viewer.ViewportHeight
            If Viewport <= 0 OrElse Content <= Viewport + 1 Then
                Entry.Thumb.Visibility = Visibility.Collapsed
                Return
            End If
            Entry.Thumb.Visibility = Visibility.Visible
            Dim TrackHeight As Double = Math.Max(10, Entry.Track.ActualHeight)
            Dim ThumbHeight As Double = Math.Max(16, TrackHeight * Viewport / Content)
            Entry.Thumb.Height = ThumbHeight
            Dim Range As Double = Math.Max(0.1, Content - Viewport)
            Dim Move As Double = (TrackHeight - ThumbHeight) * Math.Min(1, Entry.Viewer.VerticalOffset / Range)
            Canvas.SetTop(Entry.Thumb, Math.Max(0, Move))
            Canvas.SetLeft(Entry.Thumb, 1)
        Catch
        End Try
    End Sub
    Private Shared Function MeasureContent(Entry As BubbleEntry) As Double
        Try
            Entry.Host.Measure(New Size(300, Double.PositiveInfinity))
            Return Entry.Host.DesiredSize.Height
        Catch
            Return 0
        End Try
    End Function

    Private Sub OnWheel(sender As Object, e As MouseWheelEventArgs)
        If PanInput.Visibility <> Visibility.Visible Then Return
        Dim Position As Point = e.GetPosition(PanMessageLayer)
        Dim BestEntry As BubbleEntry = Nothing
        Dim BestOverflow As Double = 0
        For Each Entry In Entries
            If Entry.Box.Visibility <> Visibility.Visible Then Continue For
            If Entry.Viewer Is Nothing Then Continue For
            Dim Left As Double = Canvas.GetLeft(Entry.Box)
            Dim Top As Double = Canvas.GetTop(Entry.Box)
            If Double.IsNaN(Left) OrElse Double.IsNaN(Top) Then Continue For
            Dim Inside As Boolean = Position.X >= Left AndAlso Position.X <= Left + Entry.Box.ActualWidth AndAlso
                                    Position.Y >= Top AndAlso Position.Y <= Top + Entry.Box.ActualHeight
            Dim Overflow As Double = Entry.Viewer.ScrollableHeight
            '只有鼠标悬停在超长消息上才接管滚动
            If Inside AndAlso Overflow > 0.5 Then
                BestEntry = Entry
                BestOverflow = Overflow
                Exit For
            End If
        Next
        If BestEntry IsNot Nothing AndAlso BestOverflow > 0.5 Then
            Dim AtTop As Boolean = BestEntry.Viewer.VerticalOffset <= 0.5
            Dim AtBottom As Boolean = BestEntry.Viewer.VerticalOffset >= BestOverflow - 0.5
            If (e.Delta > 0 AndAlso AtTop) OrElse (e.Delta < 0 AndAlso AtBottom) Then
                '边界：交给隐藏/恢复逻辑
            Else
                BestEntry.StickBottom = False
                BestEntry.TargetOffset = Math.Max(0, Math.Min(BestOverflow, BestEntry.Viewer.VerticalOffset - e.Delta / 3.0))
                StartFollow()
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

    Private Sub SetBusy(Busy As Boolean)
        Try
            BtnSend.Visibility = If(Busy, Visibility.Collapsed, Visibility.Visible)
            PanSendBusy.Visibility = If(Busy, Visibility.Visible, Visibility.Collapsed)
            If Busy Then
                RecStop.Visibility = Visibility.Collapsed
                PanRing.Visibility = Visibility.Visible
                Dim Spin As New DoubleAnimation(0, 360, New Duration(TimeSpan.FromMilliseconds(900))) With {
                    .RepeatBehavior = RepeatBehavior.Forever}
                RingRotate.BeginAnimation(RotateTransform.AngleProperty, Spin)
            Else
                RingRotate.BeginAnimation(RotateTransform.AngleProperty, Nothing)
            End If
        Catch
        End Try
    End Sub
    Private Sub SendMessage()
        Dim Text As String = TxtInput.Text.Trim()
        If Text.Length = 0 Then Return
        If ModAgent.Instance.IsWaiting Then Return
        TxtInput.Text = ""
        SetBusy(True)
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
                           Reply.Notes.Clear()
                           Try
                               RenderEntry(Reply)
                               RequestLayout()
                           Catch ex As Exception
                               Logger.Warn(ex, "AI 消息渲染失败（已忽略，不影响继续输出）")
                           End Try
                       End Sub)
            End Sub,
            Sub(ToolName)
                RunInUi(Sub()
                           Reply.Notes.Add("·  " & ToolName)
                           Try
                               RenderEntry(Reply)
                               LayoutMessages()
                           Catch ex As Exception
                               Logger.Warn(ex, "AI 提示渲染失败")
                           End Try
                       End Sub)
            End Sub,
            Sub()
                RunInUi(Sub()
                           SetBusy(False)
                           If Reply.Notes.Count > 0 Then
                               Reply.Notes.Clear()
                               RenderEntry(Reply)
                           End If
                           LayoutMessages()
                       End Sub)
            End Sub,
            Sub(Message)
                RunInUi(Sub()
                           SetBusy(False)
                           If Message = "已取消" Then
                               Reply.Raw.Append("（已停止）")
                           Else
                               Reply.Raw.Append("出错了：" & Message)
                           End If
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
                   Result = MyMsgBox(Detail, "小瑟请求执行操作", "允许", "拒绝") = 1
                   Finished.Set()
               End Sub)
        Finished.WaitOne(120000)
        Return Result
    End Function

End Class
