using System.Diagnostics;
using Serilog;

namespace GitBasedFileSync;

internal static class Program
{
    public const string AppName = "GitBasedFileSync";

    // ReSharper disable once InconsistentNaming
    private static readonly ILogger log = Log.Logger;

    private static TaskScheduler? _scheduler;

    private static Mutex? _mutex;

    /// <summary>
    ///     The main entry point for the application.
    /// </summary>
    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();

        // 尝试创建互斥体
        _mutex = new Mutex(true, AppName, out var createdNew);

        if (!createdNew)
        {
            // 如果互斥体已经存在，说明程序已经在运行
            MessageBox.Show("程序已经运行，请查看系统托盘", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Application.Exit();
            return;
        }

        var trayIcon = new NotifyIcon();

        // 设置图标的属性
        trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        trayIcon.Text = AppName;
        trayIcon.Visible = true;

        // 添加右键菜单
        var trayMenu = new ContextMenuStrip();
        var showLogItem = new ToolStripMenuItem("查看日志", null, OpenLog_Click);
        var exitItem = new ToolStripMenuItem("退出", null, Exit_Click);
        trayMenu.Items.Add(showLogItem);
        trayMenu.Items.Add(exitItem);
        trayIcon.ContextMenuStrip = trayMenu;

        try
        {
            _scheduler = new TaskScheduler();
            // 启动 Quartz 调度器
            _scheduler.Start().Wait();
        }
        catch (Exception ex)
        {
            log.Error(ex, "An error occurred during application startup.");
            MessageBox.Show($"错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ReleaseResources();
            Application.Exit();
            return;
        }

        if (Config.AppSetting.EcoMode)
            try
            {
                EcoMode.EnableEfficiencyMode();
            }
            catch (Exception e)
            {
                MessageBox.Show($"错误: {e.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ReleaseResources();
                Application.Exit();
                return;
            }

        // 设置开机自启
        AutoStart.SetAutoStart(Config.AppSetting.AutoStart);

        Util.WindowsNotify("启动成功", $"{AppName} 已启动于系统托盘，任务调度器已准备就绪。");
        log.Information("Application startup.");
        Application.Run();
    }

    private static void OpenLog_Click(object? sender, EventArgs e)
    {
        try
        {
            if (!Directory.Exists(Log.LogDir))
            {
                MessageBox.Show("日志文件不存在。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = Log.LogDir,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to open log dir.");
            MessageBox.Show($"无法打开日志文件: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Exit_Click(object? sender, EventArgs e)
    {
        var dialogResult = MessageBox.Show($"确认退出{AppName}吗？", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (dialogResult != DialogResult.Yes) return;

        if (_scheduler?.HasRunning().Result == true)
        {
            MessageBox.Show("当前有同步任务正在运行，请稍等。", "无法退出", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ReleaseResources();

        log.Information("Application exit.");
        Application.Exit();
    }

    /// <summary>
    ///     释放资源
    /// </summary>
    private static void ReleaseResources()
    {
        try
        {
            _scheduler?.Stop().Wait();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch (Exception)
        {
            // ignore
        }
    }
}