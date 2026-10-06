using AutoAnthonyCombatSolverBridge.Translation;
using MegaCrit.Sts2.Core.Entities.Cards;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// move_card(random) 的精确镜像：随机从弃牌堆移 1 张到手牌。
/// 复刻 ChaosOperationExecutor.MoveCard 的 random 分支：
///   selected = CombatCardSelection.NextItem(discard) → Add(selected, Hand)
/// 镜像：分支 CombatCardSelection 随机 → simulator 移牌。
/// </summary>
public sealed class MoveCardRandomHandler : IOperationHandler
{
    public string Describe => "move_card(random)：随机移牌精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.SourceZone, spec.DestinationZone) switch
        {
            ("random", "discard", "hand") => null,
            _ => $"move_card 的 (variant={spec.Variant}, zones={spec.SourceZone}->{spec.DestinationZone}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var discard = context.Mirror.OwnerState.DiscardPile.Cards.ToList();
        var selected = context.Mirror.Rng.CombatCardSelection.NextItem(discard);
        if (selected is null) return;
        // 随机移牌：从弃牌堆移到手牌（先弃再抽回——与 Add(selected, Hand) 等价）
        context.Mirror.Simulator.AddGeneratedCardToCombat(selected, PileType.Hand,
            context.Card.Owner);
    }
}
