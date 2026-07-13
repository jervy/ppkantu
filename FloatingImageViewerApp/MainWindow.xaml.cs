using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FloatingImageViewerApp
{
    public partial class MainWindow : Window
    {
        // Win32 常量：触发原生拖拽缩放
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTBOTTOMRIGHT = 17;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // 图片宽高比，用于 SizeChanged 时保持比例
        private double _imageAspectRatio = 0;
        // 防止 SizeChanged 递归
        private bool _isResizingByAspect = false;

        // ContentGrid 的 Margin 补偿（top=20, right=20）
        // 窗口尺寸 = 内容尺寸 + 这两个偏移量，否则 Uniform 拉伸会出现黑边
        private const double MarginTop   = 20;
        private const double MarginRight = 20;

        public MainWindow()
        {
            InitializeComponent();
        }

        // 左键拖动窗口
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        // 右下角把手：触发原生缩放
        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTBOTTOMRIGHT, IntPtr.Zero);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            BitmapImage? bitmap = null;
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                string imagePath = args[1].Trim('"');
                if (File.Exists(imagePath))
                {
                    try
                    {
                        using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        FloatingImage.Source = bitmap;

                        // 加载后尝试删除临时文件（忽略失败）
                        try { File.Delete(imagePath); } catch { }

                        // 保存图片宽高比，供 SizeChanged 使用
                        if (bitmap.PixelHeight > 0)
                            _imageAspectRatio = (double)bitmap.PixelWidth / bitmap.PixelHeight;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"无法加载图片: {ex.Message}", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        this.Close();
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("图片文件不存在。", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    this.Close();
                    return;
                }
            }
            else
            {
                MessageBox.Show("未提供图片路径。", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
                return;
            }

            if (bitmap == null || _imageAspectRatio <= 0)
            {
                this.Width  = 240 + MarginRight;
                this.Height = 240 + MarginTop;
            }
            else
            {
                // 可用内容区最大尺寸（屏幕 20% - Margin 补偿）
                double maxContentW = SystemParameters.WorkArea.Width  * 0.20 - MarginRight;
                double maxContentH = SystemParameters.WorkArea.Height * 0.20 - MarginTop;

                double cw = bitmap.PixelWidth;
                double ch = bitmap.PixelHeight;

                // 按图片比例缩放到内容区范围内
                if (cw > maxContentW || ch > maxContentH)
                {
                    if (cw / maxContentW > ch / maxContentH)
                    {
                        cw = maxContentW;
                        ch = cw / _imageAspectRatio;
                    }
                    else
                    {
                        ch = maxContentH;
                        cw = ch * _imageAspectRatio;
                    }
                }

                // 窗口尺寸 = 内容区尺寸 + Margin 补偿，确保无黑边
                this.Width  = Math.Max(cw, 80) + MarginRight;
                this.Height = Math.Max(ch, 60) + MarginTop;
            }

            // 默认位置：右上角，距边缘 20px
            var workArea = SystemParameters.WorkArea;
            this.Left = workArea.Right - this.Width  - 20;
            this.Top  = workArea.Top   + 20;
        }

        // 调整大小时保持图片宽高比（同样需计入 Margin 补偿）
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_imageAspectRatio <= 0 || _isResizingByAspect) return;

            // 内容区实际尺寸 = 窗口尺寸 - Margin 补偿
            // 保持 contentW / contentH == _imageAspectRatio
            if (e.WidthChanged)
            {
                _isResizingByAspect = true;
                double contentW = this.Width - MarginRight;
                double contentH = contentW / _imageAspectRatio;
                this.Height = Math.Max(contentH, 60) + MarginTop;
                _isResizingByAspect = false;
            }
            else if (e.HeightChanged)
            {
                _isResizingByAspect = true;
                double contentH = this.Height - MarginTop;
                double contentW = contentH * _imageAspectRatio;
                this.Width = Math.Max(contentW, 60) + MarginRight;
                _isResizingByAspect = false;
            }
        }

        private void CloseFloatingWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
