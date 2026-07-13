using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Ppkantu.Services;

/// <summary>
/// 文件关联服务 — 使用 HKCU 注册 ppkantu，并尽量把支持的图片扩展名切到本程序。
/// 注意：Windows 10/11 对“默认应用”有 UserChoice Hash 保护，第三方程序不能稳定/合规地直接写入默认应用。
/// 本服务会注册 ProgId、OpenWith 列表、应用能力，并在可行时清理当前用户的 UserChoice，让系统回退到本程序关联。
/// </summary>
public static class FileAssociationService
{
    private const string ProgId = "ppkantu.ImageFile";
    private const string AppName = "ppkantu";
    private const string ExecutableIdentity = "ppkantu.exe";
    private const string ApplicationsKey =
        @"Software\Classes\Applications\ppkantu.exe";
    private const string LegacyApplicationsKey =
        @"Software\Classes\Applications\鹏鹏看图.exe";
    private const string RegisteredAppKey = $@"Software\RegisteredApplications";
    private const string AppCapabilitiesKey = $@"Software\{AppName}\Capabilities";

    private static readonly string[] ImageExtensions = Config.AppSettings.SupportedExtensions;

    private static string ExePath => Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, ExecutableIdentity);

    /// <summary>
    /// 检查固定应用注册项是否存在。用于识别“用户改名后仍有旧关联”的情况。
    /// </summary>
    public static bool HasApplicationRegistration()
    {
        try
        {
            using var hkcu = Registry.CurrentUser;
            using var appKey = hkcu.OpenSubKey(ApplicationsKey);
            return appKey != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 如果固定注册项存在但路径已经变化，先清除本程序旧关联，再用当前路径重新注册。
    /// </summary>
    public static bool RepairAssociationIfNeeded()
    {
        if (!HasApplicationRegistration() || IsRegisteredForCurrentExecutable())
            return false;

        return Unassociate() && Associate();
    }

    /// <summary>
    /// 是否已关联所有支持的图片扩展名。
    /// 同时校验 ProgId 的 open command 是否指向当前 exe，避免绿色版目录改名/移动后误判为已关联。
    /// </summary>
    public static bool IsAssociated()
    {
        try
        {
            using var hkcu = Registry.CurrentUser;
            if (!IsCurrentExecutableRegistered(hkcu))
                return false;

            return ImageExtensions.All(ext => IsExtensionAssociated(hkcu, ext));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 是否已经把当前 exe 注册为 ppkantu 的打开命令。
    /// 这只代表“本程序已注册到系统/打开方式列表”，不代表所有扩展名都已成为 Windows 默认应用。
    /// </summary>
    public static bool IsRegisteredForCurrentExecutable()
    {
        try
        {
            using var hkcu = Registry.CurrentUser;
            return IsCurrentExecutableRegistered(hkcu);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 返回已被 Windows UserChoice 默认应用保护拦截、尚未默认指向 ppkantu 的扩展名。
    /// </summary>
    public static IReadOnlyList<string> GetUserChoiceBlockedExtensions()
    {
        var blocked = new List<string>();
        try
        {
            using var hkcu = Registry.CurrentUser;
            foreach (var ext in ImageExtensions)
            {
                using var userChoice = hkcu.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice");
                var userChoiceProgId = userChoice?.GetValue("ProgId") as string;
                if (!string.IsNullOrWhiteSpace(userChoiceProgId) &&
                    !string.Equals(userChoiceProgId, ProgId, StringComparison.OrdinalIgnoreCase))
                {
                    blocked.Add(ext);
                }
            }
        }
        catch { }

        return blocked;
    }

    /// <summary>
    /// 注册文件关联并尽量设为当前用户默认打开方式。
    /// </summary>
    public static bool Associate()
    {
        try
        {
            using var hkcu = Registry.CurrentUser;

            RegisterProgId(hkcu);
            RegisterApplicationCapabilities(hkcu);
            RegisterExtensions(hkcu);
            TryClearProtectedUserChoice(hkcu);
            NotifyShellChange();

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"文件关联注册失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 取消 ppkantu 写入的关联信息。
    /// </summary>
    public static bool Unassociate()
    {
        try
        {
            using var hkcu = Registry.CurrentUser;

            foreach (var ext in ImageExtensions)
            {
                using (var extKey = hkcu.OpenSubKey($@"Software\Classes\{ext}", writable: true))
                {
                    if (extKey != null)
                    {
                        if (string.Equals(extKey.GetValue(string.Empty) as string, ProgId, StringComparison.OrdinalIgnoreCase))
                            extKey.DeleteValue(string.Empty, throwOnMissingValue: false);

                        using var openWithProgIds = extKey.OpenSubKey("OpenWithProgids", writable: true);
                        openWithProgIds?.DeleteValue(ProgId, throwOnMissingValue: false);
                    }
                }

                using (var userChoice = hkcu.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice"))
                {
                    var current = userChoice?.GetValue("ProgId") as string;
                    if (string.Equals(current, ProgId, StringComparison.OrdinalIgnoreCase))
                        hkcu.DeleteSubKeyTree($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice", throwOnMissingSubKey: false);
                }
            }

            hkcu.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
            hkcu.DeleteSubKeyTree($@"Software\{AppName}", throwOnMissingSubKey: false);
            hkcu.DeleteSubKeyTree(ApplicationsKey, throwOnMissingSubKey: false);
            hkcu.DeleteSubKeyTree(LegacyApplicationsKey, throwOnMissingSubKey: false);

            using (var registeredApps = hkcu.OpenSubKey(RegisteredAppKey, writable: true))
                registeredApps?.DeleteValue(AppName, throwOnMissingValue: false);

            NotifyShellChange();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"文件关联移除失败: {ex.Message}");
            return false;
        }
    }

    private static void RegisterProgId(RegistryKey hkcu)
    {
        using var progKey = hkcu.CreateSubKey($@"Software\Classes\{ProgId}");
        progKey.SetValue(null, "鹏鹏看图 图片文件");
        progKey.SetValue("FriendlyTypeName", "鹏鹏看图 图片文件");

        using var iconKey = progKey.CreateSubKey("DefaultIcon");
        iconKey.SetValue(null, $"\"{ExePath}\",0");

        using var cmdKey = progKey.CreateSubKey(@"shell\open\command");
        cmdKey.SetValue(null, BuildOpenCommand(ExePath));
    }

    private static bool IsCurrentExecutableRegistered(RegistryKey hkcu)
    {
        using var cmdKey = hkcu.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        var registeredCommand = cmdKey?.GetValue(null) as string;
        var currentCommand = BuildOpenCommand(ExePath);
        return string.Equals(registeredCommand, currentCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExtensionAssociated(RegistryKey hkcu, string ext)
    {
        using (var userChoice = hkcu.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice"))
        {
            var userChoiceProgId = userChoice?.GetValue("ProgId") as string;
            if (!string.IsNullOrWhiteSpace(userChoiceProgId))
                return string.Equals(userChoiceProgId, ProgId, StringComparison.OrdinalIgnoreCase);
        }

        using var extKey = hkcu.OpenSubKey($@"Software\Classes\{ext}");
        var current = extKey?.GetValue(null) as string;
        if (string.Equals(current, ProgId, StringComparison.OrdinalIgnoreCase))
            return true;

        using var openWithProgIds = extKey?.OpenSubKey("OpenWithProgids");
        return openWithProgIds?.GetValueNames()
            .Any(name => string.Equals(name, ProgId, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string BuildOpenCommand(string exePath) => $"\"{exePath}\" \"%1\"";

    private static void RegisterApplicationCapabilities(RegistryKey hkcu)
    {
        using var capabilities = hkcu.CreateSubKey(AppCapabilitiesKey);
        capabilities.SetValue("ApplicationName", AppName);
        capabilities.SetValue("ApplicationDescription", "轻量、干净、无广告的办公图片查看与处理工具");
        // 设置应用图标，Windows 默认应用列表中显示
        capabilities.SetValue("ApplicationIcon", $"\"{ExePath}\",0");

        using var fileAssociations = capabilities.CreateSubKey("FileAssociations");
        foreach (var ext in ImageExtensions)
            fileAssociations.SetValue(ext, ProgId);

        using var registeredApps = hkcu.CreateSubKey(RegisteredAppKey);
        registeredApps.SetValue(AppName, AppCapabilitiesKey);

        hkcu.DeleteSubKeyTree(LegacyApplicationsKey, throwOnMissingSubKey: false);
        using var appKey = hkcu.CreateSubKey(ApplicationsKey);
        using var shellKey = appKey.CreateSubKey(@"shell\open\command");
        shellKey.SetValue(null, BuildOpenCommand(ExePath));
        using var iconKey = appKey.CreateSubKey("DefaultIcon");
        iconKey.SetValue(null, $"\"{ExePath}\",0");
    }

    private static void RegisterExtensions(RegistryKey hkcu)
    {
        foreach (var ext in ImageExtensions)
        {
            using var extKey = hkcu.CreateSubKey($@"Software\Classes\{ext}");
            extKey.SetValue(null, ProgId);

            using var openWithProgIds = extKey.CreateSubKey("OpenWithProgids");
            openWithProgIds.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }
    }

    private static void TryClearProtectedUserChoice(RegistryKey hkcu)
    {
        foreach (var ext in ImageExtensions)
        {
            try
            {
                hkcu.DeleteSubKeyTree($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice", throwOnMissingSubKey: false);
            }
            catch (Exception ex)
            {
                // Windows 会保护 UserChoice。删除失败时保留系统状态，不弹窗，只让状态栏提示由调用方处理。
                Debug.WriteLine($"清理 {ext} UserChoice 失败: {ex.Message}");
            }
        }
    }

    private static void NotifyShellChange()
    {
        try
        {
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
