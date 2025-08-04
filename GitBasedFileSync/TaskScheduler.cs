using System.Text.Json;
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
            var (exitCode, _, _) = Util.ExecuteGitCommand(task.Path, ["status", "--porcelain"]);
            // 启动时本地仓库不存在则立即初始化
            if (exitCode != 0)
            {
                log.Information("任务{Name}的本地仓库不存在，正在初始化...", task.Name);
                Util.ExecuteGitCommand(task.Path, ["init"], true);
                Util.ExecuteGitCommand(task.Path, ["remote", "add", "origin", task.Repo], true);
                // 设置.gitignore
                CommandJob.SetupGitIgnore(task.Path, task.Ignore);
                // 设置Git LFS
                if (task.Lfs.Count > 0) CommandJob.SetupGitLfs(task.Path, task.Lfs);

                Util.ExecuteGitCommand(task.Path, ["add", "."], true);
                var currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                Util.ExecuteGitCommand(task.Path, ["commit", "-m", $"\"初始化同步 {currentTime}\""], true);
                Util.ExecuteGitCommand(task.Path, ["push", "-u", "origin", "master"], true);
                log.Information("任务{Name}已成功初始化并推送git仓库", task.Name);
            }

            var job = JobBuilder.Create<CommandJob>()
                .WithIdentity(task.Name)
                .UsingJobData("path", task.Path)
                .UsingJobData("repo", task.Repo)
                .UsingJobData("ignore", string.Join(",", task.Ignore))
                .UsingJobData("lfs", string.Join(",", task.Lfs))
                .Build();
            var trigger = TriggerBuilder.Create()
                .WithIdentity(task.Name + "_trigger")
                .WithCronSchedule(task.Cron)
                .StartNow()
                .ForJob(job.Key)
                .Build();
            await _scheduler!.ScheduleJob(job, trigger);
            log.Information("已加载任务: {Name}，路径: {Path}，仓库: {Repo}，Cron: {Cron}, 忽略文件: {Ignore}, LFS托管文件: {LFS}",
                task.Name, task.Path, task.Repo,
                task.Cron, JsonSerializer.Serialize(task.Ignore), JsonSerializer.Serialize(task.Lfs));
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
            var path = context.JobDetail.JobDataMap.GetString("path")!;
            var repo = context.JobDetail.JobDataMap.GetString("repo")!;
            var ignore = context.JobDetail.JobDataMap.GetString("ignore")?.Split(",").ToList() ?? [];
            var lfs = context.JobDetail.JobDataMap.GetString("lfs")?.Split(",").ToList() ?? [];

            var (exitCode, _, _) = Util.ExecuteGitCommand(path, ["status", "--porcelain"]);
            // 定时任务执行的时候本地仓库还不存在，说明被手动删了，直接报错
            if (exitCode != 0)
            {
                log.Error("任务{name}的本地仓库不存在，请手动恢复", name);
                throw new Exception($"任务{name}的本地仓库不存在，请手动恢复");
            }

            // 同步远程仓库，如果是多端同步可能有冲突会报错，就只能手动解决
            Util.ExecuteGitCommand(path, ["pull", "origin", "master"], true);

            // 如果忽略规则有变化，更新.gitignore文件
            var gitIgnoreFile = Path.Combine(path, ".gitignore");
            var currentIgnore = (await File.ReadAllLinesAsync(gitIgnoreFile)).ToList();
            if (currentIgnore.Count != ignore.Count || !currentIgnore.Order().SequenceEqual(ignore.Order()))
            {
                SetupGitIgnore(path, ignore);
            }

            var (_, output, _) = Util.ExecuteGitCommand(path, ["status", "--porcelain"], true);
            if (string.IsNullOrWhiteSpace(output))
            {
                log.Information("任务{name}下的内容没有发生更改", name);
                log.Information("任务{name}执行完成", name);
                return; // 没有更改，直接返回
            }

            Util.ExecuteGitCommand(path, ["add", "."], true);
            var currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Util.ExecuteGitCommand(path, ["commit", "-m", $"\"自动同步 {currentTime}\""], true);
            Util.ExecuteGitCommand(path, ["push", "origin", "master"], true);

            log.Information("任务{name}自动同步成功", name);
            log.Information("任务{name}执行完成", name);
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

    public static void SetupGitIgnore(string repoPath, List<string> ignorePatterns)
    {
        // 1. .gitignore 文件路径
        var gitIgnorePath = Path.Combine(repoPath, ".gitignore");

        try
        {
            // 2. 将忽略模式写入文件
            if (ignorePatterns.Count > 0)
            {
                File.WriteAllLines(gitIgnorePath, ignorePatterns);
                log.Information(".gitignore 文件已创建: {GitIgnorePath}", gitIgnorePath);
            }
            else
            {
                // 即使没有忽略规则也创建空文件
                File.WriteAllText(gitIgnorePath, string.Empty);
                log.Information("创建了空的 .gitignore 文件");
            }

            // 3. 提交并推送 .gitignore 文件
            Util.ExecuteGitCommand(repoPath, ["add", ".gitignore"], true);
            Util.ExecuteGitCommand(repoPath, ["commit", "-m", "\"更新 .gitignore\""], true);
            Util.ExecuteGitCommand(repoPath, ["push", "origin", "master"], true);
            log.Information("已提交并推送 .gitignore 文件到远程仓库: {RepoPath}", repoPath);
        }
        catch (Exception ex)
        {
            log.Error(ex, "创建 .gitignore 文件失败: {ExMessage}", ex.Message);
            throw;
        }
    }

    public static void SetupGitLfs(string repoPath, List<string> lfsPatterns)
    {
        // 1. 初始化 Git LFS
        var (exitCode, _, error) = Util.ExecuteGitCommand(repoPath, ["lfs", "install"]);
        if (exitCode != 0)
        {
            log.Error("初始化 Git LFS 失败: {Error}", error);
            throw new Exception($"初始化 Git LFS 失败：{error}");
        }

        // 2. 添加 LFS 跟踪规则
        foreach (var pattern in lfsPatterns)
        {
            (exitCode, _, error) = Util.ExecuteGitCommand(repoPath, ["lfs", "track", pattern]);
            // ReSharper disable once InvertIf
            if (exitCode != 0)
            {
                log.Error("添加 LFS 跟踪规则失败: {Error}", error);
                throw new Exception($"添加 LFS 跟踪规则失败：{error}");
            }
        }

        // 3. 提交并推送 .gitattributes 文件
        Util.ExecuteGitCommand(repoPath, ["add", ".gitattributes"],true);
        Util.ExecuteGitCommand(repoPath, ["commit", "-m", "\"更新 LFS 跟踪规则\""], true);
        Util.ExecuteGitCommand(repoPath, ["push", "origin", "master"], true);
        log.Information("已提交并推送 LFS 跟踪规则到远程仓库: {RepoPath}", repoPath);
    }
}