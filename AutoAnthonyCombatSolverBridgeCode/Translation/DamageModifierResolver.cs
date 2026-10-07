using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using CardTag = MegaCrit.Sts2.Core.Entities.Cards.CardTag;
using AutoAnthonyCombatSolverBridge.CombatSolver;

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>Standalone modifiers with exact branch-state counts. Dependency prefixes remain rejected.</summary>
internal static class DamageModifierResolver
{
    internal static bool IsSupportedModifier(OperationRuntimeSpec spec) => spec.Opcode switch
    {
        "template_modifier" => spec.Variant is "exhaust_pile_scaled" or "m_damageperexhaustcard"
            or "vulnerable_scaled" or "strike_count_scaled" or "m_damageminuspercardinhand"
            or "d_repeatperorb" or "m_repeatperskillinhand" or "m_damageperdiscardthisturn"
            or "m_damagepercarddrawncombat" or "cl_bonusperuniquedebuff"
            or "r_bonusperstarcostcardinhand" or "r_repeatperstargainedthisturn"
            or "ncr_damagepercarddrawnthisturn"
            // 0.9.x 扩展：非 Osty 修饰符（第二批）
            or "ncr_ostymaxhpbonusdamage"
            or "ncr_ostycurrenthpbonusdamage"
            or "ncr_repeatpervoidplayedcombat"
            or "r_damageupwhendrawn"
            // Batch AO：ForEach/依赖前缀族（第三批）
            or "d_foreachorb" or "d_foreachuniqueorb"
            or "ncr_foreachostyattackcard" or "ncr_damageperostyattackcard"
            or "ncr_foreachexhaustedsoul" or "ncr_damageperexhaustedsoul"
            or "r_foreachskillplayedthisturn" or "r_foreachgeneratedcardcombat"
            or "cl_foreachdrawpilecard" or "r_wheneverdrawn"
            or "r_bonuspergeneratedcardthiscombat" or "m_repeatperattackthisturn" or "r_repeatperskillplayedthisturn"
            or "ncr_repeatperostyattackthisturn" or "m_repeatareaonkill" or "r_doubleenergyx" or "cl_increaserollingdamage",
        "modify_x" => spec.Variant == "double_at_threshold",
        "modify_power" => spec.Variant == "doom_per_threshold",
        "modify_hits" => spec.Variant is "flat_extra" or "hp_loss_scaled",
        "modify_damage" => spec.Variant is
            "vulnerable_scaled" or "strike_count_scaled" or "current_block"
            or "triggered_attack_percentage",
        "modify_orb_slots" => spec.Variant == "loss",
        "modify_block" => spec.Variant == "strength_scaled",
        _ => false,
    };

