using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// template_self_action 的球引导/聚焦/球位/Shiv 创建精确镜像（0.8.x：37 条目录形状）。
/// 逐字复刻源码的模板路由（遗留路径）：
///  - d_channelfrost/dark/lightning/glass/plasma（L2162-2165 → ChaosOrbResolver.Channel L26-54）：
///    count 次 OrbCmd.Channel&lt;T&gt; → simulator.OrbChannel&lt;T&gt;(owner, count)
///  - d_channelrandom：CombatOrbGeneration 随机选球 → 分支 RNG 同流
///  - d_gainfocus（L2184-2185）：PowerCmd.Apply&lt;FocusPower&gt; → effects.ApplyPowerFromSource
///  - d_gaintemporaryfocus（L2187-2188）：同上（临时聚焦在本回合结束自然过期）
///  - d_gainorbslots（L2191）：OrbCmd.AddSlots → simulator.AddOrbSlots
///  - n_createshiv（SimpleHandDerivativeProducerTemplates → CreateDerivatives）：
///    simulator.CreateAndAddGeneratedCardsToCombat&lt;Shiv&gt;(owner, Hand, count, owner)
///    ——与原生镜像 BespokeCardMirrors L138 同款
/// </summary>
public sealed class TemplateSelfActionHandler : IOperationHandler
{
    public string Describe => "template_self_action(球引导/聚焦/球位/Shiv)：模拟器精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return spec.Variant switch
        {
            "d_channelfrost" or "d_channeldark" or "d_channellightning"
                or "d_channelglass" or "d_channelplasma" or "d_channelrandom" => null,
            "d_gainfocus" or "d_gaintemporaryfocus" => null,
            "d_gainorbslots" => null,
            "n_createshiv" => null,
            "r_forge" => null,
            "n_allpoison" => null,
            "d_evokerightmostorb" => null,
            "d_loseorbslots" => null,
            "d_nextturnenergy" => null,
            "d_losefocus" => null,
            "d_gainstrength" => null,
            "d_gaindexterity" => null,
            _ => $"template_self_action 的 variant={spec.Variant} 不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var mirror = context.Mirror;
        var owner = context.Card.Owner;
        var amount = context.Card.OperationAmount(context.Shape.OperationIndex);
        // 源码门控：ExecutableOrbRepeatCount(amount) = Math.Max(0, amount)，== 0 时跳过
        var count = Math.Max(0, amount);

        switch (context.Shape.Spec.Variant)
        {
            case "d_channelfrost":
                if (count == 0) return;
                mirror.Simulator.OrbChannel<FrostOrb>(owner, count);
                return;
            case "d_channeldark":
                if (count == 0) return;
                mirror.Simulator.OrbChannel<DarkOrb>(owner, count);
                return;
            case "d_channellightning":
                if (count == 0) return;
                mirror.Simulator.OrbChannel<LightningOrb>(owner, count);
                return;
            case "d_channelglass":
                if (count == 0) return;
                mirror.Simulator.OrbChannel<GlassOrb>(owner, count);
                return;
            case "d_channelplasma":
                if (count == 0) return;
                mirror.Simulator.OrbChannel<PlasmaOrb>(owner, count);
                return;
            case "d_channelrandom":
            {
                // 源码：OrbModel.GetRandomOrb(Rng.CombatOrbGeneration)——分支 RNG 同流。
                // 真实代码的 for 循环不因 Channel 失败而中断——RNG 消耗必须与实际严格一致
                //（每次迭代消耗 1 次 GetRandomOrb，无论入队是否成功）。
                for (var index = 0; index < count; index++)
                {
                    var orb = OrbModel.GetRandomOrb(mirror.Rng.CombatOrbGeneration).ToMutable();
                    mirror.Simulator.OrbChannel(owner, orb);
                    if (mirror.Simulator.HasPendingChoice)
                        return;    // 选择挂起是合法边界（续接戳会抓到 RNG 差异）
                }
                return;
            }
            case "d_gainfocus":
            case "d_gaintemporaryfocus":
            {
                // 源码：PowerCmd.Apply<FocusPower>(ctx, owner, amount, owner, card)
                if (mirror.CombatState is not ICombatPredictionEffectSink effects)
                    throw new InvalidOperationException("聚焦需要分支战斗状态效果汇。");
                effects.ApplyPowerFromSource(typeof(FocusPower), owner.Creature, amount, owner.Creature, context.Card);
                return;
            }
            case "d_gainorbslots":
                if (count == 0) return;
                mirror.Simulator.AddOrbSlots(owner, count);
                return;
            case "n_createshiv":
            {
                // 源码：CreateDerivatives(card, index, op, Hand, ExecutableOperationCount(op, amount))
                // 镜像：与原生 BespokeCardMirrors L138 同款
                var operation = context.Card.Generated.Operations[context.Shape.OperationIndex];
                var shivCount = ChaosOperationExecutor.ExecutableOperationCount(operation, amount);
                if (shivCount == 0)
                    return;
                mirror.Simulator.CreateAndAddGeneratedCardsToCombat<Shiv>(
                    owner, PileType.Hand, shivCount, owner);
                return;
            }
            case "r_forge":
            {
                // 源码 L1471：ForgeCmd.Forge(amount, owner, card)
                // 镜像：与原生 PersistentPowerSupport.Forge 同款（创建/强化君王之刃）
                if (count == 0) return;
                global::CombatSolver.PersistentPowerSupport.Forge(mirror.Simulator, owner, count);
                return;
            }
            case "n_allpoison":
            {
                // 源码 L678：PowerCmd.Apply<PoisonPower>(ctx, HittableEnemies, amount, owner, card)
                if (count == 0) return;
                if (mirror.CombatState is not ICombatPredictionEffectSink effects)
                    throw new InvalidOperationException("全体毒需要分支战斗状态效果汇。");
                foreach (var enemy in mirror.CombatState.HittableEnemies)
                    effects.ApplyPowerFromSource(typeof(PoisonPower),
                        enemy, count, owner.Creature, context.Card);
                return;
            }
            case "d_evokerightmostorb":
            {
                // 源码 L2296-2307：evokeCount 次 EvokeNext，仅最后一次 dequeue（Dualcast 式）
                if (count == 0) return;
                var orbQueue = mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue;
                if (orbQueue.Orbs.Count == 0) return;
                for (var i = 0; i < count; i++)
                    mirror.Simulator.OrbEvokeNext(owner, 1, dequeue: i == count - 1);
                return;
            }
            case "d_loseorbslots":
                // 源码 L2190：OrbCmd.RemoveSlots(owner, amount)
                if (count == 0) return;
                mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue.RemoveCapacity(count);
                return;
            case "d_nextturnenergy":
                // 源码 L2198-2199：PowerCmd.Apply<EnergyNextTurnPower>(owner, amount)
                if (count == 0) return;
                if (mirror.CombatState is not ICombatPredictionEffectSink effects2)
                    throw new InvalidOperationException("下回合能量需要分支战斗状态效果汇。");
                effects2.ApplyPowerFromSource(typeof(EnergyNextTurnPower), owner.Creature, count, owner.Creature, context.Card);
                return;
            case "d_losefocus":
                // 源码 L2187-2188：PowerCmd.Apply<FocusPower>(owner, -amount)
                if (count == 0) return;
                if (mirror.CombatState is not ICombatPredictionEffectSink effects3)
                    throw new InvalidOperationException("失焦需要分支战斗状态效果汇。");
                effects3.ApplyPowerFromSource(typeof(FocusPower), owner.Creature, -count, owner.Creature, context.Card);
                return;
            case "d_gainstrength":
                // 源码 L2192-2193：PowerCmd.Apply<StrengthPower>(owner, amount)
                if (count == 0) return;
                if (mirror.CombatState is not ICombatPredictionEffectSink effects4)
                    throw new InvalidOperationException("力量需要分支战斗状态效果汇。");
                effects4.ApplyPowerFromSource(typeof(StrengthPower), owner.Creature, count, owner.Creature, context.Card);
                return;
            case "d_gaindexterity":
                // 源码 L2195-2196：PowerCmd.Apply<DexterityPower>(owner, amount)
                if (count == 0) return;
                if (mirror.CombatState is not ICombatPredictionEffectSink effects5)
                    throw new InvalidOperationException("敏捷需要分支战斗状态效果汇。");
                effects5.ApplyPowerFromSource(typeof(DexterityPower), owner.Creature, count, owner.Creature, context.Card);
                return;
            default:
                throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
        }
    }
}
