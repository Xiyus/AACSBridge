using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// create_copy(this_card) 的精确镜像：把本卡的克隆放进弃牌堆（Anger 式）。
/// 复刻 ChaosOperationExecutor.CreateCard 的 this_card 分支（L2946-2947）：
///   count = ExecutableOperationCount(operation, amount)（amount&gt;0 取 amount；
///   X 源取 0——校验层已排除 X；add_one 标志取 1；否则 0）
///   → count 个 card.CreateClone() → AddGeneratedCardsToCombat(弃牌堆, Bottom)
/// 镜像范式与 CombatSolver 的原生 Anger 镜像（CorePowerSupport L245-251）逐位一致：
///   simulator.AddGeneratedCardToCombat(Card.CreateClone(), PileType.Discard, owner,
///   CardPilePosition.Bottom, CardGenerationResultKind.Fixed)
/// （反编译核实游戏 CardPileCmd.AddGeneratedCardsToCombat 默认位置即 Bottom。）
/// 注：不调用 RecordAngerCopyGenerated——那是原生 Anger 的搜索启发式计数，与状态正确性无关。
/// </summary>
public sealed class CreateCopyHandler : IOperationHandler
{
    public string Describe => "create_copy(this_card)：克隆进弃牌堆精确镜像（Anger 范式）";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target, spec.SourceZone, spec.DestinationZone) switch
        {
            ("this_card", "self_card", "none", "discard") => null,
            _ => $"create_copy 的 (variant={spec.Variant}, target={spec.Target}, zones={spec.SourceZone}->{spec.DestinationZone}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var operation = context.Card.Generated.Operations[context.Shape.OperationIndex];
        var count = ChaosOperationExecutor.ExecutableOperationCount(operation, context.ExecutableAmount);
        if (count == 0)
            return;    // 源码语义：count=0 = no-op

        for (var index = 0; index < count; index++)
        {
            context.Mirror.Simulator.AddGeneratedCardToCombat(
                context.Mirror.Card.CreateClone(),
                PileType.Discard,
                context.Card.Owner,
                CardPilePosition.Bottom,
                CardGenerationResultKind.Fixed);
            if (context.Mirror.Simulator.HasPendingChoice)
                return;
        }
    }
}
