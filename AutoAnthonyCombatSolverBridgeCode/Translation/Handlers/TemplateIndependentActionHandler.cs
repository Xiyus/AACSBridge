using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using MegaCrit.Sts2.Core.Models.Powers;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// template_independent_action 的简单独立模板精确镜像（0.8.x：5 条目录形状）。
/// 逐字复刻源码的独立模板路由：
///  - i_preventdrawthisturn（L3252）：PowerCmd.Apply&lt;NoDrawPower&gt;(owner, 1)
///  - i_gaintemporarystrength（L3253）：PowerCmd.Apply&lt;SetupStrikePower&gt;(owner, amount)
///  - i_applytoallenemies（L3260）：PowerCmd.Apply&lt;VulnerablePower&gt;(HittableEnemies, amount)
///  - i_gainmaxhp（L3254）：CreatureCmd.GainMaxHp(owner, amount) → 模拟器最大生命 API
/// </summary>
public sealed class TemplateIndependentActionHandler : IOperationHandler
{
    public string Describe => "template_independent_action(禁抽/临时力量/全体易伤/最大生命)：精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return spec.Variant switch
        {
            "i_preventdrawthisturn" => null,
            "i_gaintemporarystrength" => null,
            "i_applytoallenemies" => null,
            "i_gainmaxhp" => null,
            _ => $"template_independent_action 的 variant={spec.Variant} 不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var mirror = context.Mirror;
        var owner = context.Card.Owner;
        var amount = context.ExecutableAmount;
        if (mirror.CombatState is not ICombatPredictionEffectSink effects)
            throw new InvalidOperationException("独立模板需要分支战斗状态效果汇。");

        switch (context.Shape.Spec.Variant)
        {
            case "i_preventdrawthisturn":
                // 源码 L3252：PowerCmd.Apply<NoDrawPower>(ctx, owner, 1, owner, card)
                effects.ApplyPowerFromSource(typeof(NoDrawPower), owner.Creature, 1, owner.Creature, context.Card);
                return;
            case "i_gaintemporarystrength":
                // 源码 L3253：PowerCmd.Apply<SetupStrikePower>(ctx, owner, amount, owner, card)
                effects.ApplyPowerFromSource(typeof(SetupStrikePower), owner.Creature, amount, owner.Creature, context.Card);
                return;
            case "i_applytoallenemies":
                // 源码 L3260：PowerCmd.Apply<VulnerablePower>(ctx, HittableEnemies, amount, owner, card)
                if (amount == 0) return;
                foreach (var enemy in mirror.CombatState.HittableEnemies)
                    effects.ApplyPowerFromSource(typeof(VulnerablePower), enemy, amount, owner.Creature, context.Card);
                return;
            case "i_gainmaxhp":
                // 源码 L3254：CreatureCmd.GainMaxHp(owner, amount)
                // 镜像：模拟器的 GainMaxHp（与 CreatureCmd 语义一致——治疗实际增量）
                if (amount == 0) return;
                mirror.Simulator.GainMaxHp(owner.Creature, amount);
                return;
            default:
                throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
        }
    }
}
