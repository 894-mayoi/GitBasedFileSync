using Microsoft.Win32;
using Serilog;

namespace GitBasedFileSync;

public static class AutoStart
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // ReSharper disable once InconsistentNaming
    private static readonly ILogger log = Log.Logger;

    /// <summary>
    ///     设置或取消开机自启
    /// </summary>
    /// <param name="enable">true: 启用自启, false: 禁用自启</param>
    public static void SetAutoStart(bool enable)
    {
        const string registryKey = Program.AppName;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true)!;

            var oldValue = key.GetValue(registryKey) as string;

            if (enable)
            {
                var appPath = GetExecutablePath();

                if (appPath.Equals(oldValue)) return;

                // 设置注册表值
                key.SetValue(registryKey, $"\"{appPath}\"", RegistryValueKind.String);
                log.Information("设置开机自启成功: {AppPath}", appPath);
            }
            else
            {
                if (oldValue == null) return;
                // 删除注册表值
                key.DeleteValue(registryKey, false);
                log.Information("取消开机自启成功");
            }
        }
        catch (Exception e)
        {
            // 非致命错误，失败了使用通知提醒即可
            log.Error(e, "设置开机自启失败");
            Util.WindowsNotify("设置开机自启失败", $"设置开机自启失败：{e.Message}");
        }
    }

    /// <summary>
    ///     获取可执行文件路径
    /// </summary>
    private static string GetExecutablePath()
    {
        // 在 .NET 5+ 中使用 Environment.ProcessPath
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
            // 如果 ProcessPath 为空，使用当前目录和可执行文件名
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{Program.AppName}.exe");
        return path;
    }
}