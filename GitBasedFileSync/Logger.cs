using Serilog;

namespace GitBasedFileSync;

public static class Log
{
    public static readonly string LogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

    public static readonly ILogger Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.File(
            Path.Combine(LogDir, "app-.log"),
            fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB
            rollOnFileSizeLimit: true,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u4}] {Message}{NewLine}{Exception}"
        )
        .CreateLogger();
}