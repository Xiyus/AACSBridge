using Godot;
using MegaCrit.Sts2.Core.Logging;
// Godot 也导出 Logger / Environment 类型；这里显式别名/限定游戏版本。
using GameLogger = MegaCrit.Sts2.Core.Logging.Logger;

namespace AutoAnthonyCombatSolverBridge.Diagnostics;

/// <summary>
/// 桥的统一日志入口。使用游戏自带的 Mod Logger（与空模板一致），输出进入 StS2 正常日志/控制台；
/// 另外把 <see cref="Dump"/> 行镜像写入独立文件，验证里程碑时无需翻完整游戏日志。
/// </summary>
public static class BridgeLog
{
    private const string Prefix = "[AA-CS Bridge]";

    private static GameLogger? _logger;
    private static readonly object DumpFileLock = new();
    private static string? _dumpFilePath;
    private static bool _dumpFileBroken;

    private static GameLogger Log => _logger ??= new GameLogger(MainFile.ModId, LogType.Generic);

    public static void Info(string message) => Log.Info($"{Prefix} {message}");

    public static void Warn(string message) => Log.Warn($"{Prefix} {message}");

    public static void Error(string message) => Log.Error($"{Prefix} {message}");

    /// <summary>
    /// 里程碑 ③/④ 输出：结构化 RuntimeSpec 转储。走游戏 Logger，并尽力追加到
    /// &lt;用户数据&gt;/AutoAnthonyCombatSolverBridge/spec-dump.log。
    /// </summary>
    public static void Dump(string message)
    {
        Info(message);
        TryAppendDumpFile(message);
    }

    public static string? DumpFilePath => _dumpFilePath;

    private static void TryAppendDumpFile(string message)
    {
        if (_dumpFileBroken) return;
        try
        {
            lock (DumpFileLock)
            {
                _dumpFilePath ??= Path.Combine(
                    OS.GetUserDataDir(), "AutoAnthonyCombatSolverBridge", "spec-dump.log");
                var directory = Path.GetDirectoryName(_dumpFilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.AppendAllText(_dumpFilePath, message + System.Environment.NewLine);
            }
        }
        catch (Exception exception)
        {
            // 文件落盘只是便利功能——绝不让 IO 问题影响游戏或转储本身。
            _dumpFileBroken = true;
            Log.Warn($"{Prefix} spec-dump.log 不可用（{exception.GetType().Name}: {exception.Message}）；仅继续使用游戏日志。");
        }
    }
}