    internal static (decimal Damage, int Hits) Resolve(OperationExecutionContext context,
        decimal baseDamage, int baseHits)
    {
        var card = context.Card;
        var mirror = context.Mirror;
        var owner = card.Owner;
        var damage = baseDamage + card.ExtraDamage;
        var dynamicHits = 0;
        var hasDynamicHits = false;
        var additionalHits = 0;
        var player = mirror.Simulator.State.GetPlayerCombatState(owner);
        var combat = mirror.CombatState as global::CombatSolver.SimulatedCombatState
            ?? throw new InvalidOperationException("修饰符需要分支战斗状态。");
        if (ChaosOperationExecutor.IsRepeatedDependencyDamagePayoff(card, context.Shape.OperationIndex))
            baseHits *= DependencyResolver.Multiplier(context);
        for (var index = 0; index < card.Generated.Operations.Count; index++)
        {
            var modifier = card.Generated.Operations[index];
            if (modifier.Scope != OperationScope.Modifier) continue;
            if (CardEffectRules.IsDependencyPrefix(modifier) || ChaosCardPassiveMirror.IsOperation(modifier.Template)) continue;
            if (!ChaosOperationExecutor.DamageModifierSharesResolution(card.Generated.Operations, index, context.Shape.OperationIndex)) continue;
            if (modifier.Parameters.TryGetValue("triggerIndex", out var gate) && gate >= 0 && gate < card.Generated.Operations.Count
                && card.Generated.Operations[gate].RuntimeSpec?.Condition is { })
                if (!ConditionEvaluator.Evaluate(card, card.Generated.Operations[gate], mirror, context.Resolution ?? new OperationResolutionState())) continue;
            var spec = ChaosOperationExecutor.EffectiveRuntimeSpec(card, index);
            if (spec.Opcode is "modify_block" or "modify_orb_slots" or "modify_x" or "modify_power"
                || spec.Variant is "triggered_attack_percentage" or "m_repeatareaonkill" or "r_doubleenergyx" or "cl_increaserollingdamage") continue;
            if (!IsSupportedModifier(spec))
                throw new UnsupportedRuntimeSpecException(spec.Opcode, spec.Variant);
            var rawAmount = card.OperationAmount(index);
            var repeats = DependencyResolver.ModifierMultiplier(context, index);
            var amount = rawAmount * repeats;
            switch (spec.Variant)
            {
                case "exhaust_pile_scaled" or "m_damageperexhaustcard":
                    damage += (decimal)amount * player.ExhaustPile.Cards.Count;
                    break;
                case "vulnerable_scaled":
                    damage += (decimal)amount * (context.Target is { } target
                        ? combat.GetAmount<VulnerablePower>(target) : 0);
                    break;
                case "current_block":
                    if (CardEffectRules.CurrentBlockDamageAnchorIndex(card.Generated.Operations, index) == context.Shape.OperationIndex)
                        damage = mirror.Simulator.State.GetCreature(owner.Creature).Block;
                    break;
                case "strike_count_scaled":
                    damage += (decimal)amount * player.AllCards.Count(candidate => candidate.Preview.Tags.Contains(CardTag.Strike));
                    break;
                case "m_damageminuspercardinhand":
                    var handCount = player.Hand.Cards.Count;
                    if (player.Hand.Cards.Contains(mirror.Card)) handCount--;
                    damage = Math.Max(0, damage - (decimal)amount * handCount);
                    break;
                case "d_repeatperorb":
                    hasDynamicHits = true;
                    dynamicHits += RepeatCount(player.OrbQueue.Orbs.Count);
                    break;
                case "m_repeatperskillinhand":
                    hasDynamicHits = true;
                    dynamicHits += player.Hand.Cards.Count(candidate => candidate.Preview.Type == CardType.Skill) * repeats;
                    break;
                case "m_repeatperattackthisturn":
                    hasDynamicHits = true;
                    dynamicHits += ChaosHistory.Finished(mirror.Simulator, owner).Count(play => play.Card.Type == CardType.Attack) * repeats;
                    break;
                case "r_repeatperskillplayedthisturn":
                    hasDynamicHits = true;
                    dynamicHits += RepeatCount(ChaosHistory.Finished(mirror.Simulator, owner).Count(play => play.Card.Type == CardType.Skill));
                    break;
                case "ncr_repeatperostyattackthisturn":
                    additionalHits += RepeatCount(ChaosHistory.Finished(mirror.Simulator, owner).Count(play => play.Card.Tags.Contains(CardTag.OstyAttack)));
                    break;
                case "r_repeatperstargainedthisturn":
                    hasDynamicHits = true;
                    dynamicHits += RepeatCount(combat.GetStarsGainedThisTurn(owner));
                    break;
                case "m_damageperdiscardthisturn":
                    damage += (decimal)amount * combat.GetCardsDiscardedThisTurn(owner.Creature);
                    break;
                case "m_damagepercarddrawncombat":
                    damage += (decimal)amount * (combat.GetCardsDrawnBeforePrediction(owner)
                        + mirror.Simulator.History.Entries.OfType<CombatPredictionCardDrawnEntry>()
                            .Count(entry => entry.Card.Owner == owner));
                    break;
                case "ncr_damagepercarddrawnthisturn":
                    damage += (decimal)rawAmount * RepeatCount(combat.GetNonHandDrawsThisTurn(owner));
                    break;
                case "cl_bonusperuniquedebuff":
                    damage += (decimal)amount * (context.Target is { } debuffTarget
                        ? combat.EffectivePowers().Count(power => ReferenceEquals(power.Owner, debuffTarget)
                            && power.Type == PowerType.Debuff && power is not ITemporaryPower) : 0);
                    break;
                case "r_bonusperstarcostcardinhand":
                    // The preceding dependency supplies the count. Standalone has multiplier 1.
                    damage += amount;
                    break;
                case "ncr_ostymaxhpbonusdamage":
                    // 源码 L3414-3415：D += Osty.MaxHp（Osty 不存在时 +0）
                    damage += mirror.Simulator.State.GetOsty(owner) is { } ostyMax
                        ? mirror.Simulator.State.GetCreature(ostyMax).MaxHp : 0;
                    break;
                case "ncr_ostycurrenthpbonusdamage":
                    // 源码 L3416-3417：D += Osty.CurrentHp（Osty 不存在时 +0）
                    damage += mirror.Simulator.State.GetOsty(owner) is { } ostyCur
                        ? mirror.Simulator.State.GetCreature(ostyCur).CurrentHp : 0;
                    break;
                case "ncr_repeatpervoidplayedcombat":
                    hasDynamicHits = true;
                    dynamicHits += RepeatCount(ChaosHistory.Finished(mirror.Simulator, owner, thisTurn: false)
                        .Count(play => ChaosHistory.CurrentCard(mirror.Simulator, play).Keywords.Contains(CardKeyword.Ethereal)));
                    break;
                case "r_damageupwhendrawn":
                    // 源码 ChaosCardModel L844-846：抽到时 ExtraDamage += max(0,A)
                    // 已在 ExtraDamage 中体现（每次抽到叠加）——此处无额外操作
                    break;
                // Batch AO：ForEach/依赖前缀族（第三批）
                case "d_foreachorb":
                    // 源码 L2369 与 D:RepeatPerOrb 同组：H_dyn += 球队列球数
                    hasDynamicHits = true;
                    dynamicHits += player.OrbQueue.Orbs.Count;
                    break;
                case "d_foreachuniqueorb":
                    // 源码 L3754：H_dyn += 唯一球类型数
                    hasDynamicHits = true;
                    dynamicHits += player.OrbQueue.Orbs.Select(orb => orb.Id).Distinct().Count();
                    break;
                case "ncr_foreachostyattackcard":
                case "ncr_foreachexhaustedsoul":
                case "cl_foreachdrawpilecard":
                case "r_wheneverdrawn":
                    // 依赖前缀：计数由消费方（DamagePer 变体）自行解析，前缀本身 no-op
                    // 源码 L3472-3477：外部缩放伤害依赖为空分支
                    break;
                case "ncr_damageperostyattackcard":
                    // 源码 L3440-3445：damage += amount × OstyAttack 卡数（排除自身）
                    damage += amount;
                    break;
                case "ncr_damageperexhaustedsoul":
                    // 源码 L3432-3438：damage += amount × 消耗堆 Soul 衍生卡数
                    damage += amount;
                    break;
                case "r_foreachskillplayedthisturn":
                    // 源码 L3449-3455（等价 R:RepeatPerSkillPlayedThisTurn）：H_dyn += 本回合技能出牌数
                    hasDynamicHits = true;
                    dynamicHits += ChaosHistory.Finished(mirror.Simulator, owner)
                        .Count(play => play.Card.Type == CardType.Skill);
                    break;
                case "r_foreachgeneratedcardcombat":
                case "r_bonuspergeneratedcardthiscombat":
                    // 源码 L3466-3468：damage += amount × 本战斗生成卡数（根历史 + 模拟内）
                    damage += (decimal)amount * (combat.GetCardsGeneratedBeforePrediction(owner)
                        + mirror.Simulator.History.Entries.OfType<CombatPredictionCardGeneratedEntry>()
                            .Count(entry => entry.Creator == owner));
                    break;
                case "hp_loss_scaled" when spec.Opcode == "modify_hits":
                    // 源码 L3385-3391：H += amount × HP 损失事件数
                    additionalHits += Math.Max(0, rawAmount) * repeats
                        * (combat._rootHistory.DamageReceived.Count(entry => entry.Receiver == owner.Creature && entry.Result.UnblockedDamage > 0)
                            + mirror.Simulator.History.Entries.OfType<CombatPredictionDamageReceivedEntry>()
                                .Count(entry => entry.Receiver == owner.Creature && entry.Result.UnblockedDamage > 0));
                    break;
                case "flat_extra" when spec.Opcode == "modify_hits":
                    additionalHits += Math.Max(1, rawAmount) * repeats;
                    break;
                default:
                    throw new UnsupportedRuntimeSpecException(spec.Opcode, spec.Variant);
            }
            int RepeatCount(int liveCount) => CardEffectRules.ResolveDependencyRepeatCount(
                DependencyResolver.Prefix(card, index) is { } prefix && CardEffectRules.IsMultiplicativeDependencyPrefix(prefix), liveCount, repeats);
        }
        return (damage, Math.Max(0, (hasDynamicHits ? dynamicHits : Math.Max(0, baseHits)) + additionalHits));
    }
}
