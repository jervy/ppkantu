using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LiteImageViewer.Models;
using LiteImageViewer.ViewModels;
using Path = System.Windows.Shapes.Path;
using System.IO;
using System.Diagnostics;
using System;

namespace LiteImageViewer.Views;

public partial class ImageViewerControl : UserControl
{
    private enum CropDragMode
    {
        None,
        New,
        Move,
        ResizeN,
        ResizeS,
        ResizeE,
        ResizeW,
        ResizeNE,
        ResizeNW,
        ResizeSE,
        ResizeSW
    }

    private bool _isPanning;
    private Point _panStartPoint;
    private Point _panStartTranslate;
    private bool _isFitToWindow = true;
    private MainViewModel? _subscribedViewModel;
    private bool _mouseHandlersAttached;
    private NavEdgeDragState _navEdgeDragState = NavEdgeDragState.None;
    private Point _navEdgeDragStart;
    private int _lastMaximizedNavClickTick;

    // 裁切状态
    private bool _isCropDragging;
    private Point _cropScreenStart;
    private Rect _cropScreenRect;
    private CropDragMode _cropDragMode = CropDragMode.None;
    private Point _cropDragOriginalPos;
    private Rect _cropDragOriginalRect;

    private const double CropEdgeThreshold = 8.0;
    private const double CropMinSize = 10.0;

    // 标注状态
    private bool _isAnnotationDragging;
    private Point _annotationScreenStart;
    private FrameworkElement? _currentDrawingShape;
    private Brush _selectedAnnotationOriginalStroke = Brushes.Red;
    private Brush _selectedAnnotationOriginalFill = Brushes.Transparent;
    private double _selectedAnnotationOriginalThickness = 3;

    // 画笔工具状态
    private List<Point> _currentBrushPoints = new();
    private FrameworkElement? _selectedAnnotation;
    private bool _isMovingAnnotation;
    private Point _annotationMoveLast;
    private bool _annotationMoved;

    // 文字缩放控制点
    private Rectangle? _textResizeHandle;
    private bool _isResizingText;
    private Point _textResizeStart;
    private double _textResizeStartFontSize;

    // 标注覆盖层历史（主要用于文字标注的撤销/重做，不退出标注模式）
    private readonly List<List<AnnotationShape>> _annotationOverlayHistory = new();
    private int _annotationOverlayHistoryIndex = -1;
    private bool _restoringAnnotationOverlayHistory;

    // 涂抹模式状态
    private bool _isDoodleDragging;
    private Point _doodleScreenStart;
    private Polyline? _currentDoodlePolyline;
    private readonly List<Polyline> _doodlePolylines = new();

    private enum NavEdgeDragState
    {
        None,
        Left,
        Right
    }

    private const double NavResizeDragThreshold = 4.0;

    public event EventHandler<IReadOnlyList<AnnotationShape>>? PendingAnnotationsChanged;
    public event EventHandler? PendingAnnotationsCleared;

    private sealed record AnnotationVisualMetadata(AnnotationTool Tool, string Color, double StrokeThickness, Point StartPoint = default, Point EndPoint = default);

    public ImageViewerControl()
    {
        InitializeComponent();
        AttachViewerMouseHandlers();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    public MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UnsubscribeViewModel(e.OldValue as MainViewModel);
        SubscribeViewModel(ViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && _subscribedViewModel == null)
            SubscribeViewModel(ViewModel);

        AttachViewerMouseHandlers();

        // GIF 图片共享主图的变换（缩放/平移）
        GifImage.RenderTransform = MainImage.RenderTransform;

        SizeChanged += (_, _) =>
        {
            if (_isFitToWindow && (ViewModel?.CurrentImage != null || ViewModel?.IsGif == true))
                FitToWindow();
        };

        MainImage.SizeChanged += (_, _) =>
        {
            if (_isFitToWindow && (ViewModel?.CurrentImage != null || ViewModel?.IsGif == true))
                FitToWindow();
        };

        GifImage.SizeChanged += (_, _) =>
        {
            if (_isFitToWindow && ViewModel?.IsGif == true)
                FitToWindow();
        };

        ViewerGrid.AddHandler(MouseDoubleClickEvent, new MouseButtonEventHandler((_, _) =>
        {
            if (_isFitToWindow) ShowOriginalSize();
            else FitToWindow();
        }));

        // 标注层鼠标事件：选中/删除
        AnnotationOverlay.MouseLeftButtonDown += OnAnnotationOverlayMouseLeftDown;
        AnnotationOverlay.MouseMove += OnAnnotationOverlayMouseMove;
        AnnotationOverlay.MouseLeave += (_, _) => { if (ViewModel?.IsAnnotationMode == true) Cursor = Cursors.Cross; };
    }

