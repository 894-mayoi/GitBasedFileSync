using Microsoft.Toolkit.Uwp.Notifications;
using Quartz;
using Quartz.Impl;
using Serilog;

namespace GitBasedFileSync;

public class TaskScheduler
{
    // ReSharper disable once InconsistentNaming
    private static readonly ILogger log = Log.Logger;

    private IScheduler? _scheduler;
    // private FileSystemWatcher configWatcher;

    public async Task Start()
    {
        var factory = new StdSchedulerFactory();
        _scheduler = await factory.GetScheduler();
        await _scheduler.Start();
        await LoadTasks();

        // 监听配置文件变化
        // configWatcher = new FileSystemWatcher(Path.GetDirectoryName(ConfigPath));
        // configWatcher.Filter = "tasks.json";
        // configWatcher.Changed += (s, e) => LoadTasks();
        // configWatcher.EnableRaisingEvents = true;
    }

    private async Task LoadTasks()
    {
        var tasks = Config.LoadTasks();
        foreach (var task in tasks)
        {
            var job = JobBuilder.Create<CommandJob>()
                .WithIdentity(task.Name)
                .UsingJobData("path", task.Path)
                .UsingJobData("repo", task.Repo)
                .Build();
            var trigger = TriggerBuilder.Create()
                .WithIdentity(task.Name + "_trigger")
                .WithCronSchedule(task.Cron)
                .Build();
            await _scheduler!.ScheduleJob(job, trigger);
            log.Information("已加载任务: {Name}，路径: {Path}，仓库: {Repo}，Cron: {Cron}", task.Name, task.Path, task.Repo,
                task.Cron);
        }
    }

    public async Task<bool> HasRunning()
    {
        if (_scheduler == null) return false;
        var currentlyExecutingJobs = await _scheduler.GetCurrentlyExecutingJobs();
        return currentlyExecutingJobs.Count > 0;
    }

    public async Task Stop()
    {
        if (_scheduler == null) return;
        await _scheduler.Shutdown();
        _scheduler = null;
        log.Information("任务调度器已停止");
        // configWatcher?.Dispose();
    }
}

public class CommandJob : IJob
{
    // ReSharper disable once InconsistentNaming
    private static readonly ILogger log = Log.Logger;

    public async Task Execute(IJobExecutionContext context)
    {
        var name = context.JobDetail.Key.Name;
        try
        {
            log.Information("开始执行{name}", name);
            var path = context.JobDetail.JobDataMap.GetString("path");

            await Task.Delay(5000); // 模拟执行任务的延迟

            log.Information("{name}执行成功", name);
        }
        catch (Exception e)
        {
            log.Error(e, "任务{name}执行失败: {message}", name, e.Message);
            // 显示通知
            new ToastContentBuilder()
                .AddArgument("action", "viewConversation")
                .AddArgument("conversationId", name)
                .AddText("Task Execution Failed")
                .AddText($"任务{name}执行失败: {e.Message}")
                .Show();
        }
    }
}