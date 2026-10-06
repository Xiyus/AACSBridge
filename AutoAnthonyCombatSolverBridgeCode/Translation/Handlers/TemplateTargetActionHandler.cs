using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// template_target_action 的目标模板精确镜像（0.8.x：7 条目录形状）。
/// 逐字复刻源码的目标模板路由：
///  - t_poison（L749-753）：PowerCmd.Apply&lt;PoisonPower&gt;(ctx, state.Target, amount, owner, card)
///  - t_xstrengthloss（L754-758）：X 值解析 → PowerCmd.Apply&lt;StrengthPower&gt;(target, -x)
///  - t_xweak（L759）：X 值解析 → PowerCmd.Apply&lt;WeakPower&gt;(target, x)
/// 目标来自卡牌打出目标（state.Target = cardPlay.Target）。
/// </summary>
public sealed class TemplateTargetActionHandler : IOperationHandler
{
    public string Describe => "template_target_action(毒/X 力量损失/X 虚弱)：目标 Power 精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return spec.Variant switch
        {
            "t_poison" => null,
            "t_xstrengthloss" => null,
            "t_xweak" => null,
            "ncr_applydoom" => null,
            _ => $"template_target_action 的 variant={spec.Variant} 不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var target = context.Mirror.CardPlay.Target;
        if (target is null)
            return;    // 源码语义：无目标 = no-op

        var mirror = context.Mirror;
        var owner = context.Card.Owner;
        if (mirror.CombatState is not ICombatPredictionEffectSink effects)
            throw new InvalidOperationException("目标模板需要分支战斗状态效果汇。");

        switch (context.Shape.Spec.Variant)
        {
            case "t_poison":
            {
                // 源码 L749-753：PowerCmd.Apply<PoisonPower>(ctx, target, amount, owner, card)
                var amount = context.Card.OperationAmount(context.Shape.OperationIndex);
                effects.ApplyPowerFromSource(typeof(PoisonPower), target, amount, owner.Creature, context.Card);
                return;
            }
            case "t_xstrengthloss":
            {
                // 源码 L754-758：x = RuntimeSpecValue(card, index, "amount", ResolveEffectEnergyXValue())
                //              → PowerCmd.Apply<StrengthPower>(target, -x)
                var x = ChaosOperationExecutor.RuntimeSpecValue(
                    context.Card, context.Shape.OperationIndex, "amount",
                    context.Card.ResolveEffectEnergyXValue());
                effects.ApplyPowerFromSource(typeof(StrengthPower), target, -x, owner.Creature, context.Card);
                return;
            }
            case "t_xweak":
            {
                // 源码 L759：PowerCmd.Apply<WeakPower>(target, x)
                var x = ChaosOperationExecutor.RuntimeSpecValue(
                    context.Card, context.Shape.OperationIndex, "amount",
                    context.Card.ResolveEffectEnergyXValue());
                effects.ApplyPowerFromSource(typeof(WeakPower), target, x, owner.Creature, context.Card);
                return;
            }
            case "ncr_applydoom":
            {
                // 源码 L1832-1835：PowerCmd.Apply<DoomPower>(target, amount)（简单路径——
                // DoomPerDoomThreshold 修饰符由校验层排除）
                var amount = context.Card.OperationAmount(context.Shape.OperationIndex);
                effects.ApplyPowerFromSource(typeof(DoomPower), target, amount, owner.Creature, context.Card);
                return;
            }
            default:
                throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
        }
    }
}