    private void AttachViewerMouseHandlers()
    {
        if (_mouseHandlersAttached) return;
        _mouseHandlersAttached = true;

        // 裁切/标注模式必须走 Preview，并且 handledEventsToo=true，避免图片模板、透明层、按钮等
        // 子元素先处理鼠标事件后导致 ViewerGrid 收不到拖拽事件。
        ViewerGrid.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnPreviewMouseLeftButtonDown), true);
        ViewerGrid.AddHandler(UIElement.PreviewMouseMoveEvent,
            new MouseEventHandler(OnPreviewMouseMove), true);
        ViewerGrid.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnPreviewMouseLeftButtonUp), true);

        // 普通看图平移仍使用冒泡事件，避免 Preview 阶段抢走左右导航按钮等子控件点击。
        ViewerGrid.AddHandler(UIElement.MouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnMouseLeftButtonDown), false);
        ViewerGrid.AddHandler(UIElement.MouseMoveEvent,
            new MouseEventHandler(OnMouseMove), false);
        ViewerGrid.AddHandler(UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnMouseLeftButtonUp), false);

        CropOverlay.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnPreviewMouseLeftButtonDown), true);
        CropOverlay.AddHandler(UIElement.PreviewMouseMoveEvent,
            new MouseEventHandler(OnPreviewMouseMove), true);
        CropOverlay.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnPreviewMouseLeftButtonUp), true);

        DoodleOverlay.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnPreviewMouseLeftButtonDown), true);
        DoodleOverlay.AddHandler(UIElement.PreviewMouseMoveEvent,
            new MouseEventHandler(OnPreviewMouseMove), true);
        DoodleOverlay.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnPreviewMouseLeftButtonUp), true);
    }

    private void SubscribeViewModel(MainViewModel? vm)
    {
        if (vm == null || _subscribedViewModel != null) return;
        _subscribedViewModel = vm;
        vm.NeedFitToWindow += (_, _) => FitToWindow();
        vm.NeedOriginalSize += (_, _) => ShowOriginalSize();
        vm.NeedZoomIn += (_, _) => ApplyZoom(1.25);
        vm.NeedZoomOut += (_, _) => ApplyZoom(1.0 / 1.25);
        vm.NeedConfirmCrop += async (_, _) => await ConfirmCropAsync();
        vm.NeedConfirmAnnotation += async (_, _) => await ConfirmAnnotationAsync();
        vm.NeedConfirmDoodle += async (_, _) => await ConfirmDoodleAsync();
        vm.NeedDeleteAnnotation += (_, _) => HandleDeleteKey();
        vm.NeedUndoAnnotation += (_, _) => UndoAnnotationOverlay();
        vm.NeedRedoAnnotation += (_, _) => RedoAnnotationOverlay();
        vm.NeedFlushAnnotations += (_, _) => FlushPendingAnnotations();
        vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateNavVisibility();
    }

    private void UnsubscribeViewModel(MainViewModel? vm)
    {
        if (vm == null || _subscribedViewModel != vm) return;
        vm.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentImage))
            OnImageChanged();
        else if (e.PropertyName == nameof(MainViewModel.IsCropMode))
            OnCropModeChanged();
        else if (e.PropertyName == nameof(MainViewModel.IsAnnotationMode))
            OnAnnotationModeChanged();
        else if (e.PropertyName == nameof(MainViewModel.IsDoodleMode))
            OnDoodleModeChanged();
        else if (e.PropertyName == nameof(MainViewModel.HasImage))
            UpdateNavVisibility();
    }

    private void OnImageChanged()
    {
        ResetAll();
        ClearPendingAnnotations();
        if (ViewModel?.CurrentImage == null) return;
        _isFitToWindow = true;
    }

    private void OnCropModeChanged()
    {
        if (ViewModel?.IsCropMode == true)
        {
            CropOverlay.Visibility = Visibility.Visible;
            CropOverlay.Width = ViewerGrid.ActualWidth;
            CropOverlay.Height = ViewerGrid.ActualHeight;
            AnnotationOverlay.Visibility = Visibility.Collapsed;
            DoodleOverlay.Visibility = Visibility.Collapsed;
            SetViewerCursor(Cursors.Cross);
            HideFloatingButtons();
            ShowCropToolbar();
            LeftNavBtn.Visibility = Visibility.Collapsed;
            RightNavBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            CropOverlay.Visibility = Visibility.Collapsed;
            if (ViewModel?.IsAnnotationMode != true && ViewModel?.IsDoodleMode != true) SetViewerCursor(Cursors.Arrow);
            _cropDragMode = CropDragMode.None;
            _isCropDragging = false;
            ViewerGrid.ReleaseMouseCapture();
            CropOverlay.ReleaseMouseCapture();
            ClearCropSelection();
            HideFloatingButtons();
            HideCropToolbar();
            UpdateNavVisibility();
        }
    }

    private void ShowCropToolbar()
    {
        CropToolbarCanvas.Visibility = Visibility.Visible;
        UpdateCropRatioButtons();
    }

    private bool IsEventFromModeToolbar(DependencyObject? source)
    {
        while (source != null)
        {
            if (ReferenceEquals(source, CropToolbarCanvas) ||
                ReferenceEquals(source, AnnotationToolbarCanvas) ||
                ReferenceEquals(source, DoodleThicknessPanel) ||
                ReferenceEquals(source, FloatingButtonsCanvas) ||
                ReferenceEquals(source, FloatingButtons))
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void HideCropToolbar()
    {
        CropToolbarCanvas.Visibility = Visibility.Collapsed;
    }

    private void UpdateCropRatioButtons()
    {
        var ratio = ViewModel?.CropAspectRatio ?? 0;
        var buttons = new[] { ToolCropFreeBtn, ToolCrop1x1Btn, ToolCrop4x3Btn, ToolCrop3x2Btn, ToolCrop16x9Btn, ToolCrop9x16Btn };
        var ratios = new[] { 0.0, 1.0, 4.0/3.0, 3.0/2.0, 16.0/9.0, 9.0/16.0 };
        for (int i = 0; i < buttons.Length; i++)
        {
            var match = Math.Abs(ratio - ratios[i]) < 0.01;
            buttons[i].Background = match
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55))
                : System.Windows.Media.Brushes.Transparent;
            buttons[i].Foreground = match
                ? System.Windows.Media.Brushes.White
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
        }
    }

    private void OnCropRatioClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && ViewModel != null)
        {
            ViewModel.CropAspectRatio = double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
            UpdateCropRatioButtons();
            // 如果已有选区，按新比例调整
            if (_cropScreenRect.Width > 5 && _cropScreenRect.Height > 5)
            {
                ConstrainCropToRatio();
                UpdateCropOverlay();
            }
        }
    }

    private void ConstrainCropToRatio()
    {
        var ratio = ViewModel?.CropAspectRatio ?? 0;
        if (ratio <= 0) return; // 自由比例不约束

        var r = _cropScreenRect;
        var centerX = r.Left + r.Width / 2.0;
        var centerY = r.Top + r.Height / 2.0;

        // 以当前选区的较长边为基准
        if (r.Width / r.Height > ratio)
        {
            // 宽度过大，收缩宽度
            var newW = r.Height * ratio;
            _cropScreenRect = new Rect(centerX - newW / 2.0, r.Top, newW, r.Height);
        }
        else
        {
            // 高度过大，收缩高度
            var newH = r.Width / ratio;
            _cropScreenRect = new Rect(r.Left, centerY - newH / 2.0, r.Width, newH);
        }

        // 比例重算后再做一次边界收口，避免选区跑出视口
        ClampAspectRatioCropToViewport(ref _cropScreenRect, ratio);
    }

    private void OnAnnotationModeChanged()
    {
        if (ViewModel?.IsAnnotationMode == true)
        {
            AnnotationOverlay.Visibility = Visibility.Visible;
            AnnotationOverlay.Width = ViewerGrid.ActualWidth;
            AnnotationOverlay.Height = ViewerGrid.ActualHeight;
            CropOverlay.Visibility = Visibility.Collapsed;
            DoodleOverlay.Visibility = Visibility.Collapsed;
            SetViewerCursor(Cursors.Cross);
            HideFloatingButtons();
            ResetAnnotationOverlayHistory();
            ShowAnnotationToolbar();
            LeftNavBtn.Visibility = Visibility.Collapsed;
            RightNavBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            AnnotationOverlay.Visibility = Visibility.Collapsed;
            if (ViewModel?.IsCropMode != true && ViewModel?.IsDoodleMode != true) SetViewerCursor(Cursors.Arrow);
            _currentDrawingShape = null;
            _selectedAnnotation = null;
            _currentBrushPoints.Clear();
            HideFloatingButtons();
            HideAnnotationToolbar();
            UpdateNavVisibility();
        }
    }

    private void OnDoodleModeChanged()
    {
        if (ViewModel?.IsDoodleMode == true)
        {
            DoodleOverlay.Visibility = Visibility.Visible;
            DoodleOverlay.Width = ViewerGrid.ActualWidth;
            DoodleOverlay.Height = ViewerGrid.ActualHeight;
            CropOverlay.Visibility = Visibility.Collapsed;
            AnnotationOverlay.Visibility = Visibility.Collapsed;
            SetViewerCursor(Cursors.Cross);
            HideFloatingButtons();
            DoodleThicknessPanel.Visibility = Visibility.Visible;
            UpdateDoodleThicknessButtons();
            LeftNavBtn.Visibility = Visibility.Collapsed;
            RightNavBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            DoodleOverlay.Visibility = Visibility.Collapsed;
            if (ViewModel?.IsCropMode != true && ViewModel?.IsAnnotationMode != true) SetViewerCursor(Cursors.Arrow);
            ClearDoodleOverlay();
            HideFloatingButtons();
            DoodleThicknessPanel.Visibility = Visibility.Collapsed;
            UpdateNavVisibility();
        }
    }

    private void ShowAnnotationToolbar()
    {
        AnnotationToolbarCanvas.Visibility = Visibility.Visible;
        UpdateAnnotationToolButtons();
        UpdateColorButtons();
    }

    private void HideAnnotationToolbar()
    {
        AnnotationToolbarCanvas.Visibility = Visibility.Collapsed;
    }

    private void UpdateAnnotationToolButtons()
    {
        var tool = ViewModel?.CurrentAnnotationTool ?? AnnotationTool.Rectangle;
        ToolRectangleBtn.Background = tool == AnnotationTool.Rectangle
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55))
            : System.Windows.Media.Brushes.Transparent;
        ToolRectangleBtn.Foreground = tool == AnnotationTool.Rectangle
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
        ToolLineBtn.Background = tool == AnnotationTool.Line
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55))
            : System.Windows.Media.Brushes.Transparent;
        ToolLineBtn.Foreground = tool == AnnotationTool.Line
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
        ToolArrowBtn.Background = tool == AnnotationTool.Arrow
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55))
            : System.Windows.Media.Brushes.Transparent;
        ToolArrowBtn.Foreground = tool == AnnotationTool.Arrow
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
        ToolBrushBtn.Background = tool == AnnotationTool.Brush
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55))
            : System.Windows.Media.Brushes.Transparent;
        ToolBrushBtn.Foreground = tool == AnnotationTool.Brush
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
        ToolEllipseBtn.Background = tool == AnnotationTool.Ellipse
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55))
            : System.Windows.Media.Brushes.Transparent;
        ToolEllipseBtn.Foreground = tool == AnnotationTool.Ellipse
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
        ToolTextBtn.Background = tool == AnnotationTool.Text
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55))
            : System.Windows.Media.Brushes.Transparent;
        ToolTextBtn.Foreground = tool == AnnotationTool.Text
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
    }

    private void OnAnnotationToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && ViewModel != null)
        {
            ViewModel.CurrentAnnotationTool = tag switch
            {
                "Line" => AnnotationTool.Line,
                "Arrow" => AnnotationTool.Arrow,
                "Brush" => AnnotationTool.Brush,
                "Ellipse" => AnnotationTool.Ellipse,
                "Text" => AnnotationTool.Text,
                _ => AnnotationTool.Rectangle
            };
            UpdateAnnotationToolButtons();
        }
    }

    private void UpdateBrushThicknessButtons()
    {
        // kept for backward compatibility; now unused in annotation mode
    }

    private void UpdateDoodleThicknessButtons()
    {
        var active = ViewModel?.DoodleThickness ?? 30;
        DoodleThinBtn.Background = Math.Abs(active - 15) < 0.1
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55))
            : System.Windows.Media.Brushes.Transparent;
        DoodleThinBtn.Foreground = Math.Abs(active - 15) < 0.1
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
        DoodleMediumBtn.Background = Math.Abs(active - 30) < 0.1
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55))
            : System.Windows.Media.Brushes.Transparent;
        DoodleMediumBtn.Foreground = Math.Abs(active - 30) < 0.1
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
        DoodleThickBtn.Background = Math.Abs(active - 50) < 0.1
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55))
            : System.Windows.Media.Brushes.Transparent;
        DoodleThickBtn.Foreground = Math.Abs(active - 50) < 0.1
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
    }

    private void OnBrushThicknessClick(object sender, RoutedEventArgs e)
    {
        // kept for backward compatibility; now unused
    }

    private void OnDoodleThicknessClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && ViewModel != null)
        {
            if (double.TryParse(tag, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var thickness))
            {
                ViewModel.DoodleThickness = thickness;
                UpdateDoodleThicknessButtons();
            }
        }
    }


    private void OnColorClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Shapes.Ellipse ellipse && ellipse.Tag is string color && ViewModel != null)
        {
            ViewModel.AnnotationColor = color;
            UpdateColorButtons();
        }
    }

    private void UpdateColorButtons()
    {
        var current = ViewModel?.AnnotationColor ?? "#FFE53935";
        var colorBtns = new (System.Windows.Shapes.Ellipse btn, string color)[]
        {
            (ColorRedBtn, "#FFE53935"), (ColorBlueBtn, "#FF2196F3"),
            (ColorGreenBtn, "#FF4CAF50"), (ColorYellowBtn, "#FFFFC107"),
            (ColorOrangeBtn, "#FFFF9800"), (ColorPurpleBtn, "#FF9C27B0"),
            (ColorWhiteBtn, "#FFFFFFFF"), (ColorBlackBtn, "#FF000000")
        };
        foreach (var (btn, c) in colorBtns)
        {
            btn.Stroke = string.Equals(current, c, StringComparison.OrdinalIgnoreCase)
                ? System.Windows.Media.Brushes.White
                : System.Windows.Media.Brushes.Transparent;
        }
    }

    /// <summary>
    /// 适应窗口
    /// </summary>
    public void FitToWindow()
    {
        ResetAll();
        _isFitToWindow = true;
        if (ViewModel != null) ViewModel.ZoomLevel = 1.0;
    }

    /// <summary>
    /// 原始大小 1:1
    /// </summary>
    public void ShowOriginalSize()
    {
        if (ViewModel?.CurrentImage == null) return;

        var vw = ViewerGrid.ActualWidth;
        var vh = ViewerGrid.ActualHeight;
        var iw = ViewModel.CurrentImage.PixelWidth;
        var ih = ViewModel.CurrentImage.PixelHeight;
        if (vw <= 0 || vh <= 0 || iw <= 0 || ih <= 0) return;

        var uniformScale = Math.Min(vw / iw, vh / ih);
        var s = 1.0 / uniformScale;

        ImageScaleTransform.ScaleX = s;
        ImageScaleTransform.ScaleY = s;

        // 修正居中：Image 已由 WPF 居中布局在 (layoutX, layoutY)，
        // ScaleTransform 从 (0,0) 缩放，需要补偿偏移使缩放后的图片居中
        var renderedW = iw * uniformScale;
        var renderedH = ih * uniformScale;
        ImageTranslateTransform.X = renderedW * (1 - s) / 2.0;
        ImageTranslateTransform.Y = renderedH * (1 - s) / 2.0;

        _isFitToWindow = false;
        if (ViewModel != null) ViewModel.ZoomLevel = s;
    }

    private void ResetAll()
    {
        ImageScaleTransform.ScaleX = 1.0;
        ImageScaleTransform.ScaleY = 1.0;
        ImageTranslateTransform.X = 0;
        ImageTranslateTransform.Y = 0;
    }

    #region 坐标转换

    /// <summary>
    /// 获取屏幕到图片坐标的缩放因子
    /// </summary>
    private double GetScreenToImageScale()
    {
        if (ViewModel?.CurrentImage == null) return 1.0;
        var vw = ViewerGrid.ActualWidth;
        var vh = ViewerGrid.ActualHeight;
        var iw = ViewModel.CurrentImage.PixelWidth;
        var ih = ViewModel.CurrentImage.PixelHeight;
        var uniformScale = Math.Min(vw / iw, vh / ih);
        return uniformScale * ImageScaleTransform.ScaleX;
    }

    private double ScreenLengthToImageLength(double screenLength)
    {
        var scale = GetScreenToImageScale();
        return scale > 0 ? screenLength / scale : screenLength;
    }

    private double ImageLengthToScreenLength(double imageLength)
    {
        var scale = GetScreenToImageScale();
        return scale > 0 ? imageLength * scale : imageLength;
    }

    private Point ScreenToImageCoords(Point screenPt)
    {
        if (ViewModel?.CurrentImage == null) return screenPt;

        var vw = ViewerGrid.ActualWidth;
        var vh = ViewerGrid.ActualHeight;
        var iw = ViewModel.CurrentImage.PixelWidth;
        var ih = ViewModel.CurrentImage.PixelHeight;

        var uniformScale = Math.Min(vw / iw, vh / ih);
        var renderedW = iw * uniformScale;
        var renderedH = ih * uniformScale;
        var layoutX = (vw - renderedW) / 2.0;
        var layoutY = (vh - renderedH) / 2.0;

        var scaleX = ImageScaleTransform.ScaleX;
        var scaleY = ImageScaleTransform.ScaleY;
        var tx = ImageTranslateTransform.X;
        var ty = ImageTranslateTransform.Y;

        var imgX = (screenPt.X - layoutX - tx) / (uniformScale * scaleX);
        var imgY = (screenPt.Y - layoutY - ty) / (uniformScale * scaleY);

        return new Point(imgX, imgY);
    }

    private Point ImageToScreenCoords(Point imgPt)
    {
        if (ViewModel?.CurrentImage == null) return imgPt;

        var vw = ViewerGrid.ActualWidth;
        var vh = ViewerGrid.ActualHeight;
        var iw = ViewModel.CurrentImage.PixelWidth;
        var ih = ViewModel.CurrentImage.PixelHeight;

        var uniformScale = Math.Min(vw / iw, vh / ih);
        var renderedW = iw * uniformScale;
        var renderedH = ih * uniformScale;
        var layoutX = (vw - renderedW) / 2.0;
        var layoutY = (vh - renderedH) / 2.0;

        var scaleX = ImageScaleTransform.ScaleX;
        var scaleY = ImageScaleTransform.ScaleY;
        var tx = ImageTranslateTransform.X;
        var ty = ImageTranslateTransform.Y;

        var screenX = imgPt.X * uniformScale * scaleX + layoutX + tx;
        var screenY = imgPt.Y * uniformScale * scaleY + layoutY + ty;

        return new Point(screenX, screenY);
    }

    #endregion

    #region 缩放 — 以视口中心为基准

    private void ApplyZoom(double factor)
    {
        if (ViewModel?.CurrentImage == null) return;
        _isFitToWindow = false;

        var V = ViewerGrid.ActualWidth;
        var H = ViewerGrid.ActualHeight;
        var iw = ViewModel.CurrentImage.PixelWidth;
        var ih = ViewModel.CurrentImage.PixelHeight;
        var uniformScale = Math.Min(V / iw, H / ih);
        var renderedW = iw * uniformScale;
        var renderedH = ih * uniformScale;

        var oldScale = ImageScaleTransform.ScaleX;
        var newScale = Math.Clamp(oldScale * factor, 0.01, 100.0);

        // 图片局部坐标中，视口中心对应的点
        var imgX = (renderedW / 2.0 - ImageTranslateTransform.X) / oldScale;
        var imgY = (renderedH / 2.0 - ImageTranslateTransform.Y) / oldScale;

        ImageScaleTransform.ScaleX = newScale;
        ImageScaleTransform.ScaleY = newScale;
        ImageTranslateTransform.X = renderedW / 2.0 - imgX * newScale;
        ImageTranslateTransform.Y = renderedH / 2.0 - imgY * newScale;

        ViewModel.ZoomLevel = newScale;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel?.CurrentImage == null) return;
        e.Handled = true;
        ApplyZoom(e.Delta > 0 ? 1.15 : 1.0 / 1.15);
    }

    #endregion

    #region 鼠标事件

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // ViewerGrid 和各个 Overlay 都注册了 handledEventsToo=true 的 Preview 事件。
        // 同一次鼠标按下会先经过 ViewerGrid，再经过目标 Overlay；第一次处理后必须直接返回，
        // 否则涂抹模式会创建两条预览 Polyline，松开时只移除当前那条，界面残留一个圆形线帽。
        if (e.Handled)
            return;

        if (IsEventFromModeToolbar(e.OriginalSource as DependencyObject))
            return;

        if (ViewModel?.IsCropMode == true || ViewModel?.IsAnnotationMode == true || ViewModel?.IsDoodleMode == true)
            OnMouseLeftButtonDown(sender, e);
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsEventFromModeToolbar(e.OriginalSource as DependencyObject) && !_isCropDragging && !_isAnnotationDragging && !_isDoodleDragging && !_isResizingText)
            return;

        if (_isCropDragging || _isAnnotationDragging || _isMovingAnnotation || _isDoodleDragging || _isResizingText || ViewModel?.IsCropMode == true || ViewModel?.IsAnnotationMode == true || ViewModel?.IsDoodleMode == true)
            OnMouseLeftButtonUp(sender, e);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (IsEventFromModeToolbar(e.OriginalSource as DependencyObject) && !_isCropDragging && !_isAnnotationDragging && !_isMovingAnnotation && !_isDoodleDragging && !_isResizingText)
            return;

        if (_isCropDragging || _isAnnotationDragging || _isMovingAnnotation || _isDoodleDragging || _isResizingText || ViewModel?.IsCropMode == true || ViewModel?.IsAnnotationMode == true || ViewModel?.IsDoodleMode == true)
            OnMouseMove(sender, e);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsEventFromModeToolbar(e.OriginalSource as DependencyObject))
        {
            e.Handled = false;
            return;
        }

        if (ViewModel?.CurrentImage == null)
        {
            return;
        }

        var pos = GetCropPointerPosition(e);

        // 裁切模式
        if (ViewModel.IsCropMode)
        {
            SetViewerCursor(Cursors.Cross);
            HideFloatingButtons();
            var hitMode = HitTestCropSelection(pos);

            if (hitMode == CropDragMode.Move)
            {
                _cropDragMode = CropDragMode.Move;
                _cropDragOriginalPos = pos;
                _cropDragOriginalRect = _cropScreenRect;
            }
            else if (hitMode != CropDragMode.None)
            {
                _cropDragMode = hitMode;
                _cropDragOriginalPos = pos;
                _cropDragOriginalRect = _cropScreenRect;
            }
            else
            {
                _cropDragMode = CropDragMode.New;
                _cropScreenStart = pos;
                _cropScreenRect = new Rect(pos, pos);
            }

            _isCropDragging = true;
            ViewerGrid.CaptureMouse();
            CropOverlay.CaptureMouse();
            e.Handled = true;
            return;
        }

        // 标注模式
        if (ViewModel.IsAnnotationMode)
        {
            var directHit = e.OriginalSource as DependencyObject;
            if (IsTextResizeHandleSource(directHit))
            {
                StartTextResize(pos, e);
                return;
            }

            var hit = FindAnnotationAt(pos);
            if (hit != null)
            {
                SelectAnnotation(hit);
                StartMoveAnnotation(hit, pos);
                e.Handled = true;
                return;
            }

            DeselectAnnotation();

            _isAnnotationDragging = true;
            _annotationScreenStart = pos;
            StartAnnotationShape(pos);
            HideFloatingButtons();
            ViewerGrid.CaptureMouse();
            e.Handled = true;
            return;
        }

        // 涂抹模式
        if (ViewModel.IsDoodleMode)
        {
            _isDoodleDragging = true;
            _doodleScreenStart = pos;
            StartDoodleShape(pos);
            HideFloatingButtons();
            ViewerGrid.CaptureMouse();
            e.Handled = true;
            return;
        }

        // 普通模式：平移（全屏模式下禁用）
        if (!ViewModel.IsFullScreen)
        {
            _isPanning = true;
            _panStartPoint = e.GetPosition(this);
            _panStartTranslate = new Point(ImageTranslateTransform.X, ImageTranslateTransform.Y);
            ViewerGrid.CaptureMouse();
            ViewerGrid.Cursor = Cursors.Hand;
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 裁切模式
        if (_isCropDragging)
        {
            EndCropDrag(GetCropPointerPosition(e));
            return;
        }

        // 标注模式
        if (_isAnnotationDragging)
        {
            _isAnnotationDragging = false;
            ViewerGrid.ReleaseMouseCapture();
            if (_currentDrawingShape != null)
            {
                if (IsAnnotationShapeValid(_currentDrawingShape))
                {
                    RaisePendingAnnotationsChanged();
                    PushAnnotationOverlayHistory();
                }
                else
                {
                    AnnotationOverlay.Children.Remove(_currentDrawingShape);
                }

                _currentDrawingShape = null;
            }
            return;
        }

        // 标注移动结束
        if (_isMovingAnnotation)
        {
            EndMoveAnnotation();
            e.Handled = true;
            return;
        }

        // 文字缩放结束
        if (_isResizingText)
        {
            _isResizingText = false;
            AnnotationOverlay.ReleaseMouseCapture();
            if (_selectedAnnotation is TextBlock tb)
            {
                // 更新 Tag 中的 FontSize 用于保存
                if (tb.Tag is AnnotationVisualMetadata meta)
                    tb.Tag = new AnnotationVisualMetadata(meta.Tool, meta.Color, tb.FontSize);
                RaisePendingAnnotationsChanged();
                PushAnnotationOverlayHistory();
            }
            e.Handled = true;
            return;
        }

        // 涂抹模式 — 每笔画完立即应用马赛克
        if (_isDoodleDragging)
        {
            EndDoodleStroke();
            return;
        }

        // 普通模式
        _isPanning = false;
        ViewerGrid.ReleaseMouseCapture();
        ViewerGrid.Cursor = Cursors.Arrow;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = GetCropPointerPosition(e);

        // 如果 MouseLeftButtonUp 因鼠标捕获/路由没有送达，不能继续按拖拽处理；
        // 否则松开鼠标后选区会继续跟随鼠标移动。
        if (_isCropDragging && e.LeftButton != MouseButtonState.Pressed)
        {
            EndCropDrag(pos);
            return;
        }

        if (ViewModel?.IsCropMode == true)
            SetViewerCursor(GetCursorForCropDragMode(_isCropDragging ? _cropDragMode : HitTestCropSelection(pos)));

        // 裁切模式
        if (_isCropDragging)
        {
            switch (_cropDragMode)
            {
                case CropDragMode.New:
                    var ratio = ViewModel?.CropAspectRatio ?? 0;
                    _cropScreenRect = ratio > 0
                        ? CreateAspectRatioRectFromStart(_cropScreenStart, pos, ratio)
                        : CreateNormalizedRect(_cropScreenStart, pos);
                    // 拖拽中实时限制选区不超出视口，避免松手才跳回造成视觉跳变
                    // 但仅在选区已有一定大小（超过最小尺寸）时才应用，避免起始拖拽时被强制最小尺寸
                    if (_cropScreenRect.Width > CropMinSize || _cropScreenRect.Height > CropMinSize)
                    {
                        if (ratio > 0)
                            ClampAspectRatioCropToViewport(ref _cropScreenRect, ratio);
                        else
                            ClampCropToViewport(ref _cropScreenRect);
                    }
                    break;
                case CropDragMode.Move:
                    var dx = pos.X - _cropDragOriginalPos.X;
                    var dy = pos.Y - _cropDragOriginalPos.Y;
                    var orig = _cropDragOriginalRect;
                    _cropScreenRect = new Rect(orig.Left + dx, orig.Top + dy, orig.Width, orig.Height);
                    ClampCropToViewport(ref _cropScreenRect);
                    break;
                case CropDragMode.ResizeN:
                case CropDragMode.ResizeS:
                case CropDragMode.ResizeE:
                case CropDragMode.ResizeW:
                case CropDragMode.ResizeNW:
                case CropDragMode.ResizeNE:
                case CropDragMode.ResizeSE:
                case CropDragMode.ResizeSW:
                    _cropScreenRect = ResizeCropSelection(pos);
                    break;
            }
            UpdateCropOverlay();
            return;
        }
        else if (ViewModel?.IsCropMode == true)
        {
            UpdateCropCursor(pos);
        }

        // 标注模式
        if (_isAnnotationDragging)
        {
            UpdateAnnotationShape(pos);
            return;
        }

        // 标注移动
        if (_isMovingAnnotation)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndMoveAnnotation();
                return;
            }

            MoveSelectedAnnotation(pos);
            e.Handled = true;
            return;
        }

        // 文字缩放拖拽
        if (_isResizingText && _selectedAnnotation is TextBlock resizeText)
        {
            var delta = (pos.Y - _textResizeStart.Y) + (pos.X - _textResizeStart.X);
            var newFontSize = Math.Max(8, Math.Min(200, _textResizeStartFontSize + delta * 0.5));
            resizeText.FontSize = newFontSize;
            resizeText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            resizeText.Arrange(new Rect(new Point(Canvas.GetLeft(resizeText), Canvas.GetTop(resizeText)), resizeText.DesiredSize));
            UpdateTextResizeHandlePosition();
            e.Handled = true;
            return;
        }

        // 涂抹模式
        if (_isDoodleDragging)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndDoodleStroke();
                return;
            }

            UpdateDoodleShape(pos);
            return;
        }

        // 普通模式：平移
        if (!_isPanning) return;
        var cur = e.GetPosition(this);
        ImageTranslateTransform.X = _panStartTranslate.X + (cur.X - _panStartPoint.X);
        ImageTranslateTransform.Y = _panStartTranslate.Y + (cur.Y - _panStartPoint.Y);
    }

    private void EndCropDrag(Point pos)
    {
        _isCropDragging = false;
        _cropDragMode = CropDragMode.None;
        ViewerGrid.ReleaseMouseCapture();
        CropOverlay.ReleaseMouseCapture();

        var ratio = ViewModel?.CropAspectRatio ?? 0;
        if (ratio > 0)
            ClampAspectRatioCropToViewport(ref _cropScreenRect, ratio);
        else
            ClampCropToViewport(ref _cropScreenRect);

        UpdateCropOverlay();

        if (_cropScreenRect.Width > 5 && _cropScreenRect.Height > 5)
        {
            ShowFloatingButtons(_cropScreenRect);
        }
    }

    #endregion

    #region 侧边导航按钮

    private void UpdateNavVisibility()
    {
        var visibility = ViewModel?.HasImage == true && ViewModel.IsInteractiveEditMode != true
            ? Visibility.Visible
            : Visibility.Collapsed;
        LeftNavBtn.Visibility = visibility;
        RightNavBtn.Visibility = visibility;
        // 可见性变化时重置透明度，避免残留显示
        if (visibility == Visibility.Visible)
        {
            ClearNavOpacity(LeftNavBtn);
            ClearNavOpacity(RightNavBtn);
        }
    }

    private void OnLeftNavMouseEnter(object sender, MouseEventArgs e)
    {
        LeftNavBtn.BeginAnimation(System.Windows.UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1,
                TimeSpan.FromMilliseconds(150)));
    }

    private void OnLeftNavMouseLeave(object sender, MouseEventArgs e)
    {
        if (IsMouseInsideElement(LeftNavBtn))
            return;
        LeftNavBtn.BeginAnimation(System.Windows.UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0,
                TimeSpan.FromMilliseconds(200)));
    }

    private void OnRightNavMouseEnter(object sender, MouseEventArgs e)
    {
        RightNavBtn.BeginAnimation(System.Windows.UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1,
                TimeSpan.FromMilliseconds(150)));
    }

    private void OnRightNavMouseLeave(object sender, MouseEventArgs e)
    {
        if (IsMouseInsideElement(RightNavBtn))
            return;
        RightNavBtn.BeginAnimation(System.Windows.UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0,
                TimeSpan.FromMilliseconds(200)));
    }

    private void OnRootGridMouseLeave(object sender, MouseEventArgs e)
    {
        ClearNavOpacity(LeftNavBtn);
        ClearNavOpacity(RightNavBtn);
    }

    private static void ClearNavOpacity(UIElement element)
    {
        element.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
        element.Opacity = 0;
    }

    private static bool IsMouseInsideElement(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        var point = Mouse.GetPosition(element);
        return point.X >= 0 && point.Y >= 0 && point.X <= element.ActualWidth && point.Y <= element.ActualHeight;
    }

    private void OnPrevImageClick(object sender, MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this)?.WindowState == WindowState.Maximized)
        {
            if (!TryBeginMaximizedNavClick())
            {
                e.Handled = true;
                return;
            }

            if (ViewModel?.CanGoPrevious == true)
                ViewModel.GoPrevious();

            _navEdgeDragState = NavEdgeDragState.None;
            e.Handled = true;
            return;
        }

        _navEdgeDragState = NavEdgeDragState.Left;
        _navEdgeDragStart = e.GetPosition(this);
        LeftNavBtn.CaptureMouse();
        e.Handled = true;
    }

    private void OnNextImageClick(object sender, MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this)?.WindowState == WindowState.Maximized)
        {
            if (!TryBeginMaximizedNavClick())
            {
                e.Handled = true;
                return;
            }

            if (ViewModel?.CanGoNext == true)
                ViewModel.GoNext();

            _navEdgeDragState = NavEdgeDragState.None;
            e.Handled = true;
            return;
        }

        _navEdgeDragState = NavEdgeDragState.Right;
        _navEdgeDragStart = e.GetPosition(this);
        RightNavBtn.CaptureMouse();
        e.Handled = true;
    }

    private bool TryBeginMaximizedNavClick()
    {
        var tick = Environment.TickCount;
        if (unchecked(tick - _lastMaximizedNavClickTick) < 120)
            return false;

        _lastMaximizedNavClickTick = tick;
        return true;
    }

    private void OnNavResizeMouseMove(object sender, MouseEventArgs e)
    {
        if (_navEdgeDragState == NavEdgeDragState.None || e.LeftButton != MouseButtonState.Pressed)
            return;

        if (Window.GetWindow(this)?.WindowState == WindowState.Maximized)
            return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _navEdgeDragStart.X) < NavResizeDragThreshold)
            return;

        var edge = _navEdgeDragState;
        _navEdgeDragState = NavEdgeDragState.None;
        LeftNavBtn.ReleaseMouseCapture();
        RightNavBtn.ReleaseMouseCapture();
        BeginEdgeResize(edge, current);
        e.Handled = true;
    }

    private void OnNavResizeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_navEdgeDragState == NavEdgeDragState.None)
            return;

        var edge = _navEdgeDragState;
        _navEdgeDragState = NavEdgeDragState.None;
        LeftNavBtn.ReleaseMouseCapture();
        RightNavBtn.ReleaseMouseCapture();

        if (edge == NavEdgeDragState.Left)
        {
            if (ViewModel?.CanGoPrevious == true)
                ViewModel.GoPrevious();
        }
        else if (edge == NavEdgeDragState.Right)
        {
            if (ViewModel?.CanGoNext == true)
                ViewModel.GoNext();
        }

        e.Handled = true;
    }

    public bool TryNavigateFromChromeEdgeClick(Point screenPoint)
    {
        if (ViewModel?.HasImage != true || ViewModel.IsInteractiveEditMode)
            return false;

        if (IsScreenPointInsideSideNavHitArea(LeftNavBtn, screenPoint, isLeft: true))
        {
            if (ViewModel.CanGoPrevious)
                ViewModel.GoPrevious();

            return true;
        }

        if (IsScreenPointInsideSideNavHitArea(RightNavBtn, screenPoint, isLeft: false))
        {
            if (ViewModel.CanGoNext)
                ViewModel.GoNext();

            return true;
        }

        return false;
    }

    private bool IsScreenPointInsideSideNavHitArea(FrameworkElement button, Point screenPoint, bool isLeft)
    {
        if (!button.IsVisible || button.ActualWidth <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
            return false;

        var point = PointFromScreen(screenPoint);
        if (point.Y < 0 || point.Y > ActualHeight)
            return false;

        var margin = button.Margin;
        var hitWidth = button.ActualWidth + (isLeft ? margin.Left : margin.Right);
        return isLeft
            ? point.X >= 0 && point.X <= hitWidth
            : point.X <= ActualWidth && point.X >= ActualWidth - hitWidth;
    }

    private static bool IsScreenPointInsideElement(FrameworkElement element, Point screenPoint)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        var point = element.PointFromScreen(screenPoint);
        return point.X >= 0 && point.Y >= 0 && point.X <= element.ActualWidth && point.Y <= element.ActualHeight;
    }

    private void BeginEdgeResize(NavEdgeDragState edge, Point current)
    {
        if (Window.GetWindow(this) is not Window window)
            return;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero)
            return;

        var screenPoint = PointToScreen(current);
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, edge == NavEdgeDragState.Left ? HTLEFT : HTRIGHT, MakeLParam(screenPoint.X, screenPoint.Y));
    }

    private void OnPlaceholderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.HasImage != true)
        {
            ViewModel?.OpenFileCommand.Execute(null);
            e.Handled = true;
        }
    }

    #endregion

    #region 浮动按钮

    private void ShowFloatingButtons(Rect screenRect)
    {
        SetFloatingButtonsMode(isAnnotationSelection: false);

        FloatingButtonsCanvas.Width = ViewerGrid.ActualWidth;
        FloatingButtonsCanvas.Height = ViewerGrid.ActualHeight;

        // 定位到选区右侧
        var x = screenRect.Right + 8;
        var y = screenRect.Top + (screenRect.Height - 28) / 2.0;

        // 确保不超出视口
        x = Math.Min(x, ViewerGrid.ActualWidth - 70);
        y = Math.Max(5, Math.Min(y, ViewerGrid.ActualHeight - 35));

        Canvas.SetLeft(FloatingButtons, x);
        Canvas.SetTop(FloatingButtons, y);
        FloatingButtonsCanvas.Visibility = Visibility.Visible;
    }

    private void ShowFloatingButtonsAroundShape(FrameworkElement shape)
    {
        SetFloatingButtonsMode(isAnnotationSelection: true);

        Rect bounds;
        if (shape is Rectangle rect)
        {
            bounds = new Rect(Canvas.GetLeft(rect), Canvas.GetTop(rect), rect.Width, rect.Height);
        }
        else if (shape is Line line)
        {
            var x = Math.Min(line.X1, line.X2);
            var y = Math.Min(line.Y1, line.Y2);
            bounds = new Rect(x, y, Math.Abs(line.X2 - line.X1), Math.Abs(line.Y2 - line.Y1));
        }
        else if (shape is Path path && path.Data is PathGeometry geo && geo.Figures.Count > 0)
        {
            var boundsRect = geo.Bounds;
            bounds = boundsRect;
        }
        else if (shape is Polyline polyline)
        {
            if (polyline.Points.Count == 0) return;
            var minX = polyline.Points.Min(p => p.X);
            var minY = polyline.Points.Min(p => p.Y);
            var maxX = polyline.Points.Max(p => p.X);
            var maxY = polyline.Points.Max(p => p.Y);
            bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
        }
        else if (shape is TextBlock textBlock)
        {
            var left = Canvas.GetLeft(textBlock);
            var top = Canvas.GetTop(textBlock);
            bounds = new Rect(left, top, textBlock.ActualWidth, textBlock.ActualHeight);
        }
        else
        {
            return;
        }

        var x2 = bounds.Right + 8;
        var y2 = bounds.Top + (bounds.Height - 28) / 2.0;
        x2 = Math.Min(x2, ViewerGrid.ActualWidth - 70);
        y2 = Math.Max(5, Math.Min(y2, ViewerGrid.ActualHeight - 35));

        Canvas.SetLeft(FloatingButtons, x2);
        Canvas.SetTop(FloatingButtons, y2);
        FloatingButtonsCanvas.Visibility = Visibility.Visible;
    }

    private void HideFloatingButtons()
    {
        FloatingButtonsCanvas.Visibility = Visibility.Collapsed;
    }

    private void SetFloatingButtonsMode(bool isAnnotationSelection)
    {
        FloatingConfirmButton.Visibility = isAnnotationSelection ? Visibility.Collapsed : Visibility.Visible;
        FloatingCancelButton.Visibility  = isAnnotationSelection ? Visibility.Collapsed : Visibility.Visible;
        // 图钉按钮：仅裁切模式（非标注选中）时显示
        FloatingPinButton.Visibility     = isAnnotationSelection ? Visibility.Collapsed : Visibility.Visible;
        FloatingDeleteButton.Visibility  = isAnnotationSelection ? Visibility.Visible   : Visibility.Collapsed;
    }

    private async void OnFloatingConfirmClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        HideFloatingButtons();
        if (ViewModel?.IsCropMode == true)
            await ConfirmCropAsync();
    }

    private void OnFloatingDeleteClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        DeleteSelectedAnnotation();
    }

    private void OnFloatingPinClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        // 如果在裁切模式，截取裁切框区域后固定
        if (ViewModel?.IsCropMode == true && _cropScreenRect.Width > 5 && _cropScreenRect.Height > 5)
        {
            PinCroppedAreaAsync();
        }
        else
        {
            // 否则复用右键菜单的"固定到桌面"逻辑（固定完整图片）
            ContextMenu_PinToDesktop_Click(sender, e);
        }
    }

    private async void PinCroppedAreaAsync()
    {
        if (ViewModel?.CurrentImage == null) return;

        try
        {
            // 将裁切框屏幕坐标转换为图像坐标
            var imgStart = ScreenToImageCoords(_cropScreenRect.TopLeft);
            var imgEnd   = ScreenToImageCoords(_cropScreenRect.BottomRight);

            int imgX = (int)Math.Max(0, Math.Min(imgStart.X, imgEnd.X));
            int imgY = (int)Math.Max(0, Math.Min(imgStart.Y, imgEnd.Y));
            int imgW = (int)Math.Abs(imgEnd.X - imgStart.X);
            int imgH = (int)Math.Abs(imgEnd.Y - imgStart.Y);

            // 确保不超出图像边界
            imgW = (int)Math.Min(imgW, ViewModel.CurrentImage.PixelWidth  - imgX);
            imgH = (int)Math.Min(imgH, ViewModel.CurrentImage.PixelHeight - imgY);

            if (imgW < 5 || imgH < 5)
            {
                MessageBox.Show("裁切区域过小。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 用 CroppedBitmap 直接截取，不改变 ViewModel 状态
            var croppedBitmap = new CroppedBitmap(
                ViewModel.CurrentImage,
                new System.Windows.Int32Rect(imgX, imgY, imgW, imgH));
            croppedBitmap.Freeze();

            // 保存为临时 PNG 并启动浮窗
            string tempFilePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"lvi_crop_{Guid.NewGuid()}.png");
            SaveBitmapSourceToFile(croppedBitmap, tempFilePath);

            string floatingViewerExePath = GetFloatingViewerExePath();

            if (File.Exists(floatingViewerExePath))
            {
                Process.Start(floatingViewerExePath, $"\"{tempFilePath}\"");
            }
            else
            {
                MessageBox.Show(
                    $"图片浮窗应用未找到：{floatingViewerExePath}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"固定裁切区域失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        await Task.CompletedTask; // 保持 async 签名
    }

    private void OnFloatingCancelClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        HideFloatingButtons();
        if (ViewModel?.IsCropMode == true)
        {
            ViewModel.CancelCropCommand.Execute(null);
        }
        else if (ViewModel?.IsAnnotationMode == true)
        {
            DeleteSelectedAnnotation();
        }
    }

    #endregion

    #region Crop Hit Testing

    private CropDragMode HitTestCropSelection(Point pos)
    {
        if (_cropScreenRect.Width < CropMinSize || _cropScreenRect.Height < CropMinSize)
            return CropDragMode.None;

        var r = _cropScreenRect;
        double t = CropEdgeThreshold;

        bool nearLeft = Math.Abs(pos.X - r.Left) < t;
        bool nearRight = Math.Abs(pos.X - r.Right) < t;
        bool nearTop = Math.Abs(pos.Y - r.Top) < t;
        bool nearBottom = Math.Abs(pos.Y - r.Bottom) < t;

        bool insideX = pos.X >= r.Left && pos.X <= r.Right;
        bool insideY = pos.Y >= r.Top && pos.Y <= r.Bottom;

        if (nearTop && nearLeft) return CropDragMode.ResizeNW;
        if (nearTop && nearRight) return CropDragMode.ResizeNE;
        if (nearBottom && nearLeft) return CropDragMode.ResizeSW;
        if (nearBottom && nearRight) return CropDragMode.ResizeSE;
        if (nearTop && insideX) return CropDragMode.ResizeN;
        if (nearBottom && insideX) return CropDragMode.ResizeS;
        if (nearLeft && insideY) return CropDragMode.ResizeW;
        if (nearRight && insideY) return CropDragMode.ResizeE;
        if (insideX && insideY) return CropDragMode.Move;
        return CropDragMode.None;
    }

    private Cursor GetCursorForCropDragMode(CropDragMode mode)
    {
        return mode switch
        {
            CropDragMode.Move => Cursors.SizeAll,
            CropDragMode.ResizeN => Cursors.SizeNS,
            CropDragMode.ResizeS => Cursors.SizeNS,
            CropDragMode.ResizeE => Cursors.SizeWE,
            CropDragMode.ResizeW => Cursors.SizeWE,
            CropDragMode.ResizeNW => Cursors.SizeNWSE,
            CropDragMode.ResizeSE => Cursors.SizeNWSE,
            CropDragMode.ResizeNE => Cursors.SizeNESW,
            CropDragMode.ResizeSW => Cursors.SizeNESW,
            _ => Cursors.Cross
        };
    }

    private Point GetCropPointerPosition(MouseEventArgs e)
    {
        // WPF 鼠标捕获到 Canvas/子元素后，e.GetPosition(ViewerGrid) 在某些路由场景下会退化为按下点，
        // 改为用全局屏幕坐标再映射回 ViewerGrid，可避免 Move 期间坐标不变导致选区一直 0x0。
        var screen = PointToScreen(Mouse.GetPosition(this));
        return ViewerGrid.PointFromScreen(screen);
    }

    private static Rect CreateNormalizedRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var w = Math.Abs(b.X - a.X);
        var h = Math.Abs(b.Y - a.Y);
        return new Rect(x, y, w, h);
    }

    private static Rect CreateAspectRatioRectFromStart(Point start, Point current, double ratio)
    {
        var dx = current.X - start.X;
        var dy = current.Y - start.Y;
        var absDx = Math.Abs(dx);
        var absDy = Math.Abs(dy);

        if (absDx < 0.1 && absDy < 0.1)
            return new Rect(start, start);

        double width;
        double height;

        if (absDx / Math.Max(absDy, 0.1) > ratio)
        {
            width = absDx;
            height = width / ratio;
        }
        else
        {
            height = absDy;
            width = height * ratio;
        }

        var left = dx >= 0 ? start.X : start.X - width;
        var top = dy >= 0 ? start.Y : start.Y - height;
        return new Rect(left, top, width, height);
    }

    private void UpdateCropCursor(Point pos)
    {
        if (_cropDragMode != CropDragMode.None) return;
        var mode = HitTestCropSelection(pos);
        Cursor = GetCursorForCropDragMode(mode);
    }

    /// <summary>
    /// 返回图片当前实际显示区域（ViewerGrid 坐标系）。
    /// 裁切选框不能超出此区域，否则会跑到图片外面。
    /// </summary>
    private Rect GetImageScreenRect()
    {
        var vw = ViewerGrid.ActualWidth;
        var vh = ViewerGrid.ActualHeight;
        if (ViewModel?.CurrentImage == null)
            return new Rect(0, 0, vw, vh);

        var iw = ViewModel.CurrentImage.PixelWidth;
        var ih = ViewModel.CurrentImage.PixelHeight;
        var uniformScale = Math.Min(vw / iw, vh / ih);
        var renderedW = iw * uniformScale * ImageScaleTransform.ScaleX;
        var renderedH = ih * uniformScale * ImageScaleTransform.ScaleY;
        var layoutX = (vw - iw * uniformScale) / 2.0;
        var layoutY = (vh - ih * uniformScale) / 2.0;
        var left = layoutX + ImageTranslateTransform.X;
        var top  = layoutY + ImageTranslateTransform.Y;
        return new Rect(left, top, renderedW, renderedH);
    }

    private void ClampCropToViewport(ref Rect rect)
    {
        var bounds = GetImageScreenRect();
        var w = Math.Min(rect.Width,  bounds.Width);
        var h = Math.Min(rect.Height, bounds.Height);
        var x = Math.Max(bounds.Left, Math.Min(rect.Left, bounds.Right  - w));
        var y = Math.Max(bounds.Top,  Math.Min(rect.Top,  bounds.Bottom - h));
        rect = new Rect(x, y, w, h);
    }

    private Rect ResizeCropSelection(Point pos)
    {
        var ratio = ViewModel?.CropAspectRatio ?? 0;
        if (ratio > 0)
            return ResizeCropSelectionWithAspectRatio(pos, ratio);

        var r = _cropDragOriginalRect;
        var startPos = _cropDragOriginalPos;
        var dx = pos.X - startPos.X;
        var dy = pos.Y - startPos.Y;
        double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
        switch (_cropDragMode)
        {
            case CropDragMode.ResizeN: top = Math.Min(r.Top + dy, r.Bottom - CropMinSize); break;
            case CropDragMode.ResizeS: bottom = Math.Max(r.Bottom + dy, r.Top + CropMinSize); break;
            case CropDragMode.ResizeE: right = Math.Max(r.Right + dx, r.Left + CropMinSize); break;
            case CropDragMode.ResizeW: left = Math.Min(r.Left + dx, r.Right - CropMinSize); break;
            case CropDragMode.ResizeNW: left = Math.Min(r.Left + dx, r.Right - CropMinSize); top = Math.Min(r.Top + dy, r.Bottom - CropMinSize); break;
            case CropDragMode.ResizeNE: right = Math.Max(r.Right + dx, r.Left + CropMinSize); top = Math.Min(r.Top + dy, r.Bottom - CropMinSize); break;
            case CropDragMode.ResizeSW: left = Math.Min(r.Left + dx, r.Right - CropMinSize); bottom = Math.Max(r.Bottom + dy, r.Top + CropMinSize); break;
            case CropDragMode.ResizeSE: right = Math.Max(r.Right + dx, r.Left + CropMinSize); bottom = Math.Max(r.Bottom + dy, r.Top + CropMinSize); break;
        }
        var result = new Rect(left, top, right - left, bottom - top);
        return result;
    }

    private Rect ResizeCropSelectionWithAspectRatio(Point pos, double ratio)
    {
        var r = _cropDragOriginalRect;
        var startPos = _cropDragOriginalPos;
        var dx = pos.X - startPos.X;
        var dy = pos.Y - startPos.Y;
        Rect result;

        switch (_cropDragMode)
        {
            case CropDragMode.ResizeNW:
                result = CreateAspectRatioRectFromStart(r.BottomRight, pos, ratio);
                break;
            case CropDragMode.ResizeNE:
                result = CreateAspectRatioRectFromStart(new Point(r.Left, r.Bottom), pos, ratio);
                break;
            case CropDragMode.ResizeSW:
                result = CreateAspectRatioRectFromStart(new Point(r.Right, r.Top), pos, ratio);
                break;
            case CropDragMode.ResizeSE:
                result = CreateAspectRatioRectFromStart(r.TopLeft, pos, ratio);
                break;
            case CropDragMode.ResizeE:
            {
                var width = Math.Max(CropMinSize, r.Width + dx);
                var height = Math.Max(CropMinSize, width / ratio);
                result = new Rect(r.Left, r.Top + (r.Height - height) / 2.0, width, height);
                break;
            }
            case CropDragMode.ResizeW:
            {
                var width = Math.Max(CropMinSize, r.Width - dx);
                var height = Math.Max(CropMinSize, width / ratio);
                result = new Rect(r.Right - width, r.Top + (r.Height - height) / 2.0, width, height);
                break;
            }
            case CropDragMode.ResizeN:
            {
                var height = Math.Max(CropMinSize, r.Height - dy);
                var width = Math.Max(CropMinSize, height * ratio);
                result = new Rect(r.Left + (r.Width - width) / 2.0, r.Bottom - height, width, height);
                break;
            }
            case CropDragMode.ResizeS:
            {
                var height = Math.Max(CropMinSize, r.Height + dy);
                var width = Math.Max(CropMinSize, height * ratio);
                result = new Rect(r.Left + (r.Width - width) / 2.0, r.Top, width, height);
                break;
            }
            default:
                result = r;
                break;
        }

        ClampAspectRatioCropToViewport(ref result, ratio);
        return result;
    }

    private void ClampAspectRatioCropToViewport(ref Rect rect, double ratio)
    {
        var bounds = GetImageScreenRect();
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var w = Math.Max(CropMinSize, rect.Width);
        var h = Math.Max(CropMinSize, rect.Height);

        if (w > bounds.Width)
        {
            w = bounds.Width;
            h = w / ratio;
        }
        if (h > bounds.Height)
        {
            h = bounds.Height;
            w = h * ratio;
        }

        var x = Math.Max(bounds.Left, Math.Min(rect.Left, bounds.Right  - w));
        var y = Math.Max(bounds.Top,  Math.Min(rect.Top,  bounds.Bottom - h));
        rect = new Rect(x, y, w, h);
    }

    #endregion

    #region 裁切覆盖层

    private void UpdateCropOverlay() => UpdateCropOverlay(_cropScreenRect);

    private void UpdateCropOverlay(Rect sel)
    {
        if (ViewModel == null || !ViewModel.IsCropMode) return;

        var vr = new Rect(0, 0, ViewerGrid.ActualWidth, ViewerGrid.ActualHeight);

        CropOverlay.Width = ViewerGrid.ActualWidth;
        CropOverlay.Height = ViewerGrid.ActualHeight;

        if (sel.Width < 2 || sel.Height < 2)
        {
            HideCropSelectionVisuals();
            return;
        }

        sel = Rect.Intersect(sel, vr);
        if (sel.Width < 2 || sel.Height < 2) { HideCropSelectionVisuals(); return; }

        CropMaskTop.Data = new RectangleGeometry(new Rect(0, 0, vr.Width, sel.Top));
        CropMaskBottom.Data = new RectangleGeometry(new Rect(0, sel.Bottom, vr.Width, vr.Height - sel.Bottom));
        CropMaskLeft.Data = new RectangleGeometry(new Rect(0, sel.Top, sel.Left, sel.Height));
        CropMaskRight.Data = new RectangleGeometry(new Rect(sel.Right, sel.Top, vr.Width - sel.Right, sel.Height));

        Canvas.SetLeft(CropBorder, sel.Left);
        Canvas.SetTop(CropBorder, sel.Top);
        CropBorder.Width = sel.Width;
        CropBorder.Height = sel.Height;
        CropBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        CropBorder.Visibility = Visibility.Visible;

        Canvas.SetLeft(CropSelectionRect, sel.Left);
        Canvas.SetTop(CropSelectionRect, sel.Top);
        CropSelectionRect.Width = sel.Width;
        CropSelectionRect.Height = sel.Height;
        CropSelectionRect.RenderTransformOrigin = new Point(0.5, 0.5);
        CropSelectionRect.Visibility = Visibility.Visible;

        var cx = sel.Left;
        var cy = sel.Top;
        Canvas.SetLeft(CornerTL, cx); Canvas.SetTop(CornerTL, cy);
        Canvas.SetLeft(CornerTR, cx + sel.Width); Canvas.SetTop(CornerTR, cy);
        Canvas.SetLeft(CornerBL, cx); Canvas.SetTop(CornerBL, cy + sel.Height);
        Canvas.SetLeft(CornerBR, cx + sel.Width); Canvas.SetTop(CornerBR, cy + sel.Height);
        CornerTL.Visibility = Visibility.Visible;
        CornerTR.Visibility = Visibility.Visible;
        CornerBL.Visibility = Visibility.Visible;
        CornerBR.Visibility = Visibility.Visible;
    }


    private void ClearCropSelection()
    {
        _cropScreenRect = default;
        _cropDragMode = CropDragMode.None;
        HideCropSelectionVisuals();
    }

    private void HideCropSelectionVisuals()
    {
        CropMaskTop.Data = null;
        CropMaskBottom.Data = null;
        CropMaskLeft.Data = null;
        CropMaskRight.Data = null;
        CropBorder.Visibility = Visibility.Collapsed;
        CropSelectionRect.Visibility = Visibility.Collapsed;
        CornerTL.Visibility = Visibility.Collapsed;
        CornerTR.Visibility = Visibility.Collapsed;
        CornerBL.Visibility = Visibility.Collapsed;
        CornerBR.Visibility = Visibility.Collapsed;
    }

    public void BeginCropModeFromToolbar()
    {
        if (ViewModel == null) return;

        ViewModel.StartCrop();
        // 直接同步一次 View 状态：即使 PropertyChanged 订阅链路异常，也能看到十字光标和裁切工具条。
        OnCropModeChanged();
        Focus();
    }

    private void SetViewerCursor(Cursor cursor)
    {
        Cursor = cursor;
        ViewerGrid.Cursor = cursor;
        MainImage.Cursor = cursor;
        GifImage.Cursor = cursor;
    }

    public async Task ConfirmCropAsync()
    {
        if (ViewModel == null || !ViewModel.IsCropMode) return;
        if (_cropScreenRect.Width < 2 || _cropScreenRect.Height < 2) return;

        var imgStart = ScreenToImageCoords(_cropScreenRect.TopLeft);
        var imgEnd = ScreenToImageCoords(_cropScreenRect.BottomRight);

        var imgX = Math.Max(0, Math.Min(imgStart.X, imgEnd.X));
        var imgY = Math.Max(0, Math.Min(imgStart.Y, imgEnd.Y));
        var imgW = Math.Abs(imgEnd.X - imgStart.X);
        var imgH = Math.Abs(imgEnd.Y - imgStart.Y);

        if (ViewModel.CurrentImage != null)
        {
            var maxW = Math.Max(0, ViewModel.CurrentImage.PixelWidth - imgX);
            var maxH = Math.Max(0, ViewModel.CurrentImage.PixelHeight - imgY);
            imgW = Math.Min(imgW, maxW);
            imgH = Math.Min(imgH, maxH);
        }

        if (imgW > 5 && imgH > 5)
        {
            await ViewModel.ExecuteCropAsync(imgX, imgY, imgW, imgH);
        }
    }

    #endregion

    #region 涂抹

    public void BeginDoodleModeFromToolbar()
    {
        if (ViewModel == null) return;
        ViewModel.StartDoodle();
        OnDoodleModeChanged();
        Focus();
    }

    private void StartDoodleShape(Point screenPos)
    {
        var thickness = ViewModel?.DoodleThickness ?? 30;

        // 半透明橙色线条，直观显示涂抹范围
        var polyline = new Polyline
        {
            Stroke = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(100, 255, 160, 0)),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        polyline.Points.Add(screenPos);
        _currentDoodlePolyline = polyline;
        DoodleOverlay.Children.Add(polyline);
    }

    private void UpdateDoodleShape(Point screenPos)
    {
        if (_currentDoodlePolyline == null) return;
        _currentDoodlePolyline.Points.Add(screenPos);
    }

    private void ClearDoodleOverlay()
    {
        DoodleOverlay.Children.Clear();
        _doodlePolylines.Clear();
        _currentDoodlePolyline = null;
        _isDoodleDragging = false;
        ViewerGrid.ReleaseMouseCapture();
        DoodleOverlay.ReleaseMouseCapture();
    }

    private void EndDoodleStroke()
    {
        _isDoodleDragging = false;
        ViewerGrid.ReleaseMouseCapture();
        DoodleOverlay.ReleaseMouseCapture();

        var polyline = _currentDoodlePolyline;
        _currentDoodlePolyline = null;
        if (polyline == null) return;

        // 先从覆盖层移除预览线，避免图片烘焙完成前残留一个圆形笔刷/线帽。
        DoodleOverlay.Children.Remove(polyline);

        if (polyline.Points.Count > 1)
        {
            _ = ApplyDoodleStrokeAsync(polyline);
        }
    }

    /// <summary>
    /// 每笔画完立即应用马赛克到图片（实时预览）
    /// </summary>
    private async Task ApplyDoodleStrokeAsync(Polyline polyline)
    {
        if (ViewModel == null || !ViewModel.IsDoodleMode || polyline.Points.Count < 2) return;

        try
        {
            // 转换为图片坐标
            var imgPoints = polyline.Points.Select(p => ScreenToImageCoords(p)).ToList();
            var scale = GetScreenToImageScale();
            var screenRadius = ViewModel.DoodleThickness;
            var imgRadius = Math.Max(1, (int)(screenRadius / scale));

            // 调用 ViewModel 应用单笔马赛克
            await ViewModel.ApplySingleDoodleStrokeAsync(imgPoints, imgRadius);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"涂抹失败: {ex.Message}";
        }
    }

    public async Task ConfirmDoodleAsync()
    {
        if (ViewModel == null || !ViewModel.IsDoodleMode) return;
        // 每笔已立即应用，这里只需退出模式
        ClearDoodleOverlay();
        ViewModel.IsDoodleMode = false;
        ViewModel.StatusMessage = "就绪";
        await Task.CompletedTask;
    }

    private void OnDoodleOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Handled via Preview events routed through ViewerGrid
    }

    private void OnDoodleOverlayMouseMove(object sender, MouseEventArgs e)
    {
        // Handled via Preview events routed through ViewerGrid
    }

    private void OnDoodleOverlayMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Handled via Preview events routed through ViewerGrid
    }

    #endregion

    #region 标注

    private void StartAnnotationShape(Point screenPos)
    {
        if (ViewModel == null) return;

        var tool = ViewModel.CurrentAnnotationTool;
        var color = ViewModel.AnnotationColor;

        // 文字工具：点击位置直接出现输入框
        if (tool == AnnotationTool.Text)
        {
            ShowInlineTextInput(screenPos, color);
            return;
        }

        Shape shape = tool switch
        {
            AnnotationTool.Rectangle => new Rectangle
            {
                Stroke = ParseBrush(color),
                StrokeThickness = 3,
                Fill = Brushes.Transparent
            },
            AnnotationTool.Ellipse => new System.Windows.Shapes.Ellipse
            {
                Stroke = ParseBrush(color),
                StrokeThickness = 3,
                Fill = Brushes.Transparent
            },
            AnnotationTool.Line => new Line
            {
                Stroke = ParseBrush(color),
                StrokeThickness = 3
            },
            AnnotationTool.Arrow => new Path
            {
                Stroke = ParseBrush(color),
                StrokeThickness = 3,
                Fill = ParseBrush(color)
            },
            AnnotationTool.Brush => new Polyline
            {
                Stroke = ParseBrush(color),
                StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            },
            _ => new Rectangle
            {
                Stroke = ParseBrush(color),
                StrokeThickness = 3,
                Fill = Brushes.Transparent
            }
        };

        _currentDrawingShape = shape;
        shape.Tag = new AnnotationVisualMetadata(tool, color, 3);
        AnnotationOverlay.Children.Add(shape);

        if (shape is Rectangle or System.Windows.Shapes.Ellipse)
        {
            Canvas.SetLeft(shape, screenPos.X);
            Canvas.SetTop(shape, screenPos.Y);
        }

        if (shape is Line line)
        {
            line.X1 = screenPos.X;
            line.Y1 = screenPos.Y;
            line.X2 = screenPos.X;
            line.Y2 = screenPos.Y;
        }
        else if (shape is Polyline polyline)
        {
            polyline.Points.Add(screenPos);
        }
    }

    private void UpdateAnnotationShape(Point screenPos)
    {
        if (_currentDrawingShape == null || ViewModel == null) return;

        var start = _annotationScreenStart;

        if (_currentDrawingShape is System.Windows.Shapes.Ellipse ellipse)
        {
            var x = Math.Min(start.X, screenPos.X);
            var y = Math.Min(start.Y, screenPos.Y);
            var w = Math.Abs(screenPos.X - start.X);
            var h = Math.Abs(screenPos.Y - start.Y);

            Canvas.SetLeft(ellipse, x);
            Canvas.SetTop(ellipse, y);
            ellipse.Width = w;
            ellipse.Height = h;
        }
        else if (_currentDrawingShape is Rectangle rect)
        {
            var x = Math.Min(start.X, screenPos.X);
            var y = Math.Min(start.Y, screenPos.Y);
            var w = Math.Abs(screenPos.X - start.X);
            var h = Math.Abs(screenPos.Y - start.Y);

            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            rect.Width = w;
            rect.Height = h;
        }
        else if (_currentDrawingShape is Line line)
        {
            line.X2 = screenPos.X;
            line.Y2 = screenPos.Y;
        }
        else if (_currentDrawingShape is Path path)
        {
            UpdateArrowPath(path, start, screenPos);
        }
        else if (_currentDrawingShape is Polyline polyline)
        {
            if (polyline.Points.Count == 0 || Distance(polyline.Points[^1], screenPos) >= 1.5)
                polyline.Points.Add(screenPos);
        }
    }

    private static bool IsAnnotationShapeValid(FrameworkElement shape)
    {
        return shape switch
        {
            Rectangle rect => rect.Width > 2 && rect.Height > 2,
            System.Windows.Shapes.Ellipse ellipse => ellipse.Width > 2 && ellipse.Height > 2,
            Line line => Math.Abs(line.X2 - line.X1) > 2 || Math.Abs(line.Y2 - line.Y1) > 2,
            Polyline polyline => polyline.Points.Count > 1,
            TextBlock tb => !string.IsNullOrEmpty(tb.Text),
            Path path when path.Data is PathGeometry geo && geo.Figures.Count > 0 =>
                geo.Figures[0].Segments.Count > 0,
            _ => false
        };
    }

    private List<AnnotationShape> CollectPendingAnnotations()
    {
        DeselectAnnotation();

        var shapes = new List<AnnotationShape>();
        foreach (var child in AnnotationOverlay.Children)
        {
            if (child is not FrameworkElement fe) continue;
            if (ReferenceEquals(fe, _textResizeHandle) || fe.Tag as string == "ResizeHandle") continue;

            var meta = fe.Tag as AnnotationVisualMetadata;
            var color = meta?.Color ?? ViewModel?.AnnotationColor ?? "#FFE53935";
            var strokeThickness = meta?.StrokeThickness ?? (fe is Shape s ? s.StrokeThickness : 3);
            var imageStrokeThickness = ScreenLengthToImageLength(strokeThickness);

            AnnotationShape? annShape = null;

            if (fe is System.Windows.Shapes.Ellipse ellipse && ellipse.Width > 2 && ellipse.Height > 2)
            {
                var topLeft = ScreenToImageCoords(new Point(Canvas.GetLeft(ellipse), Canvas.GetTop(ellipse)));
                var bottomRight = ScreenToImageCoords(new Point(Canvas.GetLeft(ellipse) + ellipse.Width, Canvas.GetTop(ellipse) + ellipse.Height));
                annShape = new AnnotationShape
                {
                    Tool = AnnotationTool.Ellipse,
                    Start = topLeft,
                    End = bottomRight,
                    Color = color,
                    StrokeThickness = imageStrokeThickness
                };
            }
            else if (fe is Rectangle rect && rect.Width > 2 && rect.Height > 2)
            {
                var topLeft = ScreenToImageCoords(new Point(Canvas.GetLeft(rect), Canvas.GetTop(rect)));
                var bottomRight = ScreenToImageCoords(new Point(Canvas.GetLeft(rect) + rect.Width, Canvas.GetTop(rect) + rect.Height));
                annShape = new AnnotationShape
                {
                    Tool = AnnotationTool.Rectangle,
                    Start = topLeft,
                    End = bottomRight,
                    Color = color,
                    StrokeThickness = imageStrokeThickness
                };
            }
            else if (fe is Line line && (Math.Abs(line.X2 - line.X1) > 2 || Math.Abs(line.Y2 - line.Y1) > 2))
            {
                var imgStart = ScreenToImageCoords(new Point(line.X1, line.Y1));
                var imgEnd = ScreenToImageCoords(new Point(line.X2, line.Y2));
                annShape = new AnnotationShape
                {
                    Tool = AnnotationTool.Line,
                    Start = imgStart,
                    End = imgEnd,
                    Color = color,
                    StrokeThickness = imageStrokeThickness
                };
            }
            else if (fe is Polyline polyline && polyline.Points.Count > 1)
            {
                var imgPoints = polyline.Points.Select(p => ScreenToImageCoords(p)).ToList();
                annShape = new AnnotationShape
                {
                    Tool = AnnotationTool.Brush,
                    Start = imgPoints[0],
                    End = imgPoints[^1],
                    Color = color,
                    StrokeThickness = imageStrokeThickness,
                    Points = imgPoints
                };
            }
            else if (fe is TextBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
            {
                var imgPos = ScreenToImageCoords(new Point(Canvas.GetLeft(textBlock), Canvas.GetTop(textBlock)));
                annShape = new AnnotationShape
                {
                    Tool = AnnotationTool.Text,
                    Start = imgPos,
                    End = imgPos,
                    Color = color,
                    StrokeThickness = imageStrokeThickness,
                    Text = textBlock.Text,
                    FontSize = ScreenLengthToImageLength(textBlock.FontSize)
                };
            }
            else if (fe is Path path && path.Data is PathGeometry geo && geo.Figures.Count >= 1)
            {
                var fig = geo.Figures[0];
                if (fig.StartPoint != default && fig.Segments.Count > 0 &&
                    fig.Segments[0] is LineSegment lineSeg)
                {
                    var imgStart = ScreenToImageCoords(fig.StartPoint);
                    var imgEnd = ScreenToImageCoords(lineSeg.Point);
                    annShape = new AnnotationShape
                    {
                        Tool = AnnotationTool.Arrow,
                        Start = imgStart,
                        End = imgEnd,
                        Color = color,
                        StrokeThickness = imageStrokeThickness
                    };
                }
            }

            if (annShape != null) shapes.Add(annShape);
        }

        return shapes;
    }

    private void RaisePendingAnnotationsChanged()
    {
        PendingAnnotationsChanged?.Invoke(this, CollectPendingAnnotations());
    }

    public void ClearPendingAnnotations()
    {
        DeselectAnnotation();
        AnnotationOverlay.Children.Clear();
        _currentDrawingShape = null;
        _selectedAnnotation = null;
        _currentBrushPoints.Clear();
        ResetAnnotationOverlayHistory();
        HideFloatingButtons();
        PendingAnnotationsCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 保存/另存时由 MainWindow 调用：把当前覆盖层标注提交到 ViewModel，真正写入目标文件由保存流程完成。
    /// </summary>
    public void FlushPendingAnnotations()
    {
        RaisePendingAnnotationsChanged();
    }

    /// <summary>
    /// 确认标注（兼容快捷键：仅同步待保存标注，不烘焙图片）
    /// </summary>
    public Task ConfirmAnnotationAsync()
    {
        RaisePendingAnnotationsChanged();
        return Task.CompletedTask;
    }

    #endregion

    #region 标注选中/删除

    private void StartMoveAnnotation(FrameworkElement shape, Point startPos)
    {
        _selectedAnnotation = shape;
        _isMovingAnnotation = true;
        _annotationMoveLast = startPos;
        _annotationMoved = false;
        HideFloatingButtons();
        ViewerGrid.CaptureMouse();
        AnnotationOverlay.CaptureMouse();
        SetViewerCursor(Cursors.SizeAll);
    }

    private void MoveSelectedAnnotation(Point pos)
    {
        if (_selectedAnnotation == null) return;

        var dx = pos.X - _annotationMoveLast.X;
        var dy = pos.Y - _annotationMoveLast.Y;
        if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) return;

        OffsetAnnotationElement(_selectedAnnotation, dx, dy);
        _annotationMoveLast = pos;
        _annotationMoved = true;

        if (_selectedAnnotation is TextBlock)
            UpdateTextResizeHandlePosition();
    }

    private void EndMoveAnnotation()
    {
        var movedShape = _selectedAnnotation;

        _isMovingAnnotation = false;
        ViewerGrid.ReleaseMouseCapture();
        AnnotationOverlay.ReleaseMouseCapture();
        SetViewerCursor(ViewModel?.IsAnnotationMode == true ? Cursors.Cross : Cursors.Arrow);

        if (movedShape == null) return;

        if (_annotationMoved)
        {
            RaisePendingAnnotationsChanged();
            PushAnnotationOverlayHistory();
        }

        if (AnnotationOverlay.Children.Contains(movedShape))
            SelectAnnotation(movedShape);
    }

    private static void OffsetAnnotationElement(FrameworkElement element, double dx, double dy)
    {
        switch (element)
        {
            case Rectangle or System.Windows.Shapes.Ellipse or TextBlock:
                Canvas.SetLeft(element, SafeCanvasLeft(element) + dx);
                Canvas.SetTop(element, SafeCanvasTop(element) + dy);
                break;
            case Line line:
                line.X1 += dx;
                line.Y1 += dy;
                line.X2 += dx;
                line.Y2 += dy;
                break;
            case Polyline polyline:
                for (var i = 0; i < polyline.Points.Count; i++)
                {
                    var p = polyline.Points[i];
                    polyline.Points[i] = new Point(p.X + dx, p.Y + dy);
                }
                break;
            case Path path:
                OffsetPathGeometry(path, dx, dy);
                break;
        }
    }

    private static void OffsetPathGeometry(Path path, double dx, double dy)
    {
        if (path.Data is not PathGeometry geo) return;

        foreach (var figure in geo.Figures)
        {
            figure.StartPoint = new Point(figure.StartPoint.X + dx, figure.StartPoint.Y + dy);
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment lineSegment:
                        lineSegment.Point = new Point(lineSegment.Point.X + dx, lineSegment.Point.Y + dy);
                        break;
                    case PolyLineSegment polyLineSegment:
                        for (var i = 0; i < polyLineSegment.Points.Count; i++)
                        {
                            var p = polyLineSegment.Points[i];
                            polyLineSegment.Points[i] = new Point(p.X + dx, p.Y + dy);
                        }
                        break;
                    case BezierSegment bezierSegment:
                        bezierSegment.Point1 = new Point(bezierSegment.Point1.X + dx, bezierSegment.Point1.Y + dy);
                        bezierSegment.Point2 = new Point(bezierSegment.Point2.X + dx, bezierSegment.Point2.Y + dy);
                        bezierSegment.Point3 = new Point(bezierSegment.Point3.X + dx, bezierSegment.Point3.Y + dy);
                        break;
                    case QuadraticBezierSegment quadraticBezierSegment:
                        quadraticBezierSegment.Point1 = new Point(quadraticBezierSegment.Point1.X + dx, quadraticBezierSegment.Point1.Y + dy);
                        quadraticBezierSegment.Point2 = new Point(quadraticBezierSegment.Point2.X + dx, quadraticBezierSegment.Point2.Y + dy);
                        break;
                }
            }
        }
    }

    private static double SafeCanvasLeft(UIElement element)
    {
        var value = Canvas.GetLeft(element);
        return double.IsNaN(value) ? 0 : value;
    }

    private static double SafeCanvasTop(UIElement element)
    {
        var value = Canvas.GetTop(element);
        return double.IsNaN(value) ? 0 : value;
    }

    private void OnAnnotationOverlayMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.IsAnnotationMode != true) return;

        var pos = e.GetPosition(AnnotationOverlay);
        var hit = FindAnnotationAt(pos);

        if (hit != null)
        {
            SelectAnnotation(hit);
            e.Handled = true;
        }
        else
        {
            DeselectAnnotation();
            HideFloatingButtons();
        }
    }

    private void OnAnnotationOverlayMouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel?.IsAnnotationMode != true) return;

        var pos = e.GetPosition(AnnotationOverlay);
        var hit = FindAnnotationAt(pos);
        Cursor = hit != null ? Cursors.Hand : Cursors.Cross;
    }

    private FrameworkElement? FindAnnotationAt(Point pos)
    {
        // 反向遍历（后绘制的在上面）
        for (int i = AnnotationOverlay.Children.Count - 1; i >= 0; i--)
        {
            if (AnnotationOverlay.Children[i] is not FrameworkElement fe) continue;

            // 跳过缩放控制点（非标注元素）
            if (fe.Tag as string == "ResizeHandle") continue;

            if (fe is Rectangle rect)
            {
                var left = Canvas.GetLeft(rect);
                var top = Canvas.GetTop(rect);
                var bounds = new Rect(left, top, rect.Width, rect.Height);
                // 边框命中检测（扩展 5 像素）
                var outer = bounds;
                var inner = new Rect(left + 5, top + 5, Math.Max(0, rect.Width - 10), Math.Max(0, rect.Height - 10));
                if (outer.Contains(pos) && !inner.Contains(pos)) return rect;
            }
            else if (fe is System.Windows.Shapes.Ellipse ellipse)
            {
                var left = Canvas.GetLeft(ellipse);
                var top = Canvas.GetTop(ellipse);
                var bounds = new Rect(left, top, ellipse.Width, ellipse.Height);
                var outer = bounds;
                var inner = new Rect(left + 5, top + 5, Math.Max(0, ellipse.Width - 10), Math.Max(0, ellipse.Height - 10));
                if (outer.Contains(pos) && !inner.Contains(pos)) return ellipse;
            }
            else if (fe is Line line)
            {
                // 线段命中检测：点到线段距离 < 5
                var dist = DistanceToLineSegment(pos, new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
                if (dist < 5) return line;
            }
            else if (fe is Polyline polyline)
            {
                // 画笔命中检测：检查所有线段
                for (int j = 0; j < polyline.Points.Count - 1; j++)
                {
                    var dist = DistanceToLineSegment(pos, polyline.Points[j], polyline.Points[j + 1]);
                    if (dist < Math.Max(5, polyline.StrokeThickness / 2)) return polyline;
                }
            }
            else if (fe is TextBlock textBlock)
            {
                // 文字命中检测：检查文字区域
                var left = Canvas.GetLeft(textBlock);
                var top = Canvas.GetTop(textBlock);
                var bounds = new Rect(left, top, textBlock.ActualWidth + 4, textBlock.ActualHeight + 4);
                if (bounds.Contains(pos)) return textBlock;
            }
            else if (fe is Path path)
            {
                // 箭头命中检测：检查线段和三角形
                if (path.Data is PathGeometry geo && geo.Figures.Count > 0)
                {
                    var fig = geo.Figures[0];
                    if (fig.Segments.Count > 0 && fig.Segments[0] is LineSegment seg)
                    {
                        var dist = DistanceToLineSegment(pos, fig.StartPoint, seg.Point);
                        if (dist < 5) return path;
                    }
                }
            }
        }
        return null;
    }

    private static double DistanceToLineSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq < 0.001) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        var projX = a.X + t * dx;
        var projY = a.Y + t * dy;
        return Math.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
    }

    private void SelectAnnotation(FrameworkElement shape)
    {
        DeselectAnnotation();
        _selectedAnnotation = shape;

        // 高亮：只改当前选中的可视样式；真实颜色/线宽保存在 Tag 中，另存时不使用高亮样式。
        if (shape is Rectangle rect)
        {
            _selectedAnnotationOriginalStroke = rect.Stroke;
            _selectedAnnotationOriginalFill = rect.Fill;
            _selectedAnnotationOriginalThickness = rect.StrokeThickness;
            rect.Stroke = new SolidColorBrush(Colors.Cyan);
            rect.StrokeThickness = 5;
        }
        else if (shape is Line line)
        {
            _selectedAnnotationOriginalStroke = line.Stroke;
            _selectedAnnotationOriginalFill = Brushes.Transparent;
            _selectedAnnotationOriginalThickness = line.StrokeThickness;
            line.Stroke = new SolidColorBrush(Colors.Cyan);
            line.StrokeThickness = 5;
        }
        else if (shape is Path path)
        {
            _selectedAnnotationOriginalStroke = path.Stroke;
            _selectedAnnotationOriginalFill = path.Fill;
            _selectedAnnotationOriginalThickness = path.StrokeThickness;
            path.Stroke = new SolidColorBrush(Colors.Cyan);
            path.Fill = Brushes.Cyan;
            path.StrokeThickness = 5;
        }
        else if (shape is Polyline polyline)
        {
            _selectedAnnotationOriginalStroke = polyline.Stroke;
            _selectedAnnotationOriginalFill = Brushes.Transparent;
            _selectedAnnotationOriginalThickness = polyline.StrokeThickness;
            polyline.Stroke = new SolidColorBrush(Colors.Cyan);
            polyline.StrokeThickness = Math.Max(polyline.StrokeThickness, 5);
        }
        else if (shape is TextBlock textBlock)
        {
            _selectedAnnotationOriginalStroke = textBlock.Foreground;
            _selectedAnnotationOriginalFill = Brushes.Transparent;
            _selectedAnnotationOriginalThickness = 0;
            textBlock.Foreground = new SolidColorBrush(Colors.Cyan);
            // 显示文字缩放控制点
            ShowTextResizeHandle(textBlock);
        }

        // 浮动按钮跟随到选中标注旁边
        ShowFloatingButtonsAroundShape(shape);
    }

    private void DeselectAnnotation()
    {
        if (_selectedAnnotation == null) return;

        // 恢复原始样式
        if (_selectedAnnotation is Rectangle rect)
        {
            rect.Stroke = _selectedAnnotationOriginalStroke;
            rect.Fill = _selectedAnnotationOriginalFill;
            rect.StrokeThickness = _selectedAnnotationOriginalThickness;
        }
        else if (_selectedAnnotation is Line line)
        {
            line.Stroke = _selectedAnnotationOriginalStroke;
            line.StrokeThickness = _selectedAnnotationOriginalThickness;
        }
        else if (_selectedAnnotation is Path path)
        {
            path.Stroke = _selectedAnnotationOriginalStroke;
            path.Fill = _selectedAnnotationOriginalFill;
            path.StrokeThickness = _selectedAnnotationOriginalThickness;
        }
        else if (_selectedAnnotation is Polyline polyline)
        {
            polyline.Stroke = _selectedAnnotationOriginalStroke;
            polyline.StrokeThickness = _selectedAnnotationOriginalThickness;
        }
        else if (_selectedAnnotation is TextBlock textBlock)
        {
            textBlock.Foreground = _selectedAnnotationOriginalStroke;
            HideTextResizeHandle();
        }

        _selectedAnnotation = null;
    }

    /// <summary>
    /// 显示文字缩放控制点（右下角小方块）
    /// </summary>
    private void ShowTextResizeHandle(TextBlock textBlock)
    {
        HideTextResizeHandle();

        _textResizeHandle = new Rectangle
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(Colors.Cyan),
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            Cursor = Cursors.SizeNWSE,
            IsHitTestVisible = true,
            Tag = "ResizeHandle"
        };

        // 定位到文字右下角
        var left = Canvas.GetLeft(textBlock) + textBlock.ActualWidth - 6;
        var top = Canvas.GetTop(textBlock) + textBlock.ActualHeight - 6;
        Canvas.SetLeft(_textResizeHandle, left);
        Canvas.SetTop(_textResizeHandle, top);

        AnnotationOverlay.Children.Add(_textResizeHandle);

        // 使用 Preview 事件，确保在所有父级处理之前捕获
        _textResizeHandle.PreviewMouseLeftButtonDown += TextResizeHandle_PreviewMouseDown;
    }

    private void HideTextResizeHandle()
    {
        if (_textResizeHandle != null)
        {
            _textResizeHandle.PreviewMouseLeftButtonDown -= TextResizeHandle_PreviewMouseDown;
            AnnotationOverlay.Children.Remove(_textResizeHandle);
            _textResizeHandle = null;
            _isResizingText = false;
        }
    }

    private void TextResizeHandle_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        StartTextResize(e.GetPosition(AnnotationOverlay), e);
    }

    private bool IsTextResizeHandleSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (ReferenceEquals(source, _textResizeHandle)) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void StartTextResize(Point startPos, MouseButtonEventArgs e)
    {
        if (_selectedAnnotation is not TextBlock textBlock) return;

        _isResizingText = true;
        _textResizeStart = startPos;
        _textResizeStartFontSize = textBlock.FontSize;
        AnnotationOverlay.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// 更新文字缩放控制点位置
    /// </summary>
    private void UpdateTextResizeHandlePosition()
    {
        if (_textResizeHandle == null || _selectedAnnotation is not TextBlock textBlock) return;
        var left = Canvas.GetLeft(textBlock) + textBlock.ActualWidth - 5;
        var top = Canvas.GetTop(textBlock) + textBlock.ActualHeight - 5;
        Canvas.SetLeft(_textResizeHandle, left);
        Canvas.SetTop(_textResizeHandle, top);
    }

    private void DeleteSelectedAnnotation()
    {
        if (_selectedAnnotation == null) return;
        HideTextResizeHandle();
        AnnotationOverlay.Children.Remove(_selectedAnnotation);
        _selectedAnnotation = null;
        HideFloatingButtons();

        RaisePendingAnnotationsChanged();
        PushAnnotationOverlayHistory();
    }

    /// <summary>
    /// 处理 Delete 键删除选中标注
    /// </summary>
    public void HandleDeleteKey()
    {
        if (_selectedAnnotation != null)
            DeleteSelectedAnnotation();
    }

    private void ResetAnnotationOverlayHistory()
    {
        _annotationOverlayHistory.Clear();
        _annotationOverlayHistoryIndex = -1;
        PushAnnotationOverlayHistory();
    }

    private void PushAnnotationOverlayHistory()
    {
        if (_restoringAnnotationOverlayHistory) return;

        var snapshot = CloneAnnotationShapes(CollectPendingAnnotations());
        if (_annotationOverlayHistoryIndex >= 0 && AreAnnotationSnapshotsEqual(_annotationOverlayHistory[_annotationOverlayHistoryIndex], snapshot))
            return;

        if (_annotationOverlayHistoryIndex < _annotationOverlayHistory.Count - 1)
            _annotationOverlayHistory.RemoveRange(_annotationOverlayHistoryIndex + 1, _annotationOverlayHistory.Count - _annotationOverlayHistoryIndex - 1);

        _annotationOverlayHistory.Add(snapshot);
        _annotationOverlayHistoryIndex = _annotationOverlayHistory.Count - 1;
    }

    private void UndoAnnotationOverlay()
    {
        FinalizeInlineTextInput();
        if (_annotationOverlayHistoryIndex <= 0) return;
        _annotationOverlayHistoryIndex--;
        RestoreAnnotationOverlaySnapshot(_annotationOverlayHistory[_annotationOverlayHistoryIndex]);
    }

    private void RedoAnnotationOverlay()
    {
        FinalizeInlineTextInput();
        if (_annotationOverlayHistoryIndex >= _annotationOverlayHistory.Count - 1) return;
        _annotationOverlayHistoryIndex++;
        RestoreAnnotationOverlaySnapshot(_annotationOverlayHistory[_annotationOverlayHistoryIndex]);
    }

    private void RestoreAnnotationOverlaySnapshot(IReadOnlyList<AnnotationShape> snapshot)
    {
        _restoringAnnotationOverlayHistory = true;
        try
        {
            DeselectAnnotation();
            AnnotationOverlay.Children.Clear();
            _currentDrawingShape = null;
            _selectedAnnotation = null;
            _currentBrushPoints.Clear();

            foreach (var shape in snapshot)
                AddAnnotationVisualFromShape(shape);

            RaisePendingAnnotationsChanged();
        }
        finally
        {
            _restoringAnnotationOverlayHistory = false;
        }
    }

    private void AddAnnotationVisualFromShape(AnnotationShape shape)
    {
        FrameworkElement? element = null;
        var start = ImageToScreenCoords(shape.Start);
        var end = ImageToScreenCoords(shape.End);
        var brush = ParseBrush(shape.Color);
        var screenStrokeThickness = ImageLengthToScreenLength(shape.StrokeThickness);
        var screenFontSize = ImageLengthToScreenLength(shape.FontSize);

        switch (shape.Tool)
        {
            case AnnotationTool.Text:
                if (string.IsNullOrEmpty(shape.Text)) return;
                element = new TextBlock
                {
                    Text = shape.Text,
                    Foreground = brush,
                    FontSize = screenFontSize,
                    FontWeight = FontWeights.Normal,
                    Tag = new AnnotationVisualMetadata(AnnotationTool.Text, shape.Color, screenFontSize)
                };
                Canvas.SetLeft(element, start.X);
                Canvas.SetTop(element, start.Y);
                break;
            case AnnotationTool.Rectangle:
                var rect = new Rectangle { Stroke = brush, StrokeThickness = screenStrokeThickness, Fill = Brushes.Transparent };
                Canvas.SetLeft(rect, Math.Min(start.X, end.X));
                Canvas.SetTop(rect, Math.Min(start.Y, end.Y));
                rect.Width = Math.Abs(end.X - start.X);
                rect.Height = Math.Abs(end.Y - start.Y);
                element = rect;
                break;
            case AnnotationTool.Ellipse:
                var ellipse = new System.Windows.Shapes.Ellipse { Stroke = brush, StrokeThickness = screenStrokeThickness, Fill = Brushes.Transparent };
                Canvas.SetLeft(ellipse, Math.Min(start.X, end.X));
                Canvas.SetTop(ellipse, Math.Min(start.Y, end.Y));
                ellipse.Width = Math.Abs(end.X - start.X);
                ellipse.Height = Math.Abs(end.Y - start.Y);
                element = ellipse;
                break;
            case AnnotationTool.Line:
                element = new Line { X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y, Stroke = brush, StrokeThickness = screenStrokeThickness };
                break;
            case AnnotationTool.Arrow:
                element = CreateArrowPath(start, end, brush, screenStrokeThickness);
                break;
            case AnnotationTool.Brush when shape.Points is { Count: > 1 }:
                var polyline = new Polyline { Stroke = brush, StrokeThickness = screenStrokeThickness, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                foreach (var p in shape.Points)
                    polyline.Points.Add(ImageToScreenCoords(p));
                element = polyline;
                break;
        }

        if (element == null) return;
        element.Tag ??= new AnnotationVisualMetadata(shape.Tool, shape.Color, shape.Tool == AnnotationTool.Text ? screenFontSize : screenStrokeThickness);
        AnnotationOverlay.Children.Add(element);
    }

    private static List<AnnotationShape> CloneAnnotationShapes(IEnumerable<AnnotationShape> shapes)
    {
        return shapes.Select(s => new AnnotationShape
        {
            Tool = s.Tool,
            Start = s.Start,
            End = s.End,
            Color = s.Color,
            StrokeThickness = s.StrokeThickness,
            Text = s.Text,
            Points = s.Points?.ToList(),
            FontSize = s.FontSize
        }).ToList();
    }

    private static bool AreAnnotationSnapshotsEqual(IReadOnlyList<AnnotationShape> a, IReadOnlyList<AnnotationShape> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Tool != b[i].Tool || a[i].Text != b[i].Text || a[i].Color != b[i].Color ||
                Math.Abs(a[i].FontSize - b[i].FontSize) > 0.01 ||
                Math.Abs(a[i].StrokeThickness - b[i].StrokeThickness) > 0.01 ||
                Distance(a[i].Start, b[i].Start) > 0.01 || Distance(a[i].End, b[i].End) > 0.01)
                return false;

            var aPoints = a[i].Points;
            var bPoints = b[i].Points;
            if ((aPoints == null) != (bPoints == null))
                return false;
            if (aPoints != null && bPoints != null)
            {
                if (aPoints.Count != bPoints.Count)
                    return false;
                for (var j = 0; j < aPoints.Count; j++)
                {
                    if (Distance(aPoints[j], bPoints[j]) > 0.01)
                        return false;
                }
            }
        }
        return true;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private Path CreateArrowPath(Point start, Point end, Brush brush, double strokeThickness)
    {
        var path = new Path
        {
            Stroke = brush,
            StrokeThickness = strokeThickness,
            Fill = brush
        };
        UpdateArrowPath(path, start, end);
        return path;
    }

    private static void UpdateArrowPath(Path path, Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;

        var angle = Math.Atan2(dy, dx);
        var headLen = Math.Min(15, len * 0.3);
        var hx1 = end.X - headLen * Math.Cos(angle - Math.PI / 6);
        var hy1 = end.Y - headLen * Math.Sin(angle - Math.PI / 6);
        var hx2 = end.X - headLen * Math.Cos(angle + Math.PI / 6);
        var hy2 = end.Y - headLen * Math.Sin(angle + Math.PI / 6);

        var lineFig = new PathFigure { StartPoint = start };
        lineFig.Segments.Add(new LineSegment(end, true));

        var headFig = new PathFigure { StartPoint = end, IsClosed = true };
        headFig.Segments.Add(new LineSegment(new Point(hx1, hy1), true));
        headFig.Segments.Add(new LineSegment(new Point(hx2, hy2), true));

        var geo = new PathGeometry();
        geo.Figures.Add(lineFig);
        geo.Figures.Add(headFig);
        path.Data = geo;
    }

    private static Brush ParseBrush(string hex)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return Brushes.Red;
        }
    }

    #endregion

    #region 内联文字输入

    private TextBox? _inlineTextBox;

    /// <summary>
    /// 在点击位置显示内联文字输入框（输入即显示，点击别处或Esc完成）
    /// </summary>
    private void ShowInlineTextInput(Point screenPos, string color)
    {
        // 移除之前的输入框（自动完成上一个）
        FinalizeInlineTextInput();

        var fontSize = ViewModel?.AnnotationFontSize ?? 24;

        var textBox = new TextBox
        {
            MinWidth = 80,
            Width = 200,
            Height = 32,
            FontSize = fontSize,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = ParseBrush(color),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 0, 2, 0),
            CaretBrush = ParseBrush(color)
        };

        Canvas.SetLeft(textBox, screenPos.X);
        Canvas.SetTop(textBox, screenPos.Y);
        AnnotationOverlay.Children.Add(textBox);
        _inlineTextBox = textBox;

        textBox.Focus();
        textBox.LostFocus += (_, _) => FinalizeInlineTextInput();
        textBox.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                textBox.Undo();
                e.Handled = true;
            }
            else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                textBox.Redo();
                e.Handled = true;
            }
        };
        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                RemoveInlineTextInput();
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// 完成内联文字输入（TextBox → TextBlock）
    /// </summary>
    private void FinalizeInlineTextInput()
    {
        if (_inlineTextBox == null) return;

        var text = _inlineTextBox.Text?.Trim();
        var pos = new Point(Canvas.GetLeft(_inlineTextBox), Canvas.GetTop(_inlineTextBox));
        var foreground = _inlineTextBox.Foreground;
        var fontSize = _inlineTextBox.FontSize;

        RemoveInlineTextInput();

        if (!string.IsNullOrEmpty(text))
        {
            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = fontSize,
                FontWeight = FontWeights.Normal
            };
            var colorStr = foreground is System.Windows.Media.SolidColorBrush scb
                ? $"#{scb.Color.A:X2}{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}"
                : "#FFE53935";
            textBlock.Tag = new AnnotationVisualMetadata(AnnotationTool.Text, colorStr, fontSize);
            Canvas.SetLeft(textBlock, pos.X);
            Canvas.SetTop(textBlock, pos.Y);
            AnnotationOverlay.Children.Add(textBlock);
            RaisePendingAnnotationsChanged();
            PushAnnotationOverlayHistory();
        }
    }

    private void RemoveInlineTextInput()
    {
        if (_inlineTextBox != null)
        {
            AnnotationOverlay.Children.Remove(_inlineTextBox);
            _inlineTextBox = null;
        }
    }

    #endregion

    #region 文字输入对话框

    private static string? ShowTextInputDialog()
    {
        var window = new Window
        {
            Title = "输入文字",
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
            Foreground = Brushes.White
        };

        var panel = new StackPanel { Margin = new Thickness(12) };

        var label = new TextBlock
        {
            Text = "请输入标注文字：",
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(label);

        var textBox = new TextBox
        {
            Height = 28,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12),
            Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66))
        };
        panel.Children.Add(textBox);

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var okBtn = new Button
        {
            Content = "确定",
            Width = 70,
            Height = 28,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            IsDefault = true
        };
        okBtn.Click += (_, _) => { window.DialogResult = true; };
        buttonPanel.Children.Add(okBtn);

        var cancelBtn = new Button
        {
            Content = "取消",
            Width = 70,
            Height = 28,
            Background = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            IsCancel = true
        };
        buttonPanel.Children.Add(cancelBtn);
        panel.Children.Add(buttonPanel);

        window.Content = panel;
        textBox.Focus();

        return window.ShowDialog() == true ? textBox.Text : null;
    }

    #endregion

    #region 拖拽

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files != null && files.Length > 0)
            ViewModel?.HandleDrop(files);
    }

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;

    private static nint MakeLParam(double x, double y)
    {
        var lowWord = unchecked((ushort)(short)Math.Round(x));
        var highWord = unchecked((ushort)(short)Math.Round(y));
        return (nint)(lowWord | (highWord << 16));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, int wParam, nint lParam);

    #endregion

    #region 固定到桌面

    /// <summary>
    /// 获取浮窗exe路径：优先用外部目录，找不到则从嵌入资源解压到临时目录（兜底）。
    /// </summary>
    private static string GetFloatingViewerExePath()
    {
        // 第1优先：AppDir 旁边的 FloatingImageViewerPublish/（普通目录版、用户手动放置）
        string externalPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "FloatingImageViewerPublish",
            "FloatingImageViewerApp.exe");
        if (File.Exists(externalPath))
            return externalPath;

        // 第2兜底：从嵌入资源解压到 %TEMP%/FloatingImageViewerPublish/（单文件版）
        string tempDir  = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FloatingImageViewerPublish");
        string tempExe  = System.IO.Path.Combine(tempDir, "FloatingImageViewerApp.exe");
        Directory.CreateDirectory(tempDir);

        var asm      = System.Reflection.Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("FloatingImageViewerApp.exe");
        if (stream == null)
            throw new FileNotFoundException("嵌入资源 FloatingImageViewerApp.exe 未找到，请确认发布包完整。");

        using var fs = new FileStream(tempExe, FileMode.Create, FileAccess.Write);
        stream.CopyTo(fs);

        return tempExe;
    }

    private void ContextMenu_PinToDesktop_Click(object sender, RoutedEventArgs e)
    {
        BitmapSource? imageToPin = null;

        if (ViewModel == null || (!ViewModel.HasImage && !ViewModel.IsCropMode))
        {
            MessageBox.Show("没有可固定的图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (ViewModel.CurrentImage != null)
            imageToPin = ViewModel.CurrentImage;

        if (imageToPin == null)
        {
            MessageBox.Show("没有可固定的图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            string tempFilePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"lvi_float_{Guid.NewGuid()}.png");
            SaveBitmapSourceToFile(imageToPin, tempFilePath);

            string floatingViewerExePath = GetFloatingViewerExePath();

            if (File.Exists(floatingViewerExePath))
            {
                Process.Start(floatingViewerExePath, $"\"{tempFilePath}\"");
            }
            else
            {
                MessageBox.Show(
                    $"图片浮窗应用未找到：{floatingViewerExePath}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建图片浮窗失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveBitmapSourceToFile(BitmapSource bitmapSource, string filePath)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
        using var fileStream = new FileStream(filePath, FileMode.Create);
        encoder.Save(fileStream);
    }

    private void ContextMenu_ExitFullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && ViewModel.IsFullScreen)
        {
            ViewModel.IsFullScreen = false;
        }
        else
        {
            MessageBox.Show("当前不在全屏模式。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    #endregion
}
