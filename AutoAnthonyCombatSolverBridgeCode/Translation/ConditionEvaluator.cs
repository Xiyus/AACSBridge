using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using AutoAnthonyCombatSolverBridge.CombatSolver;

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>Conditions read branch state or the current card's complete local resolution.</summary>
internal static class ConditionEvaluator
{
    internal static bool IsSupportedCondition(string? kind) => kind is
        "exhaust_pile_minimum" or "card_exhausted_this_turn" or "owner_lost_hp_this_turn"
        or "target_has_vulnerable" or "target_has_poison" or "draw_pile_empty"
        or "last_drawn_card_is_skill" or "enemy_intends_attack" or "osty_alive"
        or "doom_applied_this_turn" or "osty_attacked_this_turn" or "no_attacks_in_hand" or "hand_empty"
        // Batch AP：第三批条件
        or "has_frost_orb" or "fatal" or "cards_played_this_turn_at_least"
        or "cards_played_this_turn_below" or "first_play_of_this_card_this_turn" or "energy_x_at_least";

    internal static bool Evaluate(ChaosCardModel card, GeneratorOperation conditionOp,
        CardOnPlayMirrorContext mirror, OperationResolutionState resolution)
    {
        var spec = OperationRuntimeSpecCompiler.RequireStructured(conditionOp);
        var kind = spec.Condition?.Kind;
        if (!IsSupportedCondition(kind))
            throw new UnsupportedRuntimeSpecException(spec.Opcode, kind ?? "missing_condition");
        if (kind == "last_drawn_card_is_skill") return resolution.LastDrawnCardIsSkill;
        var threshold = spec.Values.FirstOrDefault(value => value.Id == "threshold")?.BaseValue ?? 1;
        var owner = card.Owner;
        var playerState = mirror.Simulator.State.GetPlayerCombatState(owner);
        var target = mirror.CardPlay.Target;
        var combat = mirror.CombatState as global::CombatSolver.SimulatedCombatState
            ?? throw new InvalidOperationException("条件需要分支战斗状态。");
        return kind switch
        {
            "exhaust_pile_minimum" => playerState.ExhaustPile.Cards.Count >= threshold,
            "draw_pile_empty" => playerState.DrawPile.IsEmpty,
            "hand_empty" => playerState.Hand.IsEmpty,
            "no_attacks_in_hand" => playerState.Hand.Cards.All(candidate =>
                candidate.Preview.Type != MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack),
            "target_has_vulnerable" => target is not null && combat.GetAmount<VulnerablePower>(target) > 0,
            "target_has_poison" => target is not null && combat.GetAmount<PoisonPower>(target) > 0,
            "enemy_intends_attack" => target is not null && combat.IsEnemyIntendingToAttack(target),
            "osty_alive" => mirror.Simulator.State.GetOsty(owner) is { } osty
                && mirror.Simulator.State.GetCreature(osty).IsAlive,
            "card_exhausted_this_turn" => combat.WasCardExhaustedThisTurn(owner.Creature),
            "owner_lost_hp_this_turn" => combat.HasLostHpThisTurn(owner.Creature),
            "doom_applied_this_turn" => combat.WasDoomAppliedThisTurn(owner.Creature),
            "osty_attacked_this_turn" => mirror.Simulator.State.GetOsty(owner) is { } actor
                && combat.GetCreatureAttacksThisTurn(actor) > 0,
            // Batch AP：第三批条件
            "has_frost_orb" => playerState.OrbQueue.Orbs.Any(orb => orb is FrostOrb),
            "fatal" => resolution.LastAttackKilled,
            "cards_played_this_turn_at_least" => ChaosHistory.Finished(mirror.Simulator, owner).Count() >= threshold,
            "cards_played_this_turn_below" => ChaosHistory.Finished(mirror.Simulator, owner).Count() < threshold,
            "first_play_of_this_card_this_turn" => !ChaosHistory.Finished(mirror.Simulator, owner)
                .Any(play => mirror.Card.References(play.Card)),
            "energy_x_at_least" => card.ResolvedEnergyXValue >= Math.Max(1,
                card.OperationAmount(card.Generated.Operations.ToList().IndexOf(conditionOp))),
            _ => throw new UnsupportedRuntimeSpecException(spec.Opcode, kind!),
        };
    }
}
