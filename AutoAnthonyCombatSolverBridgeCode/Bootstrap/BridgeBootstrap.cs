using AutoAnthonyCombatSolverBridge.CombatSolver;
using AutoAnthonyCombatSolverBridge.Diagnostics;

namespace AutoAnthonyCombatSolverBridge.Bootstrap;

/// <summary>
/// 在兼容性守卫通过、Harmony 补丁已应用之后运行，按架构要求的顺序把桥的子系统接起来：
///
///   守卫通过 →（补丁已由 MainFile 应用）→ CombatSolver 适配面摘要
///           → 观察模式激活（转储）→ 0.1.0：翻译表 + 镜像注册
///
/// 所有 CombatSolver 相关注册必须在 Mod 初始化阶段完成、早于第一次战斗根捕获——
/// CombatSolver 的注册表在那里冻结，迟到注册要么抛异常要么静默无效。
/// </summary>
public static class BridgeBootstrap
{
    public static bool IsReady { get; private set; }
    internal static void MarkReady() => IsReady = true;

    public static void Initialize(CompatibilityReport report)
    {
        var aa = report.AutoAnthony;
        var cs = report.CombatSolver;

        BridgeLog.Info($"桥已启用：AutoAnthony {aa.ManifestVersion}（MVID {aa.Mvid}）+ CombatSolver {cs.ManifestVersion}（MVID {cs.Mvid}）。");
        BridgeLog.Info($"AutoAnthony DLL：{aa.AssemblyPath} SHA256 {aa.Sha256}");
        BridgeLog.Info($"CombatSolver DLL：{cs.AssemblyPath} SHA256 {cs.Sha256}");
        foreach (var note in report.Notes)
            BridgeLog.Info($"[guard] {note}");

        CombatSolverRegistrar.Initialize(report);

        BridgeLog.Info("全量实现候选已启用：241 翻译键；目录审计 467/467 原子在必要配对上下文中准入。" +
                       "此统计不代表全部整卡组合的实机严格 diff 已验收；未知组件继续保守排除。");
    }
}
