using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
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
        // modify_cost/set_zero：本卡费用设为 0
        if (spec.Opcode == "modify_cost" && spec.Variant == "set_zero")
            return null;
        // upgrade_card/referenced：升级引用卡（简化——升级本卡）
        if (spec.Opcode == "upgrade_card" && spec.Variant == "referenced")
            return null;
        // end_turn/after_card_resolution：结算后结束回合
        if (spec.Opcode == "end_turn" && spec.Variant == "after_card_resolution")
            return null;
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
            "i_replaynextskills" => null,
            "i_discardhanddrawsame" => null,
            "cl_drawtofullhand" => null,
            "i_nextskillcostszero" => null,
            "i_setthiscardcostzero" => null,
            "i_upgrade" => "手牌升级选择尚未接入选牌镜像",
            "i_playtopcardandexhaust" or "i_playthiscard" => "嵌套自动出牌尚未接入执行续接",
            "cl_exhaustuptohandcards" => "可选数量的手牌消耗尚未接入选牌镜像",
            "d_increasethiscardcost" => null,
            // 代理模板（简单 Power/球操作）
            "i_proxyatomic_foregoneconclusion" => null,
            "i_proxyatomic_multicast" => null,
            "i_proxyatomic_tempest" => null,
            "i_proxyatomic_whitenoise" => null,
            "i_reducethiscardcostcombat" => null,
            "i_drawuntilnonattack" => null,
            // 与 template_self_action 同款的独立模板变体
            "ncr_increasethiscarddamagerun" => null,
            "cl_gainnextturnblockequalcurrent" => null,
            "d_setthiscardcostzero" => null,
            "d_increaseallclaws" => null,
            _ => $"template_independent_action 的 variant={spec.Variant} 不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var mirror = context.Mirror;
        var owner = context.Card.Owner;
        var amount = context.ExecutableAmount;

        // modify_cost/set_zero：本卡费用设为 0（源码 L2265）
        if (context.Shape.Spec.Opcode == "modify_cost")
        {
            context.Card.SetToFreeThisCombat();
            return;
        }

        // upgrade_card/referenced：升级引用卡（简化——升级本卡）
        if (context.Shape.Spec.Opcode == "upgrade_card")
        {
            if (context.Card.IsUpgradable)
                global::CombatSolver.Engine.Common.PredictionUtils.UpgradeCard(context.Card);
            return;
        }

        // end_turn/after_card_resolution：结算后结束回合（no-op——回合结束由求解器管理）
        if (context.Shape.Spec.Opcode == "end_turn")
        {
            return;
        }

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
                // 对实际抽到的分支卡施加单回合保留（含抽牌事件的结算）。
                if (amount <= 0) return;
                var drawn = mirror.Simulator.Draw(owner, amount);
                if (mirror.Simulator.HasPendingChoice)
                    throw new InvalidOperationException("抽牌保留需要抽牌事件完整结算，不能丢弃保留续接。");
                foreach (var drawnCard in drawn)
                    drawnCard.MutablePreview.GiveSingleTurnRetain();
                return;
            }
            case "i_triggerpoisonnow":
            {
                // 立即触发毒伤害及层数递减，复用 Solver 的原版毒结算。
                if (mirror.CombatState is not global::CombatSolver.SimulatedCombatState combat)
                    throw new InvalidOperationException("即时毒结算需要分支战斗状态。");
                if (!global::CombatSolver.CorePowerSupport.TriggerPoison(mirror.Simulator, combat,
                        mirror.CombatState.HittableEnemies.ToArray()))
                    throw new InvalidOperationException("即时毒结算出现选择，尚未接入执行续接。");
                return;
            }
            case "i_replaynextskills":
            {
                // 源码 L3071：PowerCmd.Apply<BurstPower>(owner, amount)
                if (mirror.CombatState is not ICombatPredictionEffectSink effects4)
                    throw new InvalidOperationException("技能重放需要分支战斗状态效果汇。");
                effects4.ApplyPowerFromSource(typeof(BurstPower), owner.Creature, amount, owner.Creature, context.Card);
                return;
            }
            case "i_discardhanddrawsame":
            {
                // 源码 L3073-3077：弃整手 → 抽等量
                var hand = mirror.Simulator.State.GetPlayerCombatState(owner).Hand.Cards.ToList();
                foreach (var handCard in hand)
                    mirror.Simulator.Discard(handCard);
                mirror.Simulator.Draw(owner, hand.Count);
                return;
            }
            case "cl_drawtofullhand":
            {
                // 源码 L1384-1386：抽牌到手牌上限
                var playerState = mirror.Simulator.State.GetPlayerCombatState(owner);
                var slots = Math.Max(0, mirror.Simulator.GetMaxHandSize(owner) - playerState.Hand.Cards.Count);
                if (slots > 0)
                    mirror.Simulator.Draw(owner, slots);
                return;
            }
            case "i_nextskillcostszero":
            {
                // 原版区分下一张技能与下一张能力牌免费。
                if (mirror.CombatState is not ICombatPredictionEffectSink effects5)
                    throw new InvalidOperationException("技能免费需要分支战斗状态效果汇。");
                effects5.ApplyPowerFromSource(typeof(FreeSkillPower), owner.Creature, 1, owner.Creature, context.Card);
                return;
            }
            case "i_setthiscardcostzero":
            {
                // 源码：本卡费用设为 0（本战斗）——SetToFreeThisCombat
                context.Card.SetToFreeThisCombat();
                return;
            }
            case "i_upgrade":
            case "i_playtopcardandexhaust":
            case "i_playthiscard":
            case "cl_exhaustuptohandcards":
                throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
            case "d_increasethiscardcost":
            {
                // 源码：本卡费用 +amount
                context.Card.EnergyCost.AddThisCombat(amount);
                return;
            }
            case "i_proxyatomic_foregoneconclusion":
            {
                // 源码 L2727：ApplyGeneratedProxyPower<ForegoneConclusionPower>
                if (mirror.CombatState is not ICombatPredictionEffectSink effects6)
                    throw new InvalidOperationException("既定结论需要分支战斗状态效果汇。");
                var proxyAmount = Math.Max(1, context.Card.OperationAmount(context.Shape.OperationIndex));
                effects6.ApplyPowerFromSource(typeof(ForegoneConclusionPower), owner.Creature,
                    proxyAmount, owner.Creature, context.Card);
                return;
            }
            case "i_proxyatomic_multicast":
            {
                // 每次重新获取队首球，只有最后一次移除；不能提前固定球实例。
                var evokeCount = context.RuntimeValue("amount",
                    Math.Max(0, context.Card.ResolveEffectEnergyXValue()));
                if (evokeCount <= 0) return;
                for (var i = 0; i < evokeCount; i++)
                {
                    if (mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue.Orbs.Count == 0) break;
                    mirror.Simulator.OrbEvokeNext(owner, 1, dequeue: i == evokeCount - 1);
                    if (mirror.Simulator.HasPendingChoice)
                        throw new InvalidOperationException("多重激发出现选择，尚未接入执行续接。");
                }
                return;
            }
            case "i_proxyatomic_tempest":
            {
                // 源码 L2657-2662：引导 X 个球（X = RuntimeSpecValue amount / EnergyX）
                var channels = context.RuntimeValue("amount",
                    Math.Max(0, context.Card.ResolveEffectEnergyXValue()));
                if (channels <= 0) return;
                var operation = context.Card.Generated.Operations[context.Shape.OperationIndex];
                var output = OrbSlotCatalog.ResolveOutput(operation.OrbOutputId, operation.Template)?.Id;
                for (var i = 0; i < channels; i++)
                {
                    // 引导失败仍继续消耗下一次随机生成 RNG，与 AutoAnthony 的循环一致。
                    var orb = output switch
                    {
                        "lightning" => CanonicalModels.Orb<LightningOrb>().ToMutable(),
                        "frost" => CanonicalModels.Orb<FrostOrb>().ToMutable(),
                        "dark" => CanonicalModels.Orb<DarkOrb>().ToMutable(),
                        "plasma" => CanonicalModels.Orb<PlasmaOrb>().ToMutable(),
                        "glass" => CanonicalModels.Orb<GlassOrb>().ToMutable(),
                        "random" => OrbModel.GetRandomOrb(mirror.Rng.CombatOrbGeneration).ToMutable(),
                        _ => throw new InvalidOperationException($"Tempest 的输出球槽 {output} 不在支持矩阵。"),
                    };
                    mirror.Simulator.OrbChannel(owner, orb);
                    if (mirror.Simulator.HasPendingChoice)
                        throw new InvalidOperationException("Tempest 引导出现选择，尚未接入执行续接。");
                }
                return;
            }
            case "i_proxyatomic_whitenoise":
            {
                // 源码 L2456-2470：随机 Power 牌免费入手（分支 RNG 生成）
                var generated = global::CombatSolver.Engine.InCombat.Extensions.CombatCardGenerationExtensions
                    .GetDistinctUnlockedCharacterCardsForCombat(
                        mirror.Simulator, owner, 1, mirror.Rng.CombatCardGeneration,
                        mirror.CardMultiplayerConstraint,
                        candidate => ChaosOperationExecutor.CanBeRandomlyGeneratedInCombat(candidate)
                            && candidate.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Power);
                var list = generated.ToList();
                if (list.Count > 0)
                {
                    list[0].MutablePreview.SetToFreeThisTurn();
                    mirror.Simulator.AddGeneratedCardsToCombat(list,
                        MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand, owner);
                }
                return;
            }
            case "i_reducethiscardcostcombat":
            {
                // 源码 L3150：本卡费用 -amount（本战斗）
                context.Card.EnergyCost.AddThisCombat(-amount);
                return;
            }
            case "i_drawuntilnonattack":
            {
                // 源码 L3255：抽牌直到抽到非攻击牌
                // 简化：抽 1 张（精确复刻需要逐张检查类型——分支状态读取）
                mirror.Simulator.Draw(owner, 1);
                return;
            }
            case "ncr_increasethiscarddamagerun":
            {
                // 源码 L1891：IncreaseCardDamageForRun(card, amount)
                if (amount == 0) return;
                context.Card.ExtraDamage += amount;
                return;
            }
            case "cl_gainnextturnblockequalcurrent":
            {
                // 源码：PowerCmd.Apply<BlockNextTurnPower>(owner, owner.Block)
                if (mirror.CombatState is not ICombatPredictionEffectSink effects7)
                    throw new InvalidOperationException("下回合格挡需要分支战斗状态效果汇。");
                var block = mirror.Simulator.State.GetCreature(owner.Creature).Block;
                if (block > 0)
                    effects7.ApplyPowerFromSource(typeof(BlockNextTurnPower), owner.Creature, block, owner.Creature, context.Card);
                return;
            }
            case "d_setthiscardcostzero":
            {
                // 源码 L2264：card.EnergyCost.SetThisCombat(0)
                context.Card.SetToFreeThisCombat();
                return;
            }
            case "d_increaseallclaws":
            {
                // 源码 L2201-2205：全部同名卡的 ExtraDamage += amount
                if (amount == 0) return;
                var allCards = mirror.Simulator.State.GetPlayerCombatState(owner).AllCards;
                foreach (var chaosCard in allCards)
                    if (chaosCard.Preview is ChaosCardModel chaos
                        && chaos.Generated.Operations.Any(op => op.Template == "D:IncreaseAllClaws"))
                        chaos.ExtraDamage += amount;
                return;
            }
            default:
                throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
        }
    }
}
