using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Serilog;

namespace GitBasedFileSync;

/// <summary>
///     开启效率模式（Efficiency Mode）
/// </summary>
public partial class EcoMode
{
    // ReSharper disable once InconsistentNaming
    private const int PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;

    // ReSharper disable once InconsistentNaming
    private const int PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    private const int ProcessPowerThrottling = 4;

    // ReSharper disable once InconsistentNaming
    private const int IDLE_PRIORITY_CLASS = 0x00000040;

    // ReSharper disable once InconsistentNaming
    // ReSharper disable once UnusedMember.Local
    private const int NORMAL_PRIORITY_CLASS = 0x00000020;

    // ReSharper disable once InconsistentNaming
    // ReSharper disable once UnusedMember.Local
    private const int HIGH_PRIORITY_CLASS = 0x00000080;

    // ReSharper disable once InconsistentNaming
    // ReSharper disable once UnusedMember.Local
    private const int REALTIME_PRIORITY_CLASS = 0x00000100;

    // ReSharper disable once InconsistentNaming
    private static readonly ILogger log = Log.Logger;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private static partial bool SetProcessInformation(
        IntPtr hProcess,
        int ProcessInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE ProcessInformation,
        int ProcessInformationSize
    );

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetPriorityClass(
        IntPtr hProcess,
        int dwPriorityClass
    );

    public static void EnableEfficiencyMode()
    {
        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED
        };
        var hProcess = Process.GetCurrentProcess().Handle;

        var success = SetProcessInformation(
            hProcess,
            ProcessPowerThrottling,
            ref state,
            Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()
        );
        if (!success)
        {
            var error = Marshal.GetLastWin32Error();
            log.Error("设置效率模式失败，执行SetProcessInformation失败，错误代码: {Error}", error);
            throw new InitException($"设置效率模式失败，执行SetProcessInformation失败，错误代码: {error}");
        }

        success = SetPriorityClass(hProcess, IDLE_PRIORITY_CLASS);
        // ReSharper disable once InvertIf
        if (!success)
        {
            var error = Marshal.GetLastWin32Error();
            log.Error("设置效率模式失败，执行SetPriorityClass失败，错误代码: {Error}", error);
            throw new InitException($"设置效率模式失败，执行SetPriorityClass失败，错误代码: {error}");
        }
        log.Information("效率模式已启用，当前进程优先级已设置为低优先级。");
    }

    // ReSharper disable once InconsistentNaming
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }
}