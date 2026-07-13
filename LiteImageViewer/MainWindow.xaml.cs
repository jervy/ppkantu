using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using LiteImageViewer.Models;
using LiteImageViewer.ViewModels;

namespace LiteImageViewer;
public partial class MainWindow : Window
{
    /// <summary>主窗口单例，供浮窗设置 Owner 使用。</summary>
    public static MainWindow? Instance { get; private set; }

    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ImageViewer.PendingAnnotationsChanged += OnPendingAnnotationsChanged;
        ImageViewer.PendingAnnotationsCleared += OnPendingAnnotationsCleared;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsFullScreen))
        {
            if (_viewModel.IsFullScreen)
                EnterFullScreen();
            else
                ExitFullScreen();
        }
    }

    private void EnterFullScreen()
    {
        // 先恢复正常窗口状态，再修改尺寸，避免最大化时 Left/Top 为负值导致偏移
        WindowState = WindowState.Normal;
        WindowChrome.SetWindowChrome(this, null);

        TitleBarRow.Height = new GridLength(0);
        ToolbarRow.Height = new GridLength(0);
        StatusBarRow.Height = new GridLength(0);

        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GWL_STYLE);
        SetWindowLongPtr(hwnd, GWL_STYLE, (nint)((style & ~WS_CAPTION & ~WS_THICKFRAME) | WS_POPUP));

        Top = 0;
        Left = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Topmost = true;
    }

    private void ExitFullScreen()
    {
        TitleBarRow.Height = new GridLength(30);
        ToolbarRow.Height = GridLength.Auto;
        StatusBarRow.Height = GridLength.Auto;

        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GWL_STYLE);
        SetWindowLongPtr(hwnd, GWL_STYLE, (nint)((style | WS_CAPTION | WS_THICKFRAME) & ~WS_POPUP));

        Topmost = false;
        WindowStyle = WindowStyle.None;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 34,
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(8),
            CornerRadius = new CornerRadius(8)
        });

        Left = _initialBounds.X;
        Top = _initialBounds.Y;
        Width = _initialBounds.Width;
        Height = _initialBounds.Height;

        WindowState = WindowState.Normal;
    }

    private Rect _initialBounds;

    #region Win32

    private const int GWL_STYLE = -16;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowLongPtr(IntPtr hwnd, int index, nint newStyle);

    #endregion

    #region 标题栏

    private async void OnPendingAnnotationsChanged(object? sender, IReadOnlyList<AnnotationShape> shapes)
    {
        await _viewModel.ExecuteAnnotationAsync(shapes.ToList());
    }

    private async void OnPendingAnnotationsCleared(object? sender, EventArgs e)
    {
        await _viewModel.ExecuteAnnotationAsync(new List<AnnotationShape>());
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (_viewModel.IsFullScreen) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            RestoreWindowForDrag(e.GetPosition(this));
        }

        try { DragMove(); } catch { }
    }

    #endregion

    #region 窗口按钮

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    private void BtnFullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();
    private void ToggleFullScreen() => _viewModel.IsFullScreen = !_viewModel.IsFullScreen;
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void RestoreWindowForDrag(Point mousePosition)
    {
        var currentScreenPoint = PointToScreen(mousePosition);
        var restoreWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : Width;
        var horizontalRatio = ActualWidth > 0 ? mousePosition.X / ActualWidth : 0.5;
        WindowState = WindowState.Normal;
        Width = restoreWidth;
        Left = currentScreenPoint.X - restoreWidth * horizontalRatio;
        Top = currentScreenPoint.Y - Math.Min(mousePosition.Y, 20);
    }

    #endregion

    #region 窗口事件

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            WindowMaximizeHelper.AdjustMaximizedSize(hwnd, lParam);
            handled = true;
        }
        else if (msg == WM_NCLBUTTONDOWN)
        {
            // 全屏模式下禁止所有窗口拖动
            if (_viewModel.IsFullScreen)
            {
                handled = true;
                return nint.Zero;
            }

            if (WindowState == WindowState.Maximized)
            {
                var screenPoint = GetScreenPoint(lParam);
                if (ImageViewer.TryNavigateFromChromeEdgeClick(screenPoint))
                {
                    handled = true;
                    return nint.Zero;
                }
            }
        }
        else if (msg == WM_NCHITTEST && !_viewModel.IsFullScreen)
        {
            var hitTestResult = DefWindowProc(hwnd, msg, wParam, lParam);
            if (hitTestResult == HTCLIENT && IsPointInsideTitleBar(lParam))
            {
                handled = true;
                return HTCAPTION;
            }
        }

        return nint.Zero;
    }

    private bool IsPointInsideTitleBar(nint lParam)
    {
        if (TitleBar.ActualHeight <= 0 || ActualWidth <= 0) return false;
        var screenPoint = GetScreenPoint(lParam);
        var titleBarPoint = TitleBar.PointFromScreen(screenPoint);
        if (titleBarPoint.X < 0 || titleBarPoint.Y < 0 ||
            titleBarPoint.X > TitleBar.ActualWidth || titleBarPoint.Y > TitleBar.ActualHeight)
            return false;
        var source = TitleBar.InputHitTest(titleBarPoint) as DependencyObject;
        return !IsInWindowButton(source);
    }

    private bool IsInWindowButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, BtnMinimize) ||
                ReferenceEquals(source, BtnMaximize) ||
                ReferenceEquals(source, BtnFullScreen) ||
                ReferenceEquals(source, BtnClose))
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static short GetSignedLoWord(nint value) => unchecked((short)((long)value & 0xFFFF));
    private static short GetSignedHiWord(nint value) => unchecked((short)(((long)value >> 16) & 0xFFFF));
    private static Point GetScreenPoint(nint lParam) => new(GetSignedLoWord(lParam), GetSignedHiWord(lParam));

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hwnd, int msg, nint wParam, nint lParam);

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 记录初始窗口位置和大小，退出全屏时恢复
        _initialBounds = new Rect(Left, Top, Width, Height);

        _viewModel.StatusMessage = "就绪";
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            var imageArgs = args.Skip(1)
                .Where(a => File.Exists(a) && Utils.ImageFormatHelper.IsSupportedImage(a))
                .ToList();
            if (imageArgs.Count > 0)
                _viewModel.HandleDrop(imageArgs.ToArray());
        }

        // 文件关联状态延迟到 Loaded 后异步读取，不阻塞窗口显示
        await _viewModel.InitializeFileAssociationAsync();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        var tempDir = Path.GetTempPath();
        try
        {
            foreach (var f in Directory.GetFiles(tempDir, "lvi_*"))
                try { File.Delete(f); } catch { }
        }
        catch { }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        _viewModel.HandleKeyDown(e.Key, ctrl, alt);
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files != null && files.Length > 0)
            _viewModel.HandleDrop(files);
    }

    #endregion
}
