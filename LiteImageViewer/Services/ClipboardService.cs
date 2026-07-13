using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LiteImageViewer.Services;

/// <summary>
/// 剪贴板服务
/// </summary>
public class ClipboardService
{
    /// <summary>
    /// 将 BitmapImage 复制到剪贴板
    /// </summary>
    public bool CopyImageToClipboard(BitmapImage image)
    {
        try
        {
            Clipboard.SetImage(image);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 将文件路径复制到剪贴板
    /// </summary>
    public bool CopyPathToClipboard(string filePath)
    {
        try
        {
            Clipboard.SetText(filePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 将文本复制到剪贴板
    /// </summary>
    public bool CopyTextToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查剪贴板中是否有图片
    /// </summary>
    public bool HasImage()
    {
        return Clipboard.ContainsImage();
    }

    /// <summary>
    /// 从剪贴板获取图片
    /// </summary>
    public BitmapSource? GetImageFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsImage())
                return Clipboard.GetImage();
            return null;
        }
        catch
        {
            return null;
        }
    }
}
