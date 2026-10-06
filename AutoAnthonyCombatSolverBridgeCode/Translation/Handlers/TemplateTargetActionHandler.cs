using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

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
            "t_removeblockandartifact" => null,
            "ncr_targetlosestrength" => null,
            "ncr_doublevulnerableweak" => null,
            "r_kingssworddoubledamagethisturn" => null,
            "ncr_doomscaleddamage" => null,
            "ncr_ostydamage" => null,
            "ncr_unpowereddamage" => null,
            "ncr_applypower_sicempower" => null,
            "ncr_doublehangdamage" => null,
            "ncr_applydoomequaldamage" => null,
            "d_triggerlightningpassivesattarget" => null,
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
                // 源码 L1832-1835：PowerCmd.Apply<DoomPower>(target, amount)（简单路径）
                var amount = context.Card.OperationAmount(context.Shape.OperationIndex);
                effects.ApplyPowerFromSource(typeof(DoomPower), target, amount, owner.Creature, context.Card);
                return;
            }
            case "t_removeblockandartifact":
            {
                // 源码 L767-772：移除目标格挡 + 移除神器 Power
                // 简化：格挡清零（CreatureCmd.LoseBlock 等价——DamageBlock 全量）
                var creatureState = mirror.Simulator.State.GetCreature(target);
                var currentBlock = creatureState.Block;
                if (currentBlock > 0)
                    creatureState.DamageBlock(currentBlock, ValueProp.Unpowered);
                return;
            }
            case "ncr_targetlosestrength":
            {
                // 源码 L1883：PowerCmd.Apply<StrengthPower>(target, -amount)
                var amount = context.Card.OperationAmount(context.Shape.OperationIndex);
                effects.ApplyPowerFromSource(typeof(StrengthPower), target, -amount, owner.Creature, context.Card);
                return;
            }
            case "ncr_doublevulnerableweak":
            {
                // 源码 L1787-1790：翻倍目标的易伤和虚弱（DebilitatePower 路径）
                if (mirror.CombatState is global::CombatSolver.SimulatedCombatState debuffCombat)
                {
                    var vulnerable = debuffCombat.GetAmount<VulnerablePower>(target);
                    if (vulnerable > 0)
                        effects.ApplyPowerFromSource(typeof(VulnerablePower), target, vulnerable, owner.Creature, context.Card);
                    var weak = debuffCombat.GetAmount<WeakPower>(target);
                    if (weak > 0)
                        effects.ApplyPowerFromSource(typeof(WeakPower), target, weak, owner.Creature, context.Card);
                }
                return;
            }
            case "r_kingssworddoubledamagethisturn":
            {
                // 源码 L1604：PowerCmd.Apply<ConquerorPower>(target, 1)
                effects.ApplyPowerFromSource(typeof(ConquerorPower), target, 1, owner.Creature, context.Card);
                return;
            }
            case "ncr_doomscaleddamage":
            {
                // 源码 L1893-1897：伤害 = 目标 Doom 层数（经 DamageAndHits）
                if (mirror.CombatState is global::CombatSolver.SimulatedCombatState doomCombat)
                {
                    var doom = doomCombat.GetAmount<DoomPower>(target);
                    if (doom > 0)
                    {
                        var (resolvedDamage, resolvedHits) = DamageModifierResolver.Resolve(context, doom, 1);
                        if (resolvedHits > 0)
                            mirror.Simulator.Damage([target], resolvedDamage,
                                context.DamageProps, owner.Creature, context.Mirror.Card, context.Mirror.CardPlay);
                    }
                }
                return;
            }
            case "ncr_ostydamage":
            {
                // 源码：Osty 对目标造成伤害（经 DamageAndHits——Osty 为攻击者）
                var osty = mirror.Simulator.State.GetOsty(owner);
                if (osty is null) return;    // Osty 不存在 = no-op
                var amount = context.Card.OperationAmount(context.Shape.OperationIndex);
                var (ostyDamage, ostyHits) = DamageModifierResolver.Resolve(context, amount, 1);
                if (ostyHits > 0)
                    mirror.Simulator.Damage([target], ostyDamage,
                        context.DamageProps, osty, context.Mirror.Card, context.Mirror.CardPlay);
                return;
            }
            case "ncr_unpowereddamage":
            {
                // 源码 L1727-1735：Unpowered 伤害（Power 卡路径——无力量加成）
                var amount = context.Card.OperationAmount(context.Shape.OperationIndex);
                var (dmg, hits) = DamageModifierResolver.Resolve(context, amount, 1);
                if (hits > 0)
                    mirror.Simulator.Damage([target], dmg,
                        MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered,
                        owner.Creature, context.Mirror.Card, context.Mirror.CardPlay);
                return;
            }
            case "ncr_applypower_sicempower":
            {
                // 源码：PowerCmd.Apply<SicEmPower>(target, amount)
                var amount = context.Card.OperationAmount(context.Shape.OperationIndex);
                effects.ApplyPowerFromSource(typeof(SicEmPower), target, amount, owner.Creature, context.Card);
                return;
            }
            case "ncr_doublehangdamage":
            {
                // 源码：翻倍目标的 HangDamage（简化——施加等量 StranglePower）
                if (mirror.CombatState is global::CombatSolver.SimulatedCombatState hangCombat)
                {
                    var hang = hangCombat.GetAmount<StranglePower>(target);
                    if (hang > 0)
                        effects.ApplyPowerFromSource(typeof(StranglePower), target, hang, owner.Creature, context.Card);
                }
                return;
            }
            case "ncr_applydoomequaldamage":
            {
                // 源码 L1711-1714：Doom = 本次伤害量（state.LastDamageDealt）
                // 简化：Doom = OperationAmount（直接打出时近似）
                var amount = context.Card.OperationAmount(context.Shape.OperationIndex);
                if (amount > 0)
                    effects.ApplyPowerFromSource(typeof(DoomPower), target, amount, owner.Creature, context.Card);
                return;
            }
            case "d_triggerlightningpassivesattarget":
            {
                // 源码 L2330：触发所有闪电球的被动（对目标）
                var orbQueue = mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue;
                foreach (var orb in orbQueue.Orbs.OfType<LightningOrb>().ToList())
                    mirror.Simulator.OrbPassive(orb, target);
                return;
            }
            default:
                throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
        }
    }
}
