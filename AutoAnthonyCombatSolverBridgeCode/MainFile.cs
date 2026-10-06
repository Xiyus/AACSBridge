using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using AutoAnthonyCombatSolverBridge.Bootstrap;
using AutoAnthonyCombatSolverBridge.Diagnostics;

namespace AutoAnthonyCombatSolverBridge;

// 入口：复刻自 Alchyr.Sts2.Templates 的 "Empty Slay the Spire 2 Mod" 模板。
// 全部代码放在 AutoAnthonyCombatSolverBridgeCode；资源目录保持为空（纯代码桥：has_pck = false）。
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "AutoAnthonyCombatSolverBridge"; // 目前仅用于 Logger 与 Harmony id。

    public static void Initialize()
    {
        // Fail closed（失败即整体关闭）：兼容性自检通过之前，本 Mod 不应用任何 Harmony 补丁、
        // 不向 CombatSolver 注册任何镜像、不触碰两个 Mod。CombatSolver 的第三方适配规则明确
        // 要求"部分注册的桥比不注册更危险"，因此守卫失败时必须零 footprint。
        CompatibilityReport report;
        try
        {
            report = CompatibilityGuard.Run();
        }
        catch (Exception exception)
        {
            BridgeLog.Error($"兼容性自检崩溃，桥已禁用。{exception}");
            return;
        }

        if (!report.IsCompatible)
        {
            BridgeLog.Warn("桥已禁用——兼容性自检未通过。未应用任何补丁，未注册任何镜像。");
            foreach (var failure in report.Failures)
                BridgeLog.Warn($"  [guard] {failure}");
            BridgeLog.Warn("  游戏照常运行；只是 CombatSolver 继续把 AutoAnthony 视为未适配内容。");
            return;
        }

        try
        {
            // 到这里才安全触碰 AutoAnthony 类型：守卫已证明两个程序集均已加载且契约完好。
            // PatchAll 会应用本程序集内全部 [HarmonyPatch] 类（当前只有只读的 RuntimeSpecDumpPatch）。
            var harmony = new Harmony(ModId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            BridgeBootstrap.Initialize(report);
        }
        catch (Exception exception)
        {
            BridgeLog.Error($"守卫通过后桥初始化失败；转储补丁可能已部分应用。{exception}");
        }
    }
}
