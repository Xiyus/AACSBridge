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
using AutoAnthonyCombatSolverBridge.CombatSolver;
using MegaCrit.Sts2.Core.Extensions;

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
                    or "a_proxyatomic_calcify" or "a_proxyatomic_swordsage" or "a_proxyatomic_forbiddengrimoire" or "first_cards_free_each_turn"
                    or "kings_sword_hits_all"
                    // A:rule 族（ApplyBoundPower 一行式）
                    or "poison_extra_triggers" or "derivative_bonus_damage"
                    or "derivative_hits_all" or "played_skills_gain_sly"
                    or "derivative_retain" => null,
                _ => $"combat_rule 的 variant={spec.Variant} 不在支持矩阵",
            };
        return spec.Variant switch
        {
            "d_channelfrost" or "d_channeldark" or "d_channellightning"
                or "d_channelglass" or "d_channelplasma" or "d_channelrandom" => null,
            "d_gainfocus" => null,
            "d_gaintemporaryfocus" => null,
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
            // 更多球激发/简单变体
            "d_evokeleftmostorb" => null,
            "d_evokealltwice" => null,
            "n_createinkshiv" => null,
            "n_blockequalallpoison" => null,
            "d_exhaustallstatuses" => null,
            "d_shuffleallunexhaustedintodraw" => null,
            // 卡牌创建/生成/返回
            "r_putkingsswordinhand" => null,
            "d_addrandompowertohand" => null,
            "cl_addrandomattacktohand" => null,
            "d_returnzerocostdiscardtohand" => null,
            // 更多卡牌创建/简单变体
            "r_adddebristohand" => null,
            "d_createzerocostcopyindiscard" => null,
            "ncr_createsoulindiscard" => null,
            "ncr_createsoulindraw" => null,
            "ncr_createsoulinhand" => null,
            // ncr_* 简单变体（非 Osty 依赖）
            "ncr_increasethiscarddamagerun" => null,
            "ncr_allenemiesloseeventhp" => null,
            "ncr_killenemiesatdoomthreshold" => null,
            // 更多非 Osty 简单变体
            "r_fillhandwithdebris" => null,
            "ncr_increaseallcardcoststhisturn" => null,
            "ncr_addrandometherealcardtohand" => null,
            // Osty 伤害（模拟器已追踪 Osty creature）
            "ncr_ostyalldamage" => null,
            // Osty 生命管理（模拟器追踪 Osty HP）
            "ncr_healosty" => null,
            "ncr_killosty" => null,
            // Osty 召唤（模拟器原生 SummonOsty API）
            "ncr_summon" => null,
            "ncr_summonx" => null,
            "ncr_createsoulindrawx" => null,
            // 更多简单变体
            "ncr_blocktripleostymaxhp" => null,
            "ncr_addsweepinggazetohand" => null,
            "ncr_upgraderandomdiscardcards" => null,
            "cl_gainblockequaldamage" => null,
            "cl_damageotherenemiesequal" => null,
            "d_triggerdarkpassives" => null,
            "cl_playtopdrawcard" => null,
            _ => $"template_self_action 的 variant={spec.Variant} 不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var mirror = context.Mirror;
        var owner = context.Card.Owner;
        var amount = context.ExecutableAmount;
        // 源码门控：ExecutableOrbRepeatCount(amount) = Math.Max(0, amount)，== 0 时跳过
        var count = Math.Max(0, amount);
        var producer = context.Card.Generated.Operations[context.Shape.OperationIndex];
        if (context.Shape.Spec.Variant is "d_channelfrost" or "d_channeldark" or "d_channellightning"
            or "d_channelglass" or "d_channelplasma" or "d_channelrandom")
        {
            var output = OrbSlotCatalog.ResolveOutput(producer.OrbOutputId, producer.Template)?.Id
                ?? throw new InvalidOperationException($"引导操作 {producer.Template} 缺少输出球槽");
            for (var repeat = 0; repeat < count; repeat++)
            {
                var orb = output switch
                {
                    "lightning" => CanonicalModels.Orb<LightningOrb>().ToMutable(),
                    "frost" => CanonicalModels.Orb<FrostOrb>().ToMutable(),
                    "dark" => CanonicalModels.Orb<DarkOrb>().ToMutable(),
                    "plasma" => CanonicalModels.Orb<PlasmaOrb>().ToMutable(),
                    "glass" => CanonicalModels.Orb<GlassOrb>().ToMutable(),
                    "random" => OrbModel.GetRandomOrb(mirror.Rng.CombatOrbGeneration).ToMutable(),
                    _ => throw new InvalidOperationException($"未知输出球槽 {output}")
                };
                mirror.Simulator.OrbChannel(owner, orb);
                if (mirror.Simulator.HasPendingChoice) return;
            }
            return;
        }
        if (ChaosOperationExecutor.SimpleHandDerivativeProducerTemplates.Contains(producer.Template))
        {
            ChaosDerivativeMirror.Add(mirror.Simulator, context.Card, context.Shape.OperationIndex, PileType.Hand,
                ChaosOperationExecutor.ExecutableOperationCount(producer, context.ExecutableAmount));
            return;
        }

        // combat_rule 的代理 Power 模板（A:ProxyAtomic 族）
        if (context.Shape.Spec.Opcode == "combat_rule")
        {
            var proxyAmount = context.Shape.Spec.Variant.StartsWith("a_proxyatomic_", StringComparison.Ordinal)
                ? Math.Max(1, context.Card.OperationAmount(context.Shape.OperationIndex)) : amount;
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
                case "a_proxyatomic_forbiddengrimoire":
                    ApplySelf(context, typeof(ForbiddenGrimoirePower), proxyAmount);
                    return;
                case "first_cards_free_each_turn":
                    global::CombatSolver.TurnStartPowerSupport.PrepareVoidFormApplication(mirror.Simulator,
                        (global::CombatSolver.SimulatedCombatState)mirror.CombatState, owner.Creature);
                    ApplySelf(context, typeof(VoidFormPower), Math.Max(1, context.Card.OperationAmount(context.Shape.OperationIndex)));
                    return;
                // 简单 combat_rule 规则
                case "retain_hand_at_turn_end":
                    ApplySelf(context, typeof(RetainHandPower), 1);
                    return;
                case "retain_block_between_turns":
                    ApplySelf(context, typeof(BlurPower), 1);
                    return;
                case "kings_sword_hits_all":
                    ApplySelf(context, typeof(SeekingEdgePower), 1);
                    return;
                case "skills_cost_zero":
                    ApplySelf(context, typeof(FreeSkillPower), 1);
                    return;
                // A:rule 族（ApplyBoundPower 一行式）
                case "poison_extra_triggers":
                    ApplySelf(context, typeof(AccelerantPower), proxyAmount);
                    return;
                case "derivative_bonus_damage":
                    ApplySelf(context, typeof(AccuracyPower), proxyAmount);
                    return;
                case "derivative_hits_all":
                    ApplySelf(context, typeof(FanOfKnivesPower), 1);
                    return;
                case "played_skills_gain_sly":
                    ApplySelf(context, typeof(MasterPlannerPower), 1);
                    return;
                case "derivative_retain":
                    ApplySelf(context, typeof(PhantomBladesPower), proxyAmount);
                    return;
                default:
                    throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
            }
        }

        switch (context.Shape.Spec.Variant)
        {
            case "d_gainfocus":
            {
                // 源码：PowerCmd.Apply<FocusPower>(ctx, owner, amount, owner, card)
                if (mirror.CombatState is not ICombatPredictionEffectSink effects)
                    throw new InvalidOperationException("聚焦需要分支战斗状态效果汇。");
                effects.ApplyPowerFromSource(typeof(FocusPower), owner.Creature, amount, owner.Creature, context.Card);
                return;
            }
            case "d_gaintemporaryfocus":
                ((global::CombatSolver.SimulatedCombatState)mirror.CombatState)
                    .ApplyTemporaryFocus<ChaosTemporaryFocusPower>(owner.Creature, amount, owner.Creature);
                return;
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
                var operation = context.Card.Generated.Operations[context.Shape.OperationIndex];
                var evokeCount = ChaosOperationExecutor.OrbEvokeRepeatCount(operation, amount);
                if (evokeCount == 0) return;
                var orbQueue = mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue;
                if (orbQueue.Orbs.Count == 0) return;
                for (var i = 0; i < evokeCount; i++)
                {
                    mirror.Simulator.OrbEvokeNext(owner, 1, dequeue: i == evokeCount - 1);
                    if (mirror.Simulator.HasPendingChoice)
                        return;
                }
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
                ApplySelf(context, typeof(VeilpiercerPower), DependencyResolver.Multiplier(context));
                return;
            case "d_nextpowercostszero":
                ApplySelf(context, typeof(FreePowerPower), DependencyResolver.Multiplier(context));
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
            case "d_evokeleftmostorb":
            {
                var operation = context.Card.Generated.Operations[context.Shape.OperationIndex];
                var evokeCount = ChaosOperationExecutor.OrbEvokeRepeatCount(operation, amount);
                if (evokeCount == 0) return;
                var orbQueue = mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue;
                if (orbQueue.Orbs.Count == 0) return;
                for (var i = 0; i < evokeCount; i++)
                {
                    if (orbQueue.Orbs.Count == 0) break;
                    mirror.Simulator.OrbEvoke(owner, orbQueue.Orbs[^1], dequeue: i == evokeCount - 1);
                    if (mirror.Simulator.HasPendingChoice)
                        return;
                }
                return;
            }
            case "d_evokealltwice":
            {
                // 源码 L2279：全部球各激发 repeats 次
                var orbQueue2 = mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue;
                var orbCount = orbQueue2.Orbs.Count;
                if (count == 0 || orbCount == 0) return;
                for (var i = 0; i < orbCount; i++)
                    for (var repeat = 0; repeat < count; repeat++)
                    {
                        mirror.Simulator.OrbEvokeNext(owner, 1, dequeue: repeat == count - 1);
                        if (mirror.Simulator.HasPendingChoice)
                            return;
                    }
                return;
            }
            case "n_createinkshiv":
                throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
            case "n_blockequalallpoison":
            {
                // 源码 L697-699：格挡 = 全部敌人毒层数之和
                if (mirror.CombatState is not global::CombatSolver.SimulatedCombatState poisonCombat)
                    return;
                var block = 0;
                foreach (var enemy in mirror.CombatState.HittableEnemies)
                    block += poisonCombat.GetAmount<PoisonPower>(enemy);
                if (block > 0)
                    mirror.Simulator.GainBlock(owner.Creature, block, context.BlockProps,
                        context.Mirror.Card, context.Mirror.CardPlay);
                return;
            }
            case "d_exhaustallstatuses":
            {
                // 源码 L2243：消耗所有非已消耗的状态牌
                var playerState = mirror.Simulator.State.GetPlayerCombatState(owner);
                var statuses = playerState.AllCards
                    .Where(candidate => candidate.Preview.Type == CardType.Status
                        && !playerState.ExhaustPile.Cards.Contains(candidate))
                    .ToList();
                foreach (var status in statuses)
                    mirror.Simulator.Exhaust(status);
                return;
            }
            case "d_shuffleallunexhaustedintodraw":
            {
                // 直接移动到抽牌堆，不能触发弃牌事件。
                var hand = mirror.Simulator.State.GetPlayerCombatState(owner).Hand.Cards.ToList();
                foreach (var handCard in hand)
                    mirror.Simulator.AddToPile(handCard, PileType.Draw);
                mirror.Simulator.Shuffle(owner);
                return;
            }
            case "r_putkingsswordinhand":
            {
                // 源码 L1611：将君王之刃放入手牌（有匹配卡时）
                mirror.Simulator.CreateAndAddGeneratedCardsToCombat<SovereignBlade>(
                    owner, PileType.Hand, 1, owner);
                return;
            }
            case "d_addrandompowertohand":
            {
                // 源码 L2166-2176：随机 Power 牌入手（分支 RNG 生成）
                if (count == 0) return;
                var generated = global::CombatSolver.Engine.InCombat.Extensions.CombatCardGenerationExtensions
                    .GetDistinctUnlockedCharacterCardsForCombat(
                        mirror.Simulator, owner, count, mirror.Rng.CombatCardGeneration,
                        mirror.CardMultiplayerConstraint,
                        candidate => ChaosOperationExecutor.CanBeRandomlyGeneratedInCombat(candidate)
                            && candidate.Type == CardType.Power);
                mirror.Simulator.AddGeneratedCardsToCombat(generated.ToList(), PileType.Hand, owner);
                return;
            }
            case "cl_addrandomattacktohand":
            {
                // 源码 L1283-1293：随机攻击牌入手（分支 RNG 生成）
                if (count == 0) return;
                var generated = global::CombatSolver.Engine.InCombat.Extensions.CombatCardGenerationExtensions
                    .GetDistinctUnlockedCharacterCardsForCombat(
                        mirror.Simulator, owner, count, mirror.Rng.CombatCardGeneration,
                        mirror.CardMultiplayerConstraint,
                        candidate => ChaosOperationExecutor.CanBeRandomlyGeneratedInCombat(candidate)
                            && candidate.Type == CardType.Attack);
                mirror.Simulator.AddGeneratedCardsToCombat(generated.ToList(), PileType.Hand, owner);
                return;
            }
            case "d_returnzerocostdiscardtohand":
            {
                // 源码 L2152-2158：从弃牌堆返回零费牌到手（选择型——走选牌机制）
                return;
            }
            case "r_adddebristohand":
            {
                // 源码：SimpleHandDerivativeProducerTemplates → CreateDerivatives → Debris 入手
                if (count <= 0) return;
                mirror.Simulator.CreateAndAddGeneratedCardsToCombat<Debris>(
                    owner, PileType.Hand, count, owner);
                return;
            }
            case "d_createzerocostcopyindiscard":
            {
                // 源码 L2145-2150：本卡克隆 + 零费 → 弃牌堆
                var clone = context.Mirror.Card.CreateClone();
                clone.MutablePreview.SetToFreeThisCombat();
                mirror.Simulator.AddGeneratedCardToCombat(clone, PileType.Discard, owner);
                return;
            }
            case "ncr_createsoulindiscard":
            {
                // 源码：CreateDerivatives → Soul → 弃牌堆
                if (count <= 0) return;
                ChaosDerivativeMirror.Add(mirror.Simulator, context.Card, context.Shape.OperationIndex, PileType.Discard, count);
                return;
            }
            case "ncr_createsoulindraw":
            {
                // AA inserts each derivative at a random draw-pile position, consuming Shuffle RNG.
                if (count <= 0) return;
                ChaosDerivativeMirror.Add(mirror.Simulator, context.Card, context.Shape.OperationIndex, PileType.Draw, count,
                    CardPilePosition.Random);
                return;
            }
            case "ncr_createsoulinhand":
            {
                // 源码：CreateDerivatives → Soul → 手牌
                if (count <= 0) return;
                mirror.Simulator.CreateAndAddGeneratedCardsToCombat<Soul>(
                    owner, PileType.Hand, count, owner);
                return;
            }
            case "ncr_increasethiscarddamagerun":
            {
                // 源码 L1891：IncreaseCardDamageForRun(card, amount)
                if (count == 0) return;
                context.Card.ExtraDamage += count;
                return;
            }
            case "ncr_allenemiesloseeventhp":
            {
                if (context.EventAmount > 0)
                    foreach (var enemy in mirror.CombatState.HittableEnemies.ToArray())
                    {
                        mirror.Simulator.Damage([enemy], context.EventAmount, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unblockable
                            | MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, owner.Creature, mirror.Card, mirror.CardPlay);
                        if (mirror.Simulator.HasPendingChoice) return;
                    }
                return;
            }
            case "ncr_killenemiesatdoomthreshold":
            {
                // 源码 L1811：击杀所有 Doom >= CurrentHp 的敌人
                if (mirror.CombatState is global::CombatSolver.SimulatedCombatState killCombat)
                {
                    foreach (var enemy in mirror.CombatState.HittableEnemies.ToList())
                    {
                        var doom = killCombat.GetAmount<DoomPower>(enemy);
                        var hp = mirror.Simulator.State.GetCreature(enemy).CurrentHp;
                        if (doom >= hp)
                            mirror.Simulator.Kill(enemy);
                    }
                }
                return;
            }
            case "r_fillhandwithdebris":
            {
                // 源码：填满手牌（到上限）的 Debris
                var handCount = mirror.Simulator.State.GetPlayerCombatState(owner).Hand.Cards.Count;
                var maxHand = mirror.Simulator.GetMaxHandSize(owner);
                var debrisCount = Math.Max(0, maxHand - handCount);
                if (debrisCount > 0)
                    mirror.Simulator.CreateAndAddGeneratedCardsToCombat<Debris>(
                        owner, PileType.Hand, debrisCount, owner);
                return;
            }
            case "ncr_increaseallcardcoststhisturn":
            {
                // 源码 L1718：全部手牌费用 +amount（本回合）
                if (count == 0) return;
                var hand = mirror.Simulator.State.GetPlayerCombatState(owner).Hand.Cards;
                foreach (var handCard in hand)
                    handCard.MutablePreview.EnergyCost.AddThisTurn(count);
                return;
            }
            case "ncr_addrandometherealcardtohand":
            {
                // 源码：随机 Ethereal 牌入手（分支 RNG 生成）
                if (count == 0) return;
                var generated = global::CombatSolver.Engine.InCombat.Extensions.CombatCardGenerationExtensions
                    .GetDistinctUnlockedCharacterCardsForCombat(
                        mirror.Simulator, owner, count, mirror.Rng.CombatCardGeneration,
                        mirror.CardMultiplayerConstraint,
                        candidate => ChaosOperationExecutor.CanBeRandomlyGeneratedInCombat(candidate)
                            && candidate.Keywords.Contains(CardKeyword.Ethereal));
                mirror.Simulator.AddGeneratedCardsToCombat(generated.ToList(), PileType.Hand, owner);
                return;
            }
            case "ncr_ostyalldamage":
            {
                OstyDamageResolver.Execute(context, null);
                return;
            }
            case "ncr_healosty":
            {
                // 源码：治疗 Osty（模拟器 SimCreatureState 支持 HP 修改）
                var osty = mirror.Simulator.State.GetOsty(owner);
                if (osty is null) return;
                if (count > 0)
                    mirror.Simulator.Heal(osty, count);
                return;
            }
            case "ncr_killosty":
            {
                // 源码：杀死 Osty
                var osty = mirror.Simulator.State.GetOsty(owner);
                if (osty is null) return;
                mirror.Simulator.Kill(osty);
                return;
            }
            case "ncr_summon":
            {
                // 源码：OstyCmd.Summon(ctx, owner, amount, card)
                // 镜像：模拟器原生 SummonOsty（创建 Osty 或增加已有 Osty 的最大生命）
                if (count <= 0) return;
                if (mirror.CombatState is global::CombatSolver.SimulatedCombatState summonCombat)
                    summonCombat.SummonOsty(mirror.Simulator, owner, count);
                return;
            }
            case "ncr_summonx":
            {
                // 源码：X 次召唤，每次 summonAmount HP
                var summonAmount = context.RuntimeValue("amount", Math.Max(1, count));
                var x = context.RuntimeValue("hits", context.Card.ResolveEffectEnergyXValue());
                if (mirror.CombatState is global::CombatSolver.SimulatedCombatState summonXCombat)
                    for (var i = 0; i < x; i++)
                        summonXCombat.SummonOsty(mirror.Simulator, owner, summonAmount);
                return;
            }
            case "ncr_createsoulindrawx":
            {
                // 源码：X 张 Soul → 抽牌堆（随机位置）
                var soulCount = context.RuntimeValue("amount",
                    context.Card.ResolveEffectEnergyXValue());
                if (soulCount <= 0) return;
                ChaosDerivativeMirror.Add(mirror.Simulator, context.Card, context.Shape.OperationIndex, PileType.Draw, soulCount,
                    CardPilePosition.Random);
                return;
            }
            case "ncr_blocktripleostymaxhp":
            {
                // 源码 L1866-1870：格挡 = Osty 最大生命 × multiplier
                var osty = mirror.Simulator.State.GetOsty(owner);
                if (osty is null) return;
                var ostyMaxHp = mirror.Simulator.State.GetCreature(osty).MaxHp;
                if (ostyMaxHp > 0)
                    mirror.Simulator.GainBlock(owner.Creature, ostyMaxHp * 3,
                        context.BlockProps, context.Mirror.Card, context.Mirror.CardPlay);
                return;
            }
            case "ncr_addsweepinggazetohand":
            {
                // 源码：SweepingGaze 入手（SimpleHandDerivativeProducerTemplates）
                mirror.Simulator.CreateAndAddGeneratedCardsToCombat<SweepingGaze>(
                    owner, PileType.Hand, 1, owner);
                return;
            }
            case "ncr_upgraderandomdiscardcards":
            {
                var discard = mirror.Simulator.State.GetPlayerCombatState(owner).DiscardPile.Cards;
                var selected = discard.Where(card => card.Preview.IsUpgradable).TakeRandom(Math.Max(0, amount), mirror.Rng.CombatCardSelection).ToList();
                foreach (var card in selected) mirror.Simulator.Upgrade(card);
                return;
            }
            case "cl_gainblockequaldamage":
            {
                // 源码 L1276-1279：格挡 = 本次伤害量（state.LastDamageDealt）
                mirror.Simulator.GainBlock(owner.Creature, context.Resolution?.LastDamageDealt ?? 0,
                    context.BlockProps, context.Mirror.Card, context.IsTriggered ? null : context.Mirror.CardPlay);
                return;
            }
            case "cl_damageotherenemiesequal":
            {
                // 源码 L1339-1343：对其他敌人造成等量伤害
                var target = context.Target;
                var others = mirror.CombatState.HittableEnemies
                    .Where(e => !ReferenceEquals(e, target)).ToArray();
                if (others.Length > 0 && context.Resolution?.LastDamageDealt > 0)
                    mirror.Simulator.Damage(others, context.Resolution.LastDamageDealt,
                        MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered | MegaCrit.Sts2.Core.ValueProps.ValueProp.Move,
                        owner.Creature, context.Mirror.Card, context.Mirror.CardPlay);
                return;
            }
            case "d_triggerdarkpassives":
            {
                // 源码 L2237-2240：触发所有暗球的被动
                var orbQueue = mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue;
                for (var repeat = 0; repeat < ChaosOperationExecutor.UpgradedOperationRepeatCount(context.Card, context.Shape.OperationIndex); repeat++)
                    foreach (var orb in orbQueue.Orbs.Where(orb => ChaosOrbResolver.MatchesSource(orb, context.Operation)).ToList())
                    {
                        mirror.Simulator.OrbPassive(orb);
                        if (mirror.Simulator.HasPendingChoice) return;
                    }
                return;
            }
            case "cl_playtopdrawcard":
            {
                new AutoPlayHandler().Execute(context);
                return;
            }
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
