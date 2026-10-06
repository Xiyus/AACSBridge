using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CardTag = MegaCrit.Sts2.Core.Entities.Cards.CardTag;

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
            or "ncr_damagepercarddrawnthisturn",
        "modify_hits" => spec.Variant == "flat_extra",
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
        for (var index = 0; index < card.Generated.Operations.Count; index++)
        {
            var modifier = card.Generated.Operations[index];
            if (modifier.Scope != OperationScope.Modifier || modifier.Template == "M:base") continue;
            var spec = ChaosOperationExecutor.EffectiveRuntimeSpec(card, index);
            if (!IsSupportedModifier(spec) || modifier.Parameters.ContainsKey("triggerIndex"))
                throw new UnsupportedRuntimeSpecException(spec.Opcode, spec.Variant);
            var amount = card.OperationAmount(index);
            switch (spec.Variant)
            {
                case "exhaust_pile_scaled" or "m_damageperexhaustcard":
                    damage += (decimal)amount * player.ExhaustPile.Cards.Count;
                    break;
                case "vulnerable_scaled":
                    damage += (decimal)amount * (mirror.CardPlay.Target is { } target
                        ? combat.GetAmount<VulnerablePower>(target) : 0);
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
                    dynamicHits += player.OrbQueue.Orbs.Count;
                    break;
                case "m_repeatperskillinhand":
                    hasDynamicHits = true;
                    dynamicHits += player.Hand.Cards.Count(candidate => candidate.Preview.Type == CardType.Skill);
                    break;
                case "r_repeatperstargainedthisturn":
                    hasDynamicHits = true;
                    dynamicHits += combat.GetStarsGainedThisTurn(owner);
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
                    damage += (decimal)amount * combat.GetNonHandDrawsThisTurn(owner);
                    break;
                case "cl_bonusperuniquedebuff":
                    damage += (decimal)amount * (mirror.CardPlay.Target is { } debuffTarget
                        ? combat.EffectivePowers().Count(power => ReferenceEquals(power.Owner, debuffTarget)
                            && power.Type == PowerType.Debuff && power is not ITemporaryPower) : 0);
                    break;
                case "r_bonusperstarcostcardinhand":
                    // The preceding dependency supplies the count. Standalone has multiplier 1.
                    damage += amount;
                    break;
                case "flat_extra" when spec.Opcode == "modify_hits":
                    additionalHits += Math.Max(1, amount);
                    break;
                default:
                    throw new UnsupportedRuntimeSpecException(spec.Opcode, spec.Variant);
            }
        }
        return (damage, Math.Max(0, (hasDynamicHits ? dynamicHits : Math.Max(0, baseHits)) + additionalHits));
    }
}
