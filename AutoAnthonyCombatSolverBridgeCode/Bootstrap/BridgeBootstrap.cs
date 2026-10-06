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

        BridgeLog.Info("0.4.0 生效：即时牌 + apply_power + 牌堆移动 + X 费卡（energy_x/star_x）+ 随机目标（分支 RNG）可被精确预测。" +
                       "矩阵外的卡保守排除（模拟中不可打）。进入战斗后留意 CHAOS_CARD 转储行与 CombatSolver 的搜索行为。");
    }
}
