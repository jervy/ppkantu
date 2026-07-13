using System.Windows;
using System.Windows.Controls;
using LiteImageViewer.ViewModels;

namespace LiteImageViewer.Views;

/// <summary>
/// 工具栏控件
/// </summary>
public partial class ToolbarControl : UserControl
{
    public ToolbarControl()
    {
        InitializeComponent();
    }

    private ImageViewerControl? FindImageViewer()
    {
        var win = Window.GetWindow(this) as MainWindow;
        return win?.FindName("ImageViewer") as ImageViewerControl;
    }

    private void OnStartCropClick(object sender, RoutedEventArgs e)
    {
        if (FindImageViewer() is { } viewer)
        {
            viewer.BeginCropModeFromToolbar();
            return;
        }

        if (DataContext is MainViewModel vm)
            vm.StartCrop();
    }

    private void OnStartDoodleClick(object sender, RoutedEventArgs e)
    {
        if (FindImageViewer() is { } viewer)
        {
            viewer.BeginDoodleModeFromToolbar();
            return;
        }

        if (DataContext is MainViewModel vm)
            vm.StartDoodleCommand.Execute(null);
    }

    private async void OnConfirmCropClick(object sender, RoutedEventArgs e)
    {
        var viewer = FindImageViewer();
        if (viewer != null)
            await viewer.ConfirmCropAsync();
    }

    private async void OnConfirmAnnotationClick(object sender, RoutedEventArgs e)
    {
        var viewer = FindImageViewer();
        if (viewer != null)
            await viewer.ConfirmAnnotationAsync();
    }
}
