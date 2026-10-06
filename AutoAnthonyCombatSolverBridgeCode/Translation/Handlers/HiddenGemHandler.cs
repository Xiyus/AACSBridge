using AutoAnthonyCombatSolverBridge.Translation;
using MegaCrit.Sts2.Core.Entities.Cards;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// CL:ProxyAtomic_HiddenGem（未察觉大师式"抽牌堆随机一张牌获得 Replay"）的精确镜像。
/// 复刻 ChaosOperationExecutor.ExecuteOriginalOperation L2549-2567：
///   抽牌堆过滤（可打、非状态/诅咒、未被附魔重放过）→ 优先普通牌（攻击/技能/能力）
///   → CombatCardSelection 随机选一张 → BaseReplayCount += max(1, amount)
///   → CardCmd.Preview（视觉，跳过）
/// 镜像侧：抽牌堆/关键词/类型/重放计数读分支状态；随机用分支 CombatCardSelection 流
/// （RNG 消耗与真实一致）；写入走 MutablePreview（COW 写入唯一合法目标）——与原生
/// 镜像 CardSelectionCardMirrors 的 BaseReplayCount 写法同款。求解器的执行/状态键/
/// 续接戳全面支持 BaseReplayCount。
/// 注意：源码此处直接读 OperationAmount（不走 Execute 的外部加成链），镜像保持一致。
/// </summary>
public sealed class HiddenGemHandler : IOperationHandler
{
    public string Describe => "template_independent_action(cl_proxyatomic_hiddengem)：抽牌堆随机加 Replay 精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target) switch
        {
            ("cl_proxyatomic_hiddengem", "self") => null,
            _ => $"template_independent_action 的 (variant={spec.Variant}, target={spec.Target}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var drawPile = context.Mirror.OwnerState.DrawPile.Cards;
        var candidates = drawPile.Where(candidate =>
                !candidate.Preview.Keywords.Contains(CardKeyword.Unplayable)
                && candidate.Preview.Type is not (CardType.Status or CardType.Curse)
                && candidate.Preview.GetEnchantedReplayCount() < 1)
            .ToList();
        var ordinaryCards = candidates.Where(candidate => candidate.Preview.Type is
            CardType.Attack or CardType.Skill or CardType.Power).ToList();
        var selected = context.Mirror.Rng.CombatCardSelection.NextItem(
            ordinaryCards.Count > 0 ? ordinaryCards : candidates);
        if (selected is null)
            return;    // 源码语义：无候选 = no-op

        // 源码直接读 OperationAmount（不含外部加成链）
        var amount = context.Card.OperationAmount(context.Shape.OperationIndex);
        selected.MutablePreview.BaseReplayCount += Math.Max(1, amount);
    }
}
