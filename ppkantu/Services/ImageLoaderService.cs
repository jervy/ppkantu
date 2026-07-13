using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ppkantu.Models;
using Ppkantu.Utils;
using SixLabors.ImageSharp.Formats.Png;

namespace Ppkantu.Services;

/// <summary>
/// 图片加载服务
/// </summary>
public class ImageLoaderService
{
    /// <summary>
    /// 支持的图片扩展名（用于文件夹扫描）
    /// </summary>
    private static readonly HashSet<string> ImageExtensions =
        new(Config.AppSettings.SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 异步加载图片为 BitmapImage
    /// </summary>
    public async Task<BitmapImage?> LoadImageAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                return LoadWithWpfDecoder(filePath)
                    ?? LoadWithIcoDecoder(filePath)
                    ?? LoadWithDibDecoder(filePath)
                    ?? LoadWithImageSharpFallback(filePath);
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>
    /// 加载 GIF 文件用于动画显示（不冻结，保留帧信息）
    /// </summary>
    public System.Windows.Media.ImageSource? LoadGifForAnimation(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            // 不冻结 — WpfAnimatedGif 需要访问帧数据来播放动画
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取图片的尺寸信息
    /// </summary>
    public static (int Width, int Height)? GetImageDimensions(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                using var stream = File.OpenRead(filePath);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var frame = decoder.Frames.FirstOrDefault();
                if (frame != null)
                    return ((int)frame.PixelWidth, (int)frame.PixelHeight);
            }
            catch
            {
                // WPF 解码器不支持 WebP、部分 ICO/DIB 等格式时，继续尝试专用解析和 ImageSharp。
            }

            var ext = Path.GetExtension(filePath);
            if (string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase))
            {
                var icoDims = GetIcoDimensions(filePath);
                if (icoDims.HasValue) return icoDims;
            }

            if (string.Equals(ext, ".dib", StringComparison.OrdinalIgnoreCase))
            {
                var dibDims = GetDibDimensions(filePath);
                if (dibDims.HasValue) return dibDims;
            }

            var info = SixLabors.ImageSharp.Image.Identify(filePath);
            return info == null ? null : (info.Width, info.Height);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 扫描指定目录中的所有图片文件
    /// </summary>
    public List<ImageFileInfo> ScanDirectory(string directoryPath)
    {
        var result = new List<ImageFileInfo>();

        try
        {
            if (!Directory.Exists(directoryPath))
                return result;

            var files = Directory.GetFiles(directoryPath)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files)
            {
                var info = ImageFileInfo.FromFile(file);
                if (info != null)
                    result.Add(info);
            }
        }
        catch
        {
            // 静默处理目录扫描错误
        }

        return result;
    }

