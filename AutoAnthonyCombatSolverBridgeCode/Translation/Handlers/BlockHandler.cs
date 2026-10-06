using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver;
using MegaCrit.Sts2.Core.Models.Powers;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// gain_block(immediate/self) 的精确镜像。
/// 复刻 ChaosOperationExecutor.TryExecuteStructuredCommon L820-829 与 ApplyBlockModifiers：
///   block = amount + ExtraBlock + Σ(strength_scaled 修饰符加成)
///   props = BlockPropsForCardEffect（Power 卡 Unpowered，否则 Move——普通直打格挡吃
///   Dexterity/Frail 的游戏管线）
///   → CreatureCmd.GainBlock(owner, block, props, cardPlay)
///   → simulator.GainBlock(owner, block, props, card, cardPlay)
/// strength_scaled 数学（0.2.0，逐字复刻源码 L3628-3648）：
///   strength / max(1, interval) * block_per_interval * 1（依赖乘数对无前缀修饰符恒为 1）
///   ——整数除法后乘；力量从分支状态读（combat.GetPower&lt;StrengthPower&gt;(owner)），
///   绝不读 live creature；interval 是 live 槽（DynamicVar 优先），bonus 走升级后 spec。
/// </summary>
public sealed class BlockHandler : IOperationHandler
{
    public string Describe => "gain_block(immediate)：simulator.GainBlock + strength_scaled 修饰符精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target) switch
        {
            ("immediate", "self") => null,
            _ => $"gain_block 的 (variant={spec.Variant}, target={spec.Target}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        // ApplyBlockModifiers：base + ExtraBlock + Σ(strength_scaled 加成)
        var block = context.ExecutableAmount + context.Card.ExtraBlock + StrengthScaledBonus(context);
        context.Mirror.Simulator.GainBlock(
            context.Card.Owner.Creature, block, context.BlockProps, context.Mirror.Card, context.Mirror.CardPlay);
    }

    /// <summary>
    /// 遍历卡上全部 M:base/strength_scaled 修饰符并累加加成（校验层保证只存在这一种修饰符，
    /// 且其值槽全部 fixed、无依赖前缀——依赖乘数恒为 1）。
    /// </summary>
    private static int StrengthScaledBonus(OperationExecutionContext context)
    {
        var card = context.Card;
        var operations = card.Generated.Operations;
        var combat = context.Mirror.CombatState as SimulatedCombatState
            ?? throw new InvalidOperationException("strength_scaled 修饰符需要 SimulatedCombatState 分支状态。");
        // 力量从分支状态读（源码读 live creature 的 GetPower，镜像侧等价换成分支查询）
        var strength = Math.Max(0, combat.GetPower<StrengthPower>(card.Owner.Creature)?.Amount ?? 0);

        var bonus = 0;
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            if (operation.Scope != OperationScope.Modifier || operation.Template != "M:base")
                continue;

            var spec = ChaosOperationExecutor.EffectiveRuntimeSpec(card, index);
            if (spec.Variant != "strength_scaled")
                continue;

            var interval = ChaosOperationExecutor.RuntimeSpecValue(card, index, "strength_interval", 1);
            var perInterval = ChaosOperationExecutor.RuntimeSpecValue(card, index, "block_per_interval", 0);
            // 源码：strength / Math.Max(1, interval) * bonus * dependencyRepeats（整数除法后乘）
            bonus += strength / Math.Max(1, interval) * perInterval;
        }
        return bonus;
    }
}
