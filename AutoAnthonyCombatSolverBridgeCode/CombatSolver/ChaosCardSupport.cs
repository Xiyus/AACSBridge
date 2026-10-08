using System.Runtime.CompilerServices;
using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Bootstrap;
using MegaCrit.Sts2.Core.Models;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

/// <summary>
/// 卡级支持矩阵判定（带缓存）。
///
/// 判定结果只依赖（卡牌实例定义, 是否已升级）——用 (OriginalCard, IsUpgraded) 缓存，
/// 避免搜索每个节点对同一张卡重复做 EffectiveRuntimeSpec 升级投影。
///
/// 保守可打性模式（默认开启）：超出支持矩阵的卡在模拟中按"不可打"处理——搜索只探索
/// 可精确预测的路线（部署的每个动作都被精确预测），矩阵外的卡留在手里不被求解器主动
/// 打出（真实游戏不受任何影响，玩家仍可手动打出）。强制打出（Havoc/Cascade 类 auto-play
/// 效果绕过 CanPlay）仍由 OnPlay 镜像的硬失败兜底。
///
/// 代价与取舍：路线可能次优（避开了一张本可制胜的矩阵外卡）——但预测永远精确，
/// 这是对"宁可次优、不可错误预测"红线的保守实现。设环境变量 AA_BRIDGE_STRICT=1
/// 可关闭本模式，回到纯硬失败语义（任何矩阵外卡进入候选即中止整场搜索）。
/// </summary>
internal static class ChaosCardSupport
{
    public static bool ConservativePlayability =>
        UseConservativePlayability(BridgeBootstrap.ForceConservativePlayability,
            Environment.GetEnvironmentVariable("AA_BRIDGE_STRICT"));

    internal static bool UseConservativePlayability(bool compatibilityFallback, string? strict)
        => compatibilityFallback || !string.Equals(strict, "1", StringComparison.Ordinal);

    private sealed class Verdict
    {
        public string? Reason;   // null = 支持
    }

    private sealed class VerdictCache
    {
        public volatile Verdict? Upgraded;
        public volatile Verdict? NotUpgraded;
    }

    private static readonly ConditionalWeakTable<CardModel, VerdictCache> Cache = new();

    /// <summary>
    /// 返回卡牌的不支持原因（null = 在支持矩阵内）。cacheKey 用根实例（PredictedCard.Original），
    /// 判定读 receiver（Preview/MutablePreview 克隆——定义与升级状态与根一致）。
    /// 缓存竞争是良性的（重复计算同一结果）。
    /// </summary>
    public static string? GetUnsupportedReason(ChaosCardModel card, CardModel? cacheKey)
    {
        var key = cacheKey ?? card;
        var cache = Cache.GetValue(key, static _ => new VerdictCache());
        var slot = card.IsUpgraded ? cache.Upgraded : cache.NotUpgraded;
        if (slot is not null)
            return slot.Reason;

        var reason = ChaosCardOnPlayMirror.ValidateCard(card);
        var verdict = new Verdict { Reason = reason };
        if (card.IsUpgraded)
            cache.Upgraded = verdict;
        else
            cache.NotUpgraded = verdict;
        return reason;
    }
}