    private static BitmapImage? LoadWithWpfDecoder(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = fs;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? LoadWithIcoDecoder(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".ico", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var data = File.ReadAllBytes(filePath);
            if (data.Length < 6 || data[0] != 0 || data[1] != 0 || data[2] != 1 || data[3] != 0)
                return null;

            var count = BitConverter.ToUInt16(data, 4);
            if (count == 0 || data.Length < 6 + count * 16)
                return null;

            int bestOffset = 0;
            int bestSize = 0;
            int bestArea = -1;

            for (var i = 0; i < count; i++)
            {
                var entry = 6 + i * 16;
                var width = data[entry] == 0 ? 256 : data[entry];
                var height = data[entry + 1] == 0 ? 256 : data[entry + 1];
                var imageSize = BitConverter.ToInt32(data, entry + 8);
                var imageOffset = BitConverter.ToInt32(data, entry + 12);
                if (imageSize <= 0 || imageOffset < 0 || imageOffset + imageSize > data.Length)
                    continue;

                var area = width * height;
                if (area > bestArea)
                {
                    bestArea = area;
                    bestOffset = imageOffset;
                    bestSize = imageSize;
                }
            }

            if (bestSize <= 0)
                return null;

            using var ms = new MemoryStream(data, bestOffset, bestSize, writable: false);
            return BitmapImageFromStream(ms);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? LoadWithDibDecoder(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".dib", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var dib = File.ReadAllBytes(filePath);
            var headerSize = GetDibHeaderSize(dib);
            if (headerSize <= 0)
                return null;

            var pixelOffset = CalculateDibPixelOffset(dib, headerSize);
            if (pixelOffset <= 0 || pixelOffset > dib.Length)
                return null;

            using var bmp = new MemoryStream(dib.Length + 14);
            using (var writer = new BinaryWriter(bmp, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((byte)'B');
                writer.Write((byte)'M');
                writer.Write(dib.Length + 14);
                writer.Write((short)0);
                writer.Write((short)0);
                writer.Write(pixelOffset + 14);
                writer.Write(dib);
            }
            bmp.Position = 0;
            return BitmapImageFromStream(bmp);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? BitmapImageFromStream(Stream stream)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static (int Width, int Height)? GetIcoDimensions(string filePath)
    {
        try
        {
            var data = File.ReadAllBytes(filePath);
            if (data.Length < 6 || data[0] != 0 || data[1] != 0 || data[2] != 1 || data[3] != 0)
                return null;

            var count = BitConverter.ToUInt16(data, 4);
            int bestWidth = 0;
            int bestHeight = 0;
            int bestArea = -1;
            for (var i = 0; i < count && data.Length >= 6 + (i + 1) * 16; i++)
            {
                var entry = 6 + i * 16;
                var width = data[entry] == 0 ? 256 : data[entry];
                var height = data[entry + 1] == 0 ? 256 : data[entry + 1];
                var area = width * height;
                if (area > bestArea)
                {
                    bestArea = area;
                    bestWidth = width;
                    bestHeight = height;
                }
            }

            return bestArea < 0 ? null : (bestWidth, bestHeight);
        }
        catch
        {
            return null;
        }
    }

    private static (int Width, int Height)? GetDibDimensions(string filePath)
    {
        try
        {
            var data = File.ReadAllBytes(filePath);
            var headerSize = GetDibHeaderSize(data);
            if (headerSize < 12 || data.Length < headerSize)
                return null;

            if (headerSize == 12)
            {
                var width = BitConverter.ToUInt16(data, 4);
                var height = BitConverter.ToUInt16(data, 6);
                return (width, height);
            }

            var dibWidth = BitConverter.ToInt32(data, 4);
            var dibHeight = Math.Abs(BitConverter.ToInt32(data, 8));
            return dibWidth <= 0 || dibHeight <= 0 ? null : (dibWidth, dibHeight);
        }
        catch
        {
            return null;
        }
    }

    private static int GetDibHeaderSize(byte[] dib)
    {
        if (dib.Length < 4)
            return -1;

        var headerSize = BitConverter.ToInt32(dib, 0);
        return headerSize is 12 or 40 or 52 or 56 or 108 or 124 && dib.Length >= headerSize
            ? headerSize
            : -1;
    }

    private static int CalculateDibPixelOffset(byte[] dib, int headerSize)
    {
        if (headerSize == 12)
        {
            if (dib.Length < 12) return -1;
            var bitCount = BitConverter.ToUInt16(dib, 10);
            var colors = bitCount <= 8 ? 1 << bitCount : 0;
            return headerSize + colors * 3;
        }

        if (dib.Length < 36) return -1;
        var bitCountModern = BitConverter.ToUInt16(dib, 14);
        var compression = BitConverter.ToInt32(dib, 16);
        var colorsUsed = BitConverter.ToInt32(dib, 32);
        var paletteColors = colorsUsed > 0 ? colorsUsed : bitCountModern <= 8 ? 1 << bitCountModern : 0;
        var masksSize = (compression == 3 || compression == 6) && headerSize == 40 ? 12 : 0;
        return headerSize + masksSize + paletteColors * 4;
    }

    private static BitmapImage? LoadWithImageSharpFallback(string filePath)
    {
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load(filePath);
            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            ms.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
