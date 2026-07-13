namespace Ppkantu.Utils;

/// <summary>
/// 文件大小格式化工具
/// </summary>
public static class FileSizeFormatter
{
    /// <summary>
    /// 将字节大小格式化为可读字符串
    /// </summary>
    public static string Format(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
