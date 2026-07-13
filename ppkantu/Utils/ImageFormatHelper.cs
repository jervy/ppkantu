using System.IO;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;

namespace Ppkantu.Utils;

/// <summary>
/// 图片格式辅助类
/// </summary>
public static class ImageFormatHelper
{
    public static readonly string[] SaveSupportedExtensions =
    [
        ".jpg", ".jpeg", ".jpe", ".jfif",
        ".png",
        ".bmp", ".dib",
        ".gif",
        ".webp",
        ".tiff", ".tif"
    ];

    public static readonly string[] CompressSupportedExtensions =
    [
        ".jpg", ".jpeg", ".jpe", ".jfif",
        ".png",
        ".gif",
        ".webp",
        ".tiff", ".tif"
    ];

    private static readonly HashSet<string> SaveSupportedExtensionSet =
        new(SaveSupportedExtensions, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CompressSupportedExtensionSet =
        new(CompressSupportedExtensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 支持的图片扩展名
    /// </summary>
    public static readonly HashSet<string> SupportedExtensions =
        new(Config.AppSettings.SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 判断文件是否为支持的图片格式
    /// </summary>
    public static bool IsSupportedImage(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    /// <summary>
    /// 判断扩展名是否支持重新编码保存。
    /// </summary>
    public static bool IsSaveSupportedExtension(string extension)
    {
        return !string.IsNullOrWhiteSpace(extension) && SaveSupportedExtensionSet.Contains(extension);
    }

    /// <summary>
    /// 判断扩展名是否适合执行“保留原格式压缩”。
    /// BMP/DIB 虽可重新编码保存，但通常不会变小，DIB 还容易因 BMP/DIB 容器差异导致输出读取异常。
    /// </summary>
    public static bool IsCompressSupportedExtension(string extension)
    {
        return !string.IsNullOrWhiteSpace(extension) && CompressSupportedExtensionSet.Contains(extension);
    }

    /// <summary>
    /// 获取文件扩展名对应的 ImageSharp 编码器
    /// </summary>
    public static IImageEncoder GetEncoder(string extension, int jpegQuality = 90)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".jpe" or ".jfif" => new JpegEncoder { Quality = jpegQuality },
            ".png" => new PngEncoder(),
            ".bmp" or ".dib" => new BmpEncoder(),
            ".gif" => new GifEncoder(),
            ".webp" => new WebpEncoder(),
            ".tif" or ".tiff" => new TiffEncoder(),
            _ => throw new NotSupportedException($"不支持保存为 {extension} 格式。请另存为 JPG、PNG、BMP、GIF、WebP 或 TIFF。")
        };
    }

    /// <summary>
    /// 获取所有支持的文件过滤器字符串（用于文件对话框）
    /// </summary>
    public static string GetOpenFilter()
    {
        return "所有支持的图片|*.jpg;*.jpeg;*.jpe;*.jfif;*.png;*.bmp;*.dib;*.gif;*.webp;*.tiff;*.tif;*.ico;*.wdp;*.jxr;*.hdp" +
               "|JPEG 图片|*.jpg;*.jpeg;*.jpe;*.jfif" +
               "|PNG 图片|*.png" +
               "|BMP 图片|*.bmp;*.dib" +
               "|GIF 图片|*.gif" +
               "|WebP 图片|*.webp" +
               "|TIFF 图片|*.tiff;*.tif" +
               "|图标文件|*.ico" +
               "|JPEG XR 图片|*.wdp;*.jxr;*.hdp" +
               "|所有文件|*.*";
    }

    /// <summary>
    /// 获取保存对话框的文件过滤器
    /// </summary>
    public static string GetSaveFilter()
    {
        return "JPEG 图片|*.jpg;*.jpeg;*.jpe;*.jfif" +
               "|PNG 图片|*.png" +
               "|BMP 图片|*.bmp;*.dib" +
               "|GIF 图片|*.gif" +
               "|WebP 图片|*.webp" +
               "|TIFF 图片|*.tif;*.tiff";
    }
}
