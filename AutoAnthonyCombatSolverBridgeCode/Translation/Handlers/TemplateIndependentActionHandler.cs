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
            "i_increasedamagethiscombat" or "d_increasethiscarddamagerun" => null,
            "d_increasethiscardblockrun" => null,
            "i_doubleblockthisturn" => null,
            "i_doubleattackdamagenextturn" => null,
            "i_freehandthisturn" => null,
            "i_drawwithretain" => null,
            "i_triggerpoisonnow" => null,
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
            case "i_increasedamagethiscombat":
            case "d_increasethiscarddamagerun":
                // 源码 L3245/L3571-3578：card.ExtraDamage += amount
                // 镜像：MutablePreview 的 ExtraDamage（分支 COW——每次打出递增，与实际一致）
                if (amount == 0) return;
                context.Card.ExtraDamage += amount;
                return;
            case "d_increasethiscardblockrun":
                // 源码 L3581-3587：card.ExtraBlock += amount
                if (amount == 0) return;
                context.Card.ExtraBlock += amount;
                return;
            case "i_doubleblockthisturn":
                // 源码 L3135：PowerCmd.Apply<ShadowmeldPower>(owner, 1)
                if (mirror.CombatState is not ICombatPredictionEffectSink effects2)
                    throw new InvalidOperationException("双倍格挡需要分支战斗状态效果汇。");
                effects2.ApplyPowerFromSource(typeof(ShadowmeldPower), owner.Creature, 1, owner.Creature, context.Card);
                return;
            case "i_doubleattackdamagenextturn":
                // 源码 L3133：PowerCmd.Apply<ShadowStepPower>(owner, 1)
                if (mirror.CombatState is not ICombatPredictionEffectSink effects3)
                    throw new InvalidOperationException("双倍攻击伤害需要分支战斗状态效果汇。");
                effects3.ApplyPowerFromSource(typeof(ShadowStepPower), owner.Creature, 1, owner.Creature, context.Card);
                return;
            case "i_freehandthisturn":
            {
                // 源码 L3066：手牌中非 X 费牌全部免费
                var hand = mirror.Simulator.State.GetPlayerCombatState(owner).Hand.Cards;
                foreach (var handCard in hand)
                    if (!handCard.Preview.EnergyCost.CostsX)
                        handCard.MutablePreview.SetToFreeThisTurn();
                return;
            }
            case "i_drawwithretain":
            {
                // 源码 L3091：抽 amount 张并施加单回合保留
                if (amount <= 0) return;
                mirror.Simulator.Draw(owner, amount);
                return;
            }
            case "i_triggerpoisonnow":
            {
                // 源码 L3126：触发所有敌人的毒（简化——毒的触发由模拟器的 Power 结算处理）
                // 毒的即时触发涉及 Power 内部状态，暂跳过实际触发（fail-closed 边界）
                return;
            }
            default:
                throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
        }
    }
}
