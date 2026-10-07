using AutoAnthonyCombatSolverBridge.Translation;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// discard_card(all) 的精确镜像：弃掉整手牌。
/// 复刻 ChaosOperationExecutor.TryExecuteStructuredCommon L885-887 → Discard(card, int.MaxValue, ...)
/// 的 count >= hand.Count 分支（L2926-2927）：targets = hand → 一次 CardCmd.Discard(列表)
/// → 一次 simulator.Discard(列表)。
/// </summary>
public sealed class DiscardHandler : IOperationHandler
{
    public string Describe => "discard_card(all)：simulator.Discard 整手精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target, spec.SourceZone, spec.DestinationZone, spec.CardFilter) switch
        {
            ("all", "all_cards", "hand", "none", "any") => null,
            _ => $"discard_card 的 (variant={spec.Variant}, target={spec.Target}, zones={spec.SourceZone}->{spec.DestinationZone}, filter={spec.CardFilter}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var hand = context.Mirror.OwnerState.Hand.Cards.ToList();
        if (hand.Count == 0)
            return;    // 源码语义：空目标 = no-op
        context.Mirror.Simulator.Discard(hand);
        context.Resolution?.DiscardedByCard.AddRange(hand);
    }
}
