using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Ppkantu.Services;

/// <summary>
/// 主题管理服务 — 跟随系统 + 手动切换深色/浅色
/// </summary>
public static class ThemeService
{
    private const string LightThemePath = "Resources/LightTheme.xaml";
    private const string DarkThemePath = "Resources/DarkTheme.xaml";
    private const string ThemeKeyName = "ThemeMode"; // 0=system, 1=light, 2=dark
    private const string SettingsFileName = "theme_settings.txt";

    /// <summary>
    /// 当前实际主题状态（Initialize/Toggle 后更新）
    /// </summary>
    public static bool CurrentIsDark { get; private set; }

    /// <summary>
    /// 应用启动时初始化主题（读取用户上次选择或跟随系统）
    /// </summary>
    public static bool Initialize()
    {
        var mode = LoadThemeMode();
        bool isDark;
        if (mode == 0) // 跟随系统
            isDark = IsSystemDarkMode();
        else
            isDark = mode == 2;

        ApplyTheme(isDark);
        CurrentIsDark = isDark;
        return isDark;
    }

    /// <summary>
    /// 切换主题并返回新的 isDark 状态
    /// </summary>
    public static bool Toggle(bool currentIsDark)
    {
        var newIsDark = !currentIsDark;
        ApplyTheme(newIsDark);
        SaveThemeMode(newIsDark ? 2 : 1);
        CurrentIsDark = newIsDark;
        return newIsDark;
    }

    /// <summary>
    /// 设置为跟随系统
    /// </summary>
    public static bool FollowSystem()
    {
        var isDark = IsSystemDarkMode();
        ApplyTheme(isDark);
        SaveThemeMode(0);
        return isDark;
    }

    /// <summary>
    /// 检测当前系统是否为深色模式
    /// </summary>
    public static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 0;
        }
        catch { }
        return false; // 默认浅色
    }

    private static void ApplyTheme(bool isDark)
    {
        var app = Application.Current;
        if (app == null) return;

        var dicts = app.Resources.MergedDictionaries;

        // 移除旧主题
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            if (dicts[i].Source != null &&
                (dicts[i].Source.OriginalString.Contains("LightTheme") ||
                 dicts[i].Source.OriginalString.Contains("DarkTheme")))
            {
                dicts.RemoveAt(i);
            }
        }

        // 加载新主题
        var themePath = isDark ? DarkThemePath : LightThemePath;
        var dict = new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) };
        dicts.Insert(0, dict);
    }

    private static int LoadThemeMode()
    {
        try
        {
            var path = GetSettingsPath();
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path).Trim();
                if (int.TryParse(text, out var mode) && mode >= 0 && mode <= 2)
                    return mode;
            }
        }
        catch { }
        return 0; // 默认跟随系统
    }

    private static void SaveThemeMode(int mode)
    {
        try
        {
            File.WriteAllText(GetSettingsPath(), mode.ToString());
        }
        catch { }
    }

    private static string GetSettingsPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ppkantu");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, SettingsFileName);
    }
}
