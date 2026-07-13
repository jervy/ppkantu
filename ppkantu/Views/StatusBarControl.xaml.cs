using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ppkantu.Views;

/// <summary>
/// 状态栏控件
/// </summary>
public partial class StatusBarControl : UserControl
{
    public StatusBarControl()
    {
        InitializeComponent();
    }

    private void DonateButton_Click(object sender, MouseButtonEventArgs e)
    {
        var window = new DonateWindow
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
    }

    private void DonateBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        DonateBorder.Background = (Brush)FindResource("ButtonHoverBgBrush");
    }

    private void DonateBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        DonateBorder.Background = Brushes.Transparent;
    }
}
