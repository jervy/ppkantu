using System.IO;

namespace Ppkantu.Models;

/// <summary>
/// 图片文件信息模型
/// </summary>
public class ImageFileInfo
{
    /// <summary>
    /// 文件名（含扩展名）
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 完整文件路径
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 图片宽度（像素）
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 图片高度（像素）
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 图片格式
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// 文件创建时间
    /// </summary>
    public DateTime CreatedTime { get; set; }

    /// <summary>
    /// 文件修改时间
    /// </summary>
    public DateTime ModifiedTime { get; set; }

    /// <summary>
    /// 从文件路径创建实例
    /// </summary>
    public static ImageFileInfo? FromFile(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists) return null;

            return new ImageFileInfo
            {
                FileName = fileInfo.Name,
                FilePath = filePath,
                FileSize = fileInfo.Length,
                Format = fileInfo.Extension.TrimStart('.').ToUpperInvariant(),
                CreatedTime = fileInfo.CreationTime,
                ModifiedTime = fileInfo.LastWriteTime
            };
        }
        catch
        {
            return null;
        }
    }

    public override string ToString() => $"{FileName} ({Format}, {FileSize:N0} bytes)";
}
