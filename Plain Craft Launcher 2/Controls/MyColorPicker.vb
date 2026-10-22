Imports System.Windows.Shapes
Imports System.Windows.Media.Animation
Imports System.Windows.Media.Effects

''' <summary>
''' 启动器风格的取色器：方形 HSV 色板 + 右侧竖向色相条 + 底部颜色预览。
''' 拖动结束（松开鼠标）时触发 Changed。
''' </summary>
Public Class MyColorPicker
    Inherits UserControl

    ''' <summary>拖动结束、颜色确定后触发。</summary>
    Public Event Changed()

    Private Hue As Double = 0      '0~360
    Private Sat As Double = 1      '0~1
    Private Val As Double = 1      '0~1
    Private Dragging As Integer    '0 无，1 色板，2 色相条

    Private ReadOnly SquareBase As New Border()
    Private ReadOnly SquareMarker As New Ellipse()
    Private ReadOnly MarkerScale As New ScaleTransform(1, 1)
    Private ReadOnly HueBar As New Border()
    Private ReadOnly HueMarker As New Rectangle()
    Private ReadOnly Preview As New Border()
    Private ReadOnly PreviewBrush As New SolidColorBrush(Colors.White)
    Private ReadOnly HexLabel As New TextBlock()

    Public ReadOnly Property SelectedColor As Color
        Get
            Return HsvToColor(Hue, Sat, Val)
        End Get
    End Property

    Public Sub New()
        BuildUi()
        RefreshColor(False)
    End Sub

    ''' <summary>载入一个颜色作为当前值（不会触发 Changed）。</summary>
    Public Sub SetColor(Col As Color)
        SetFromColor(Col)
        RefreshColor(False)
    End Sub

    Protected Overrides Function MeasureOverride(availableSize As Size) As Size
        Dim Side As Double = If(Double.IsNaN(Width), 190, Width)
        Dim Board As Double = Side - 18          '右侧色相条 12 + 间距 6
        Dim Final As New Size(Side, Board + 24)  '底部预览 16 + 间距 8
        MyBase.MeasureOverride(Final)
        Return Final
    End Function

    Private Sub BuildUi()
        Dim Root As New Grid()
        Root.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(1, GridUnitType.Star)})
        Root.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})

        '上半部分：色板 + 竖向色相条
        Dim Board As New Grid()
        Board.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Board.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(12)})

        Dim Layers As New Grid With {.Margin = New Thickness(0, 0, 6, 0)}
        Layers.Children.Add(SquareBase)
        Layers.Children.Add(New Border With {
            .Background = New LinearGradientBrush(Color.FromArgb(255, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), New Point(0, 0), New Point(1, 0))})
        Layers.Children.Add(New Border With {
            .Background = New LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), New Point(0, 0), New Point(0, 1))})
        SquareMarker.Width = 12
        SquareMarker.Height = 12
        SquareMarker.Stroke = Brushes.White
        SquareMarker.StrokeThickness = 2
        SquareMarker.IsHitTestVisible = False
        SquareMarker.RenderTransformOrigin = New Point(0.5, 0.5)
        SquareMarker.RenderTransform = MarkerScale
        SquareMarker.Effect = New DropShadowEffect With {.BlurRadius = 5, .ShadowDepth = 1, .Opacity = 0.35, .Direction = 270}
        Dim SquareCanvas As New Canvas()
        SquareCanvas.Children.Add(SquareMarker)
        Layers.Children.Add(SquareCanvas)
        Grid.SetColumn(Layers, 0)
        Board.Children.Add(Layers)

        HueBar.CornerRadius = New CornerRadius(6)
        HueBar.ClipToBounds = True
        HueBar.Background = MakeHueBrush()
        HueMarker.Width = 12
        HueMarker.Height = 14
        HueMarker.RadiusX = 3
        HueMarker.RadiusY = 3
        HueMarker.Stroke = Brushes.White
        HueMarker.StrokeThickness = 2
        HueMarker.Fill = Brushes.Transparent
        HueMarker.IsHitTestVisible = False
        Dim HueCanvas As New Canvas()
        HueCanvas.Children.Add(HueMarker)
        HueBar.Child = HueCanvas
        Grid.SetColumn(HueBar, 1)
        Board.Children.Add(HueBar)

        Grid.SetRow(Board, 0)
        Root.Children.Add(Board)

        '底部：颜色预览 + 十六进制值
        Dim Bottom As New Grid With {.Margin = New Thickness(0, 8, 0, 0)}
        Bottom.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Bottom.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Preview.Height = 16
        Preview.CornerRadius = New CornerRadius(4)
        Preview.Background = PreviewBrush
        Grid.SetColumn(Preview, 0)
        Bottom.Children.Add(Preview)
        HexLabel.Margin = New Thickness(8, 0, 0, 0)
        HexLabel.VerticalAlignment = VerticalAlignment.Center
        HexLabel.FontSize = 11
        HexLabel.Foreground = New MyColor(138, 138, 138)
        Grid.SetColumn(HexLabel, 1)
        Bottom.Children.Add(HexLabel)
        Grid.SetRow(Bottom, 1)
        Root.Children.Add(Bottom)

        Content = Root

        '色板四角用圆角裁剪，避免生硬的直角边框
        AddHandler Layers.SizeChanged,
            Sub(sender, e)
                If Layers.ActualWidth > 0 AndAlso Layers.ActualHeight > 0 Then
                    Layers.Clip = New RectangleGeometry(New Rect(0, 0, Layers.ActualWidth, Layers.ActualHeight), 4, 4)
                End If
                RefreshColor(False)
            End Sub

        AddHandler Layers.MouseLeftButtonDown, Sub(sender, e) StartDrag(1, Layers, e)
        AddHandler Layers.MouseMove, Sub(sender, e) ContinueDragSquare(e.GetPosition(Layers))
        AddHandler Layers.MouseLeftButtonUp, Sub(sender, e) EndDrag(1, Layers)
        AddHandler Layers.MouseEnter, Sub(sender, e) AnimateMarker(1.3)
        AddHandler Layers.MouseLeave, Sub(sender, e) AnimateMarker(1)
        AddHandler HueBar.MouseLeftButtonDown, Sub(sender, e) StartDrag(2, HueBar, e)
        AddHandler HueBar.MouseMove, Sub(sender, e) ContinueDragHue(e.GetPosition(HueBar))
        AddHandler HueBar.MouseLeftButtonUp, Sub(sender, e) EndDrag(2, HueBar)
        AddHandler HueBar.SizeChanged, Sub(sender, e) RefreshColor(False)
    End Sub
    Private Sub StartDrag(Which As Integer, Source As UIElement, e As MouseButtonEventArgs)
        Dragging = Which
        Source.CaptureMouse()
        AnimateMarker(1.4)
        If Which = 1 Then ContinueDragSquare(e.GetPosition(Source)) Else ContinueDragHue(e.GetPosition(Source))
    End Sub
    Private Sub EndDrag(Which As Integer, Source As UIElement)
        If Dragging = Which Then Dragging = 0
        Source.ReleaseMouseCapture()
        AnimateMarker(1)
        RaiseEvent Changed()
    End Sub
    Private Sub ContinueDragSquare(Position As Point)
        If Dragging <> 1 Then Return
        If SquareBase.ActualWidth > 0 Then Sat = (Position.X / SquareBase.ActualWidth).Clamp(0, 1)
        If SquareBase.ActualHeight > 0 Then Val = 1 - (Position.Y / SquareBase.ActualHeight).Clamp(0, 1)
        RefreshColor(True)
    End Sub
    Private Sub ContinueDragHue(Position As Point)
        If Dragging <> 2 Then Return
        If HueBar.ActualHeight > 0 Then Hue = (Position.Y / HueBar.ActualHeight).Clamp(0, 1) * 360
        RefreshColor(True)
    End Sub

    ''' <summary>刷新色板与游标；Animate 为 True 时给预览色加过渡动画。</summary>
    Private Sub RefreshColor(Animate As Boolean)
        SquareBase.Background = New SolidColorBrush(HsvToColor(Hue, 1, 1))
        If SquareBase.ActualWidth > 0 Then
            Canvas.SetLeft(SquareMarker, Sat * SquareBase.ActualWidth - 6)
            Canvas.SetTop(SquareMarker, (1 - Val) * SquareBase.ActualHeight - 6)
        End If
        If HueBar.ActualHeight > 0 Then
            HueMarker.Height = 14
            Canvas.SetLeft(HueMarker, 0)
            Canvas.SetTop(HueMarker, Hue / 360 * HueBar.ActualHeight - 7)
        End If

        Dim Now As Color = SelectedColor
        If Animate Then
            PreviewBrush.BeginAnimation(SolidColorBrush.ColorProperty, New ColorAnimation(Now, TimeSpan.FromMilliseconds(140)))
        Else
            PreviewBrush.BeginAnimation(SolidColorBrush.ColorProperty, Nothing)
            PreviewBrush.Color = Now
        End If
        HexLabel.Text = $"#{Now.R:X2}{Now.G:X2}{Now.B:X2}"
    End Sub

    Private Sub AnimateMarker(Scale As Double)
        Dim Animation As New DoubleAnimation(Scale, TimeSpan.FromMilliseconds(130)) With {.EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseOut}}
        MarkerScale.BeginAnimation(ScaleTransform.ScaleXProperty, Animation)
        MarkerScale.BeginAnimation(ScaleTransform.ScaleYProperty, Animation)
    End Sub

    Private Sub SetFromColor(Col As Color)
        Dim R As Double = Col.R / 255
        Dim G As Double = Col.G / 255
        Dim B As Double = Col.B / 255
        Dim Max As Double = Math.Max(R, Math.Max(G, B))
        Dim Min As Double = Math.Min(R, Math.Min(G, B))
        Val = Max
        Sat = If(Max = 0, 0, (Max - Min) / Max)
        Hue = 0
        If Max <> Min Then
            Dim D As Double = Max - Min
            If Max = R Then
                Hue = ((G - B) / D + If(G < B, 6, 0)) * 60
            ElseIf Max = G Then
                Hue = ((B - R) / D + 2) * 60
            Else
                Hue = ((R - G) / D + 4) * 60
            End If
        End If
    End Sub

    Private Shared Function HsvToColor(H As Double, S As Double, V As Double) As Color
        H = ((H Mod 360) + 360) Mod 360
        Dim C As Double = V * S
        Dim X As Double = C * (1 - Math.Abs((H / 60) Mod 2 - 1))
        Dim M As Double = V - C
        Dim R As Double = 0, G As Double = 0, B As Double = 0
        Select Case CInt(Math.Floor(H / 60))
            Case 0 : R = C : G = X
            Case 1 : R = X : G = C
            Case 2 : G = C : B = X
            Case 3 : G = X : B = C
            Case 4 : R = X : B = C
            Case Else : R = C : B = X
        End Select
        Return Color.FromRgb(CByte(Math.Round((R + M) * 255)), CByte(Math.Round((G + M) * 255)), CByte(Math.Round((B + M) * 255)))
    End Function

    Private Shared Function MakeHueBrush() As Brush
        Dim Brush As New LinearGradientBrush With {.StartPoint = New Point(0, 0), .EndPoint = New Point(0, 1)}
        For Index As Integer = 0 To 6
            Brush.GradientStops.Add(New GradientStop(HsvToColor(Index * 60, 1, 1), Index / 6))
        Next
        Return Brush
    End Function

End Class
