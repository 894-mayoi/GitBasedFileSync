using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Quartz;
using Quartz.Impl;
using Serilog;

namespace GitBasedFileSync;

public class TaskScheduler
{
    // ReSharper disable once InconsistentNaming
    private static readonly ILogger log = Log.Logger;

    private IScheduler? _scheduler;

    public async Task Start()
    {
        var factory = new StdSchedulerFactory();
        _scheduler = await factory.GetScheduler();
        await _scheduler.Start();
        await LoadTasks();
    }

    private async Task LoadTasks()
    {
        var tasks = Config.LoadTasks();
        foreach (var task in tasks)
        {
            InitTask(task);
            var job = JobBuilder.Create<CommandJob>()
                .WithIdentity(task.Name)
                .UsingJobData("path", task.Path)
                .UsingJobData("notifyWhenSuccess", task.NotifyWhenSuccess)
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

    [SuppressMessage("Performance", "CA1822:将成员标记为 static")]
    private void InitTask(TaskInfo task)
    {
        var (exitCode, _, _) = Util.ExecuteGitCommand(task.Path, ["status", "--porcelain"]);
        if (exitCode != 0)
        {
            // 启动时本地仓库不存在则立即初始化
            log.Information("任务{Name}的本地仓库不存在，正在初始化...", task.Name);
            Util.WindowsNotify("任务初始化", $"任务{task.Name}的正在初始化...");
            Util.ExecuteGitCommand(task.Path, ["init"], true);

            Util.ExecuteGitCommand(task.Path, ["remote", "add", "origin", task.Repo], true);
            var (code, output, error) = Util.ExecuteGitCommand(task.Path, ["pull", "origin", "master"]);
            if (code != 0)
            {
                if (output.Contains("couldn't find remote ref master") ||
                    error.Contains("couldn't find remote ref master"))
                    log.Information("任务{Name}的远程仓库为新仓库", task.Name);
                else
                    throw new InitException($"任务{task.Name}的远程仓库拉取失败，请检查。\n错误信息： {output} {error}");
            }
            else
            {
                log.Information("任务{Name}的远程仓库已成功拉取", task.Name);
            }

            // 设置.gitignore
            CommandJob.SetupGitIgnore(task.Path, task.Ignore.ToHashSet());
            // 设置Git LFS
            if (task.Lfs.Count > 0)
                CommandJob.SetupGitLfs(task.Path, task.Lfs);

            Util.ExecuteGitCommand(task.Path, ["add", "."], true);
            (_, output, _) = Util.ExecuteGitCommand(task.Path, ["status", "--porcelain"], true);
            if (string.IsNullOrWhiteSpace(output))
            {
                log.Information("任务{Name}已成功初始化，没有需要提交的内容", task.Name);
                Util.WindowsNotify("任务初始化", $"任务{task.Name}已成功初始化，没有需要同步的内容");
            }
            else
            {
                var currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                Util.ExecuteGitCommand(task.Path, ["commit", "-m", $"\"初始化同步 {currentTime}\""], true);
                Util.ExecuteGitCommand(task.Path, ["push", "-u", "origin", "master"], true);
                log.Information("任务{Name}已成功初始化并推送git仓库", task.Name);
                Util.WindowsNotify("任务初始化", $"任务{task.Name}已成功初始化并同步");
            }
        }
        else
        {
            // 启动时更新gitignore和lfs规则
            var gitIgnoreFile = Path.Combine(task.Path, ".gitignore");
            var currentIgnore = File.ReadAllLines(gitIgnoreFile).ToHashSet();
            if (currentIgnore.Count != task.Ignore.Count || !currentIgnore.SetEquals(task.Ignore))
                CommandJob.SetupGitIgnore(task.Path, task.Ignore);

            // lfs规则直接添加，不考虑减少规则的情况
            if (task.Lfs.Count > 0)
                CommandJob.SetupGitLfs(task.Path, task.Lfs);
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
            var notifyWhenSuccess = context.JobDetail.JobDataMap.GetBoolean("notifyWhenSuccess");

            var (exitCode, _, _) = Util.ExecuteGitCommand(path, ["status", "--porcelain"]);
            // 定时任务执行的时候本地仓库还不存在，说明被手动删了，直接报错
            if (exitCode != 0)
            {
                log.Error("任务{name}的本地仓库不存在，请手动恢复", name);
                throw new Exception($"任务{name}的本地仓库不存在，请手动恢复");
            }

            // 同步远程仓库，如果是多端同步可能有冲突会报错，就只能手动解决
            Util.ExecuteGitCommand(path, ["pull", "origin", "master"], true);

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
            if (notifyWhenSuccess)
                Util.WindowsNotify("同步成功", $"任务{name}同步执行成功");
        }
        catch (Exception e)
        {
            log.Error(e, "任务{name}执行失败: {message}", name, e.Message);
            Util.WindowsNotify("同步失败", $"任务{name}执行失败: {e.Message}");
        }
    }

    public static void SetupGitIgnore(string repoPath, HashSet<string> ignorePatterns)
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
            var (_, output, _) = Util.ExecuteGitCommand(repoPath, ["status", "--porcelain"], true);
            if (string.IsNullOrWhiteSpace(output))
                return;

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

    public static void SetupGitLfs(string repoPath, HashSet<string> lfsPatterns)
    {
        // 1. 初始化 Git LFS
        var (exitCode, _, error) = Util.ExecuteGitCommand(repoPath, ["lfs", "install"]);
        if (exitCode != 0)
        {
            log.Error("初始化 Git LFS 失败: {Error}", error);
            throw new Exception($"初始化 Git LFS 失败：{error}");
        }

        var flag = false;
        // 2. 添加 LFS 跟踪规则
        foreach (var pattern in lfsPatterns.Where(pattern => !string.IsNullOrWhiteSpace(pattern)))
        {
            (exitCode, var output, error) = Util.ExecuteGitCommand(repoPath, ["lfs", "track", pattern]);
            if (exitCode != 0)
            {
                log.Error("添加 LFS 跟踪规则失败: {Error}", error);
                throw new Exception($"添加 LFS 跟踪规则失败：{error}");
            }

            // 如果是已经存在的规则，输出会包含 "xxx already supported"
            if (!output.Contains("already supported"))
                flag = true;
        }

        // 3. 提交并推送 .gitattributes 文件
        // ReSharper disable once InvertIf
        if (flag)
        {
            Util.ExecuteGitCommand(repoPath, ["add", ".gitattributes"], true);
            Util.ExecuteGitCommand(repoPath, ["commit", "-m", "\"更新 LFS 跟踪规则\""], true);
            Util.ExecuteGitCommand(repoPath, ["push", "origin", "master"], true);
            log.Information("已提交并推送 LFS 跟踪规则到远程仓库: {RepoPath}", repoPath);
        }
    }
}