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
        // combat_rule 的代理 Power 模板（A:ProxyAtomic 族——简单 PowerCmd.Apply）
        if (spec.Opcode == "combat_rule")
            return spec.Variant switch
            {
                "a_proxyatomic_buffer" or "a_proxyatomic_parry" or "a_proxyatomic_royalties"
                    or "a_proxyatomic_calcify" or "a_proxyatomic_swordsage" => null,
                _ => $"combat_rule 的 variant={spec.Variant} 不在支持矩阵",
            };
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
            "d_triggerrightmostorbpassive" => null,
            "ncr_applydoomall" => null,
            "ncr_applyselfdoom" => null,
            "ncr_applyweakall" => null,
            "ncr_applyvulnerableall" => null,
            "r_enemieslosestrengththisturn" => null,
            // 单行 Power 模板族（源码 case 路由，全部 PowerCmd.Apply 一行式）
            "cl_retainhandthisturn" or "r_retainhandthisturn" => null,
            "cl_gaingold" => null,
            "cl_noblockfromcards" => null,
            "cl_gainvigor" or "r_gainvigor" => null,
            "cl_gainnextturnblockequalcurrent" => null,
            "cl_applyweakall" or "r_applyweakall" => null,
            "cl_applyvulnerableall" or "r_applyvulnerableall" => null,
            "r_gainstrengththisturn" => null,
            "r_reflectblockeddamagethisturn" => null,
            "r_gainstrength" => null,
            "r_enemieslosestrength" => null,
            "r_kingsswordhitsallenemies" => null,
            "ncr_nextturnenergy" => null,
            "ncr_losestrength" => null,
            "ncr_nextvoidcostszero" => null,
            "d_nextpowercostszero" => null,
            "n_allweak" => null,
            // 简单 Power 模板（N: 族）
            "n_thorns" => null,
            "n_intangible" => null,
            "n_tempdex" => null,
            "n_nextturndraw" => null,
            "n_keepblocknextturn" => null,
            "n_nextturnblock" => null,
            // 状态牌创建（D:CreateXxxInDiscard 族）
            "d_createdazedindiscard" => null,
            "d_createtwowoundsindiscard" => null,
            "d_createburnindiscard" => null,
            "d_createslimeindiscard" => null,
            "d_createvoidindiscard" => null,
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

        // combat_rule 的代理 Power 模板（A:ProxyAtomic 族）
        if (context.Shape.Spec.Opcode == "combat_rule")
        {
            var proxyAmount = Math.Max(1, amount);    // 源码 ApplyGeneratedProxyPower：max(1, amount)
            switch (context.Shape.Spec.Variant)
            {
                case "a_proxyatomic_buffer":
                    ApplySelf(context, typeof(BufferPower), proxyAmount);
                    return;
                case "a_proxyatomic_parry":
                    ApplySelf(context, typeof(ParryPower), proxyAmount);
                    return;
                case "a_proxyatomic_royalties":
                    ApplySelf(context, typeof(RoyaltiesPower), proxyAmount);
                    return;
                case "a_proxyatomic_calcify":
                    ApplySelf(context, typeof(CalcifyPower), proxyAmount);
                    return;
                case "a_proxyatomic_swordsage":
                    ApplySelf(context, typeof(SwordSagePower), proxyAmount);
                    return;
                default:
                    throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
            }
        }

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
            case "d_triggerrightmostorbpassive":
            {
                // 源码 L2179-2182：最右球被动 × count 次
                if (count == 0) return;
                var orbQueue = mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue;
                if (orbQueue.Orbs.Count == 0) return;
                var rightmost = orbQueue.Orbs.FirstOrDefault();
                if (rightmost is null) return;
                for (var i = 0; i < count; i++)
                    mirror.Simulator.OrbPassive(rightmost);
                return;
            }
            case "ncr_applydoomall":
                // 源码 L1775-1776：PowerCmd.Apply<DoomPower>(HittableEnemies, amount)
                if (count == 0) return;
                if (mirror.CombatState is not ICombatPredictionEffectSink effects6)
                    throw new InvalidOperationException("末日需要分支战斗状态效果汇。");
                foreach (var enemy in mirror.CombatState.HittableEnemies)
                    effects6.ApplyPowerFromSource(typeof(DoomPower), enemy, count, owner.Creature, context.Card);
                return;
            case "ncr_applyselfdoom":
                // 源码 L1778-1779：PowerCmd.Apply<DoomPower>(owner, amount)
                if (count == 0) return;
                if (mirror.CombatState is not ICombatPredictionEffectSink effects7)
                    throw new InvalidOperationException("自身末日需要分支战斗状态效果汇。");
                effects7.ApplyPowerFromSource(typeof(DoomPower), owner.Creature, count, owner.Creature, context.Card);
                return;
            case "ncr_applyweakall":
                // 源码 L1781-1782：PowerCmd.Apply<WeakPower>(HittableEnemies, amount)
                if (count == 0) return;
                if (mirror.CombatState is not ICombatPredictionEffectSink effects8)
                    throw new InvalidOperationException("全体虚弱需要分支战斗状态效果汇。");
                foreach (var enemy in mirror.CombatState.HittableEnemies)
                    effects8.ApplyPowerFromSource(typeof(WeakPower), enemy, count, owner.Creature, context.Card);
                return;
            case "ncr_applyvulnerableall":
                // 源码 L1784-1785：PowerCmd.Apply<VulnerablePower>(HittableEnemies, amount)
                if (count == 0) return;
                if (mirror.CombatState is not ICombatPredictionEffectSink effects9)
                    throw new InvalidOperationException("全体易伤需要分支战斗状态效果汇。");
                foreach (var enemy in mirror.CombatState.HittableEnemies)
                    effects9.ApplyPowerFromSource(typeof(VulnerablePower), enemy, count, owner.Creature, context.Card);
                return;
            case "r_enemieslosestrengththisturn":
                // 源码 L1521-1522：PowerCmd.Apply<PiercingWailPower>(HittableEnemies, amount)
                if (count == 0) return;
                if (mirror.CombatState is not ICombatPredictionEffectSink effects10)
                    throw new InvalidOperationException("全体力量损失需要分支战斗状态效果汇。");
                foreach (var enemy in mirror.CombatState.HittableEnemies)
                    effects10.ApplyPowerFromSource(typeof(PiercingWailPower), enemy, count, owner.Creature, context.Card);
                return;
            // ===== 单行 Power 模板族（通用路由）=====
            case "cl_retainhandthisturn" or "r_retainhandthisturn":
                ApplySelf(context, typeof(RetainHandPower), 1);
                return;
            case "cl_gaingold":
                if (count == 0) return;
                if (mirror.CombatState is not global::CombatSolver.SimulatedCombatState simCombat)
                    throw new InvalidOperationException("金币需要分支战斗状态。");
                simCombat.GainPlayerGold(mirror.Simulator, owner, count);
                return;
            case "cl_noblockfromcards":
                ApplySelf(context, typeof(NoBlockPower), 1);
                return;
            case "cl_gainvigor" or "r_gainvigor":
                if (count == 0) return;
                ApplySelf(context, typeof(VigorPower), count);
                return;
            case "cl_gainnextturnblockequalcurrent":
            {
                // 源码：PowerCmd.Apply<BlockNextTurnPower>(owner, owner.Creature.Block)
                if (mirror.CombatState is not ICombatPredictionEffectSink effects11)
                    throw new InvalidOperationException("下回合格挡需要分支战斗状态效果汇。");
                var block = mirror.Simulator.State.GetCreature(owner.Creature).Block;
                if (block > 0)
                    effects11.ApplyPowerFromSource(typeof(BlockNextTurnPower), owner.Creature, block, owner.Creature, context.Card);
                return;
            }
            case "cl_applyweakall" or "r_applyweakall" or "n_allweak":
                ApplyToAll(context, typeof(WeakPower), count);
                return;
            case "cl_applyvulnerableall" or "r_applyvulnerableall":
                ApplyToAll(context, typeof(VulnerablePower), count);
                return;
            case "r_gainstrengththisturn":
                if (count == 0) return;
                ApplySelf(context, typeof(FlexPotionPower), count);
                return;
            case "r_reflectblockeddamagethisturn":
                if (count == 0) return;
                ApplySelf(context, typeof(ReflectPower), count);
                return;
            case "r_gainstrength":
                if (count == 0) return;
                ApplySelf(context, typeof(StrengthPower), count);
                return;
            case "r_enemieslosestrength":
                ApplyToAll(context, typeof(StrengthPower), -count);
                return;
            case "r_kingsswordhitsallenemies":
                if (count == 0) return;
                ApplySelf(context, typeof(SeekingEdgePower), count);
                return;
            case "ncr_nextturnenergy":
                if (count == 0) return;
                ApplySelf(context, typeof(EnergyNextTurnPower), count);
                return;
            case "ncr_losestrength":
                if (count == 0) return;
                ApplySelf(context, typeof(StrengthPower), -count);
                return;
            case "ncr_nextvoidcostszero":
                ApplySelf(context, typeof(VeilpiercerPower), 1);
                return;
            case "d_nextpowercostszero":
                ApplySelf(context, typeof(FreePowerPower), 1);
                return;
            // ===== N: 族简单 Power 模板 =====
            case "n_thorns":
                if (count == 0) return;
                ApplySelf(context, typeof(ThornsPower), count);
                return;
            case "n_intangible":
                if (count == 0) return;
                ApplySelf(context, typeof(IntangiblePower), count);
                return;
            case "n_tempdex":
                if (count == 0) return;
                ApplySelf(context, typeof(AnticipatePower), count);
                return;
            case "n_nextturndraw":
                if (count == 0) return;
                ApplySelf(context, typeof(DrawCardsNextTurnPower), count);
                return;
            case "n_keepblocknextturn":
                ApplySelf(context, typeof(BlurPower), 1);
                return;
            case "n_nextturnblock":
                if (count == 0) return;
                ApplySelf(context, typeof(BlockNextTurnPower), count);
                return;
            // ===== 状态牌创建（D:CreateXxxInDiscard 族）=====
            case "d_createdazedindiscard":
                CreateStatusCards<Dazed>(context, count);
                return;
            case "d_createtwowoundsindiscard":
                CreateStatusCards<Wound>(context, count);
                return;
            case "d_createburnindiscard":
                CreateStatusCards<Burn>(context, count);
                return;
            case "d_createslimeindiscard":
                CreateStatusCards<Slimed>(context, count);
                return;
            case "d_createvoidindiscard":
                CreateStatusCards<MegaCrit.Sts2.Core.Models.Cards.Void>(context, count);
                return;
            default:
                throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
        }
    }

    /// <summary>施加 Power 到自身（与源码 PowerCmd.Apply&lt;T&gt;(owner, amount, owner, card) 等价）。</summary>
    private static void ApplySelf(OperationExecutionContext context, Type powerType, int amount)
    {
        var mirror = context.Mirror;
        if (mirror.CombatState is not ICombatPredictionEffectSink effects)
            throw new InvalidOperationException($"Power 施加需要分支战斗状态效果汇：{powerType.Name}。");
        effects.ApplyPowerFromSource(powerType, context.Card.Owner.Creature, amount,
            context.Card.Owner.Creature, context.Card);
    }

    /// <summary>施加 Power 到全部敌人（与源码 foreach HittableEnemies 等价）。</summary>
    private static void ApplyToAll(OperationExecutionContext context, Type powerType, int amount)
    {
        if (amount == 0) return;
        var mirror = context.Mirror;
        if (mirror.CombatState is not ICombatPredictionEffectSink effects)
            throw new InvalidOperationException($"全体 Power 施加需要分支战斗状态效果汇：{powerType.Name}。");
        foreach (var enemy in mirror.CombatState.HittableEnemies)
            effects.ApplyPowerFromSource(powerType, enemy, amount,
                context.Card.Owner.Creature, context.Card);
    }

    /// <summary>创建状态牌到弃牌堆（与源码 CreateDerivatives → AddGeneratedCardToCombat 等价）。</summary>
    private static void CreateStatusCards<TStatus>(OperationExecutionContext context, int count) where TStatus : CardModel
    {
        if (count <= 0) return;
        var mirror = context.Mirror;
        var owner = context.Card.Owner;
        mirror.Simulator.CreateAndAddGeneratedCardsToCombat<TStatus>(
            owner, PileType.Discard, count, owner);
    }
}
