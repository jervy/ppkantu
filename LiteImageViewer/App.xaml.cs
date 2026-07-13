using System.Windows;
using System.Windows.Threading;
using LiteImageViewer.Services;

namespace LiteImageViewer;

/// <summary>
/// 应用程序入口
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 初始化主题（跟随系统或用户上次选择）
        ThemeService.Initialize();

        // 全局异常处理
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
    }

    /// <summary>
    /// UI 线程未处理异常
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show(
            "程序遇到一个错误: " + e.Exception.Message + "\n\n程序将继续运行。",
            "错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// 非 UI 线程未处理异常
    /// </summary>
    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MessageBox.Show(
                    "程序遇到严重错误: " + ex.Message + "\n\n建议保存工作并重启程序。",
                    "严重错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }));
        }
    }

    /// <summary>
    /// 异步任务未观察异常
    /// </summary>
    private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var msg = e.Exception?.InnerException?.Message ?? e.Exception?.Message ?? "未知错误";
            MessageBox.Show(
                "后台任务遇到错误: " + msg,
                "后台错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }));
    }
}
