using AutoAnthonyCombatSolverBridge.Translation;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// gain_block(immediate/self) 的精确镜像。
/// 复刻 ChaosOperationExecutor.TryExecuteStructuredCommon L820-829：
///   block = ApplyBlockModifiers 无修饰符路径（amount + ExtraBlock）
///   props = BlockPropsForCardEffect（Power 卡 Unpowered，否则 Move——普通直打格挡吃
///   Dexterity/Frail 的游戏管线）
///   → CreatureCmd.GainBlock(owner, block, props, cardPlay)
///   → simulator.GainBlock(owner, block, props, card, cardPlay)
/// </summary>
public sealed class BlockHandler : IOperationHandler
{
    public string Describe => "gain_block(immediate)：simulator.GainBlock 精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target) switch
        {
            ("immediate", "self") => null,
            _ => $"gain_block 的 (variant={spec.Variant}, target={spec.Target}) 组合不在 0.1.0 支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        // ApplyBlockModifiers 无修饰符路径（校验层已拒绝全部 Modifier 操作）
        var block = context.ExecutableAmount + context.Card.ExtraBlock;
        context.Mirror.Simulator.GainBlock(
            context.Card.Owner.Creature, block, context.BlockProps, context.Mirror.Card, context.Mirror.CardPlay);
    }
}
