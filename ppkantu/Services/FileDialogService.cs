using Microsoft.Win32;
using Ppkantu.Utils;

namespace Ppkantu.Services;

/// <summary>
/// 文件对话框服务
/// </summary>
public class FileDialogService
{
    /// <summary>
    /// 打开文件对话框，选择图片
    /// </summary>
    public string? OpenImageFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择图片文件",
            Filter = ImageFormatHelper.GetOpenFilter(),
            FilterIndex = 1,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// 保存文件对话框
    /// </summary>
    public string? SaveImageFile(string? defaultFileName = null, string? sourceFilePath = null)
    {
        // 根据原图格式选择默认过滤器，并显式补默认扩展名。
        // 只传不带扩展名的 FileName 时，部分情况下 SaveFileDialog 会按第一个过滤器补 .jpg，
        // 导致 PNG/WebP/TIFF 等图片“另存为”仍默认变成 JPG。
        var filterIndex = 1; // 默认 JPEG
        var defaultExt = ".jpg";
        if (!string.IsNullOrEmpty(sourceFilePath))
        {
            var ext = System.IO.Path.GetExtension(sourceFilePath)?.ToLowerInvariant();
            (filterIndex, defaultExt) = ext switch
            {
                ".png" => (2, ".png"),
                ".bmp" or ".dib" => (3, ".bmp"),
                ".gif" => (4, ".gif"),
                ".webp" => (5, ".webp"),
                ".tif" or ".tiff" => (6, ".tif"),
                ".jpg" or ".jpeg" or ".jpe" or ".jfif" => (1, ".jpg"),
                _ => (1, ".jpg")
            };
        }

        var fileName = defaultFileName ?? "image";
        if (string.IsNullOrEmpty(System.IO.Path.GetExtension(fileName)))
            fileName += defaultExt;

        var dialog = new SaveFileDialog
        {
            Title = "另存为",
            Filter = ImageFormatHelper.GetSaveFilter(),
            FilterIndex = filterIndex,
            DefaultExt = defaultExt,
            AddExtension = true,
            FileName = fileName
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// 选择导出 TXT 文件路径
    /// </summary>
    public string? SaveTextFile(string? defaultFileName = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出文本",
            Filter = "文本文件|*.txt|所有文件|*.*",
            FilterIndex = 1,
            FileName = defaultFileName ?? "ocr_result.txt"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
