using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Commands;
using CombatSolver.Engine.InCombat.Simulation;

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
            "ncr_applyeventdamageasdoom" => null,
            "ncr_copytargetdebuffstoothers" => null,
            _ => $"template_target_action 的 variant={spec.Variant} 不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var target = context.Target;
        if (target is null)
            return;    // 源码语义：无目标 = no-op

        var mirror = context.Mirror;
        var owner = context.Card.Owner;
        if (mirror.CombatState is not ICombatPredictionEffectSink effects)
            throw new InvalidOperationException("目标模板需要分支战斗状态效果汇。");

        switch (context.Shape.Spec.Variant)
        {
            case "ncr_copytargetdebuffstoothers":
                TargetDebuffResolver.Copy(context);
                return;
            case "ncr_applyeventdamageasdoom":
                if (context.EventAmount > 0) effects.ApplyPowerFromSource(typeof(DoomPower), target, (int)context.EventAmount, owner.Creature, context.Card);
                return;
            case "t_poison":
            {
                // 源码 L749-753：PowerCmd.Apply<PoisonPower>(ctx, target, amount, owner, card)
                var amount = context.ExecutableAmount;
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
                var amount = context.ExecutableAmount;
                var modifierIndex = context.Card.Generated.Operations.ToList().FindIndex(op => op.Template == "NCR:DoomPerDoomThreshold");
                if (modifierIndex >= 0)
                {
                    var prefix = modifierIndex > 0 && context.Card.Generated.Operations[modifierIndex - 1].Template == "NCR:ForEachDoomThreshold";
                    var threshold = ChaosOperationExecutor.RuntimeSpecValue(context.Card, prefix ? modifierIndex - 1 : modifierIndex, "threshold", 0);
                    var bonus = ChaosOperationExecutor.RuntimeSpecValue(context.Card, modifierIndex, "bonus", 0);
                    if (threshold > 0 && bonus > 0)
                        amount += ((global::CombatSolver.SimulatedCombatState)mirror.CombatState).GetAmount<DoomPower>(target) / threshold * bonus;
                }
                effects.ApplyPowerFromSource(typeof(DoomPower), target, amount, owner.Creature, context.Card);
                return;
            }
            case "t_removeblockandartifact":
            {
                // 源码 L767-772：移除目标格挡 + 移除神器 Power
                var creatureState = mirror.Simulator.State.GetCreature(target);
                var currentBlock = creatureState.Block;
                if (currentBlock > 0)
                    creatureState.DamageBlock(currentBlock, ValueProp.Unpowered);
                if (mirror.CombatState is global::CombatSolver.SimulatedCombatState artifactCombat)
                    artifactCombat.SetAmount<ArtifactPower>(target, 0);
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
                var amount = ChaosOperationExecutor.ExecutableOperationCount(context.Operation, context.ExecutableAmount);
                if (amount > 0) effects.ApplyPowerFromSource(typeof(DebilitatePower), target, amount, owner.Creature, context.Card);
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
                    {
                        var (resolvedDamage, resolvedHits) = DamageModifierResolver.Resolve(context, doom, 1);
                        var results = new List<DamageResult>();
                        if (context.Card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Power)
                            for (var hit = 0; hit < resolvedHits; hit++)
                            {
                                results.AddRange(mirror.Simulator.Damage([target], resolvedDamage, ValueProp.Unpowered,
                                    owner.Creature, mirror.Card, mirror.CardPlay));
                                if (mirror.Simulator.HasPendingChoice) break;
                            }
                        else
                        {
                            var attack = DamageCmd.Attack(resolvedDamage).WithHitCount(resolvedHits).FromCard(context.Card, mirror.CardPlay).Targeting(target);
                            attack.Simulate(mirror.Simulator);
                            results.AddRange(attack.Results.SelectMany(result => result));
                        }
                        if (context.Resolution is { } resolution)
                        {
                            resolution.LastAttackKilled |= results.Any(result => result.WasTargetKilled);
                            resolution.LastDamageDealt = decimal.ToInt32(results.Sum(result => result.TotalDamage + result.OverkillDamage));
                        }
                    }
                }
                return;
            }
            case "ncr_ostydamage":
            {
                OstyDamageResolver.Execute(context, target);
                return;
            }
            case "ncr_unpowereddamage":
            {
                // 源码 L1727-1735：Unpowered 伤害（Power 卡路径——无力量加成）
                var amount = context.Card.OperationAmount(context.Shape.OperationIndex);
                var (dmg, hits) = DamageModifierResolver.Resolve(context, amount, 1);
                for (var hit = 0; hit < hits; hit++)
                {
                    mirror.Simulator.Damage([target], dmg, ValueProp.Unpowered, owner.Creature, mirror.Card, mirror.CardPlay);
                    if (mirror.Simulator.HasPendingChoice) break;
                }
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
                if (mirror.CombatState is global::CombatSolver.SimulatedCombatState hangCombat)
                {
                    var hang = hangCombat.GetAmount<HangPower>(target);
                    var increase = Math.Max(2, hang);
                    if (hang + increase > 999_999_999) increase = Math.Max(0, 999_999_999 - hang);
                    if (increase > 0) effects.ApplyPowerFromSource(typeof(HangPower), target, increase, owner.Creature, context.Card);
                }
                return;
            }
            case "ncr_applydoomequaldamage":
            {
                // 源码 L1711-1714：Doom = 本次伤害量（state.LastDamageDealt）
                var amount = context.Resolution?.LastDamageDealt ?? 0;
                if (amount > 0)
                    effects.ApplyPowerFromSource(typeof(DoomPower), target, amount, owner.Creature, context.Card);
                return;
            }
            case "d_triggerlightningpassivesattarget":
            {
                // 源码 L2330：触发所有闪电球的被动（对目标）
                var orbQueue = mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue;
                for (var repeat = 0; repeat < ChaosOperationExecutor.UpgradedOperationRepeatCount(context.Card, context.Shape.OperationIndex); repeat++)
                    foreach (var orb in orbQueue.Orbs.Where(orb => ChaosOrbResolver.MatchesSource(orb, context.Operation)).ToList())
                    {
                        mirror.Simulator.OrbPassive(orb, target);
                        if (mirror.Simulator.HasPendingChoice) return;
                    }
                return;
            }
            default:
                throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
        }
    }
}
