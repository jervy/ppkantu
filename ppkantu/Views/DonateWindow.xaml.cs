using System.Windows;
using System.Windows.Input;

namespace Ppkantu.Views;

/// <summary>
/// 打赏弹窗
/// </summary>
public partial class DonateWindow : Window
{
    public DonateWindow()
    {
        InitializeComponent();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
