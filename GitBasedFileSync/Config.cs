using Hocon;
using Quartz;

namespace GitBasedFileSync;

public static class Config
{
    private const string ConfigFile = "app-settings.conf";

    public static List<TaskInfo> LoadTasks()
    {
        var taskInfos = new List<TaskInfo>();

        var hoconConfig = HoconConfigurationFactory.FromFile(ConfigFile);

        var tasks = hoconConfig.GetObjectList("app.tasks");
        if (tasks == null || !tasks.Any()) throw new InitException($"未找到任务配置，请检查{ConfigFile}文件内容。");
        var names = new HashSet<string>();
        foreach (var task in tasks)
        {
            var name = task.GetFieldOrNull("name")?.GetString();
            var path = task.GetFieldOrNull("path")?.GetString();
            var repo = task.GetFieldOrNull("repo")?.GetString();
            var cron = task.GetFieldOrNull("cron")?.GetString();
            var ignore = task.GetFieldOrNull("ignore")?.GetArray()?.Select(x => x.GetString()).ToList() ?? [];
            var lfs = task.GetFieldOrNull("lfs")?.GetArray()?.Select(x => x.GetString()).ToList() ?? [];

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(repo) ||
                string.IsNullOrEmpty(cron)) throw new InitException($"任务配置不完整，请检查{ConfigFile}文件内容。\n{task}");
            if (!names.Add(name)) throw new InitException($"任务名称{name}重复，请检查{ConfigFile}文件内容。");

            var (isValid, errorMessage) = Util.ValidatePath(path);
            if (!isValid) throw new InitException($"任务{name}中路径不正确：{errorMessage}。请检查{ConfigFile}文件内容。");

            if (!CronExpression.IsValidExpression(cron))
                throw new InitException($"任务{name}中的Cron表达式不正确，请检查{ConfigFile}文件内容。");

            taskInfos.Add(new TaskInfo
            {
                Name = name,
                Path = Path.GetFullPath(path),
                Repo = repo,
                Cron = cron,
                Ignore = ignore,
                Lfs = lfs
            });
        }

        return taskInfos;
    }

    private static HoconField? GetFieldOrNull(this HoconObject obj, string key)
    {
        try
        {
            return obj.GetField(key);
        }
        catch (Exception e)
        {
            return null;
        }
    }
    
}

public class TaskInfo
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Repo { get; init; }
    public required string Cron { get; init; }
    public required List<string> Ignore { get; init; }
    public required List<string> Lfs { get; init; }
}