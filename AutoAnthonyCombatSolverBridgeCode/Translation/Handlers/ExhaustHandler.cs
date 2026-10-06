using AutoAnthonyCombatSolverBridge.Translation;
using MegaCrit.Sts2.Core.Entities.Cards;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// exhaust_card(all) 的精确镜像：消耗整手牌（filter=non_attack 时排除攻击牌）。
/// 复刻 ChaosOperationExecutor.Exhaust 的 variant=all 分支（L2878-2881）：
///   targets = hand（或 hand.Where(Type != Attack)）→ 逐张 CardCmd.Exhaust
///   → 逐张 simulator.Exhaust（与 CombatSolver 自带的 ExhaustHand 扩展同款循环，
///   含挂起选择断流）。
/// 注意：出牌中的卡本身在 Play 堆，不在手牌内——与真实执行一致。
/// </summary>
public sealed class ExhaustHandler : IOperationHandler
{
    public string Describe => "exhaust_card(all)：逐张 Exhaust 精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target, spec.SourceZone, spec.DestinationZone, spec.CardFilter) switch
        {
            ("all", "all_cards", "hand", "none", "any") => null,
            ("all", "all_cards", "hand", "none", "non_attack") => null,
            ("random", "random_card", "hand", "none", _) => null,
            _ => $"exhaust_card 的 (variant={spec.Variant}, target={spec.Target}, zones={spec.SourceZone}->{spec.DestinationZone}, filter={spec.CardFilter}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var spec = context.Shape.Spec;
        if (spec.Variant == "random")
        {
            // 源码：随机消耗手牌 1 张（CombatCardSelection 随机）
            var hand = context.Mirror.OwnerState.Hand.Cards.ToList();
            var selected = context.Mirror.Rng.CombatCardSelection.NextItem(hand);
            if (selected is not null)
            {
                context.Mirror.Simulator.Exhaust(selected);
                context.Resolution?.ExhaustedByCard.Add(selected);
            }
            return;
        }
        var handCards = context.Mirror.OwnerState.Hand.Cards.ToList();
        var targets = spec.CardFilter == "non_attack"
            ? handCards.Where(candidate => candidate.Preview.Type != CardType.Attack).ToList()
            : handCards;
        foreach (var target in targets)
        {
            context.Mirror.Simulator.Exhaust(target);
            context.Resolution?.ExhaustedByCard.Add(target);
            if (context.Mirror.Simulator.HasPendingChoice)
                return;
        }
    }
}
