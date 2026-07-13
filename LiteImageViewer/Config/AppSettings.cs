using System.IO;
using System.Text.Json;

namespace LiteImageViewer.Config;

/// <summary>
/// 应用配置模型 — 支持便携模式（exe 目录优先，回退到 %LOCALAPPDATA%）
/// </summary>
public class AppSettings
{
    private const string ConfigFileName = "appsettings.json";
    private const string AppName = "ppkantu";

    /// <summary>
    /// 主题：Light / Dark
    /// </summary>
    public string Theme { get; set; } = "Light";

    /// <summary>
    /// 默认缩放模式：Fit（适应窗口）/ Original（原始大小）
    /// </summary>
    public string DefaultZoomMode { get; set; } = "Fit";

    /// <summary>
    /// JPEG 保存质量 (1-100)
    /// </summary>
    public int JpegQuality { get; set; } = 90;

    /// <summary>
    /// OCR 提供者：Mock / Api
    /// </summary>
    public string OcrProvider { get; set; } = "Mock";

    /// <summary>
    /// OCR API 地址
    /// </summary>
    public string OcrApiEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// OCR API 密钥（不要硬编码，从配置或环境变量读取）
    /// </summary>
    public string OcrApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 支持的图片扩展名
    /// </summary>
    public static readonly string[] SupportedExtensions =
    [
        ".jpg", ".jpeg", ".jpe", ".jfif",
        ".png",
        ".bmp", ".dib",
        ".gif",
        ".webp",
        ".tiff", ".tif",
        ".ico",
        ".wdp", ".jxr", ".hdp"
    ];

    // ──────────────────────────────────────────────────────
    // 便携模式路径解析
    // 优先级：exe 目录/Data/appsettings.json > exe 目录/appsettings.json > %LOCALAPPDATA%
    // ──────────────────────────────────────────────────────

    private static readonly Lazy<string> _exeDir = new(() => AppContext.BaseDirectory);

    /// <summary>
    /// 可执行文件所在目录
    /// </summary>
    private static string ExeDir => _exeDir.Value;

    /// <summary>
    /// 候选配置目录列表（按优先级排列）
    /// </summary>
    private static readonly string[] CandidateConfigDirs =
    [
        Path.Combine(ExeDir, "Data"),
        ExeDir,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName)
    ];

    /// <summary>
    /// 返回实际使用的配置目录（第一个已有配置文件的目录，或回退到 %LOCALAPPDATA%）
    /// </summary>
    private static string ResolveConfigDir()
    {
        foreach (var dir in CandidateConfigDirs)
        {
            if (File.Exists(Path.Combine(dir, ConfigFileName)))
                return dir;
        }
        // 没有找到已有配置，回退到 %LOCALAPPDATA%（标准安装模式）
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>
    /// 便携模式标志：如果 exe 目录下有配置文件，则为便携模式
    /// </summary>
    public static bool IsPortable { get; private set; }

    /// <summary>
    /// 获取配置文件路径（便携优先）
    /// </summary>
    public static string GetConfigPath()
    {
        var configDir = ResolveConfigDir();
        // 标记是否为便携模式（配置在 exe 同级或 Data 子目录下）
        IsPortable = configDir == ExeDir || configDir == Path.Combine(ExeDir, "Data");
        return Path.Combine(configDir, ConfigFileName);
    }

    /// <summary>
    /// 从文件加载配置，如果不存在则创建默认配置
    /// </summary>
    public static AppSettings Load()
    {
        var configPath = GetConfigPath();

        if (!File.Exists(configPath))
        {
            var defaultSettings = new AppSettings();
            defaultSettings.Save();
            return defaultSettings;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    public void Save()
    {
        try
        {
            var configPath = GetConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(configPath, json);
        }
        catch
        {
            // 静默处理保存失败
        }
    }

    /// <summary>
    /// 从环境变量读取敏感配置（API Key 等）
    /// </summary>
    public static AppSettings LoadWithEnvironmentOverrides()
    {
        var settings = Load();

        var envApiKey = Environment.GetEnvironmentVariable("OCR_API_KEY");
        if (!string.IsNullOrEmpty(envApiKey))
            settings.OcrApiKey = envApiKey;

        var envApiEndpoint = Environment.GetEnvironmentVariable("OCR_API_ENDPOINT");
        if (!string.IsNullOrEmpty(envApiEndpoint))
            settings.OcrApiEndpoint = envApiEndpoint;

        return settings;
    }
}
