using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using AutoAnthonyCombatSolverBridge.CombatSolver;
using CardTag = MegaCrit.Sts2.Core.Entities.Cards.CardTag;

namespace AutoAnthonyCombatSolverBridge.Translation;

internal static class DependencyResolver
{
    internal static GeneratorOperation? Prefix(ChaosCardModel card, int index)
        => ChaosOperationExecutor.DependencyPrefix(card, index);

    internal static bool Matches(OperationExecutionContext context)
    {
        var prefix = Prefix(context.Card, context.Shape.OperationIndex);
        if (prefix is null) return true;
        if (prefix.Template == "D:IfHasFrost")
            return context.Mirror.OwnerState.OrbQueue.Orbs.Any(orb => ChaosOrbResolver.MatchesSource(orb, prefix));
        if (prefix.RuntimeSpec?.Condition is null) return true;
        return ConditionEvaluator.Evaluate(context.Card, prefix, context.Mirror, context.Resolution ?? new OperationResolutionState());
    }

    internal static int Multiplier(OperationExecutionContext context, int? indexOverride = null)
    {
        var card = context.Card;
        var index = indexOverride ?? context.Shape.OperationIndex;
        var prefix = Prefix(card, index);
        if (prefix is null) return 1;
        var mirror = context.Mirror;
        var simulator = mirror.Simulator;
        var combat = (SimulatedCombatState)mirror.CombatState;
        var owner = card.Owner;
        var player = simulator.State.GetPlayerCombatState(owner);
        var prefixIndex = card.Generated.Operations.ToList().IndexOf(prefix);
        return prefix.Template switch
        {
            "D:ForEachOrb" => player.OrbQueue.Orbs.Count,
            "D:ForEachEnemy" => combat.HittableEnemies.Count,
            "D:ForEachUniqueOrb" => player.OrbQueue.Orbs.Select(orb => orb.Id).Distinct().Count(),
            "NCR:ForEachEtherealPlayedCombat" => ChaosHistory.Finished(simulator, owner, thisTurn: false)
                .Count(play => ChaosHistory.CurrentCard(simulator, play).Keywords.Contains(CardKeyword.Ethereal)),
            "NCR:ForEachCardDrawnThisTurn" => combat.GetNonHandDrawsThisTurn(owner),
            "NCR:ForEachDoomThreshold" => ChaosOperationExecutor.DoomThresholdMultiplier(
                context.Target is { } target ? combat.GetAmount<DoomPower>(target) : 0,
                ChaosOperationExecutor.RuntimeSpecValue(card, prefixIndex, "threshold", 10)),
            "NCR:ForEachOstyAttackThisTurn" => ChaosHistory.Finished(simulator, owner)
                .Count(play => play.Card.Tags.Contains(CardTag.OstyAttack)),
            "NCR:ForEachExhaustedSoul" => player.ExhaustPile.Cards.Count(candidate => ChaosDerivativeResolver.Matches(candidate.Preview, prefix)),
            "NCR:ForEachOstyAttackCard" => player.AllCards.Count(candidate => !candidate.References(card) && candidate.Preview.Tags.Contains(CardTag.OstyAttack)),
            "R:ForEachPriorAttackHitOnTarget" => context.Resolution?.PriorAttackHitsOnTargetAtPlayStart
                ?? (context.Target is { } hitTarget ? combat.GetPoweredAttackHitsThisTurn(owner.Creature, hitTarget) : 0),
            "R:ForEachStarCostCard" => player.AllCards.Count(candidate => candidate.Preview.CanonicalStarCost >= 0 || candidate.Preview.HasStarCostX),
            "R:ForEachSkillPlayedThisTurn" => ChaosHistory.Finished(simulator, owner).Count(play => play.Card.Type == CardType.Skill),
            "R:ForEachStarGainedThisTurn" => combat.GetStarsGainedThisTurn(owner),
            "R:ForEachGeneratedCardCombat" => combat.GetCardsGeneratedBeforePrediction(owner)
                + simulator.History.Entries.OfType<CombatPredictionCardGeneratedEntry>().Count(entry => entry.Creator == owner),
            "CL:ForEachCardPlayedCombat" => ChaosHistory.Finished(simulator, owner, thisTurn: false).Count(),
            "CL:ForEachDrawPileCard" => player.DrawPile.Cards.Count,
            _ => 1
        };
    }

    internal static int ModifierMultiplier(OperationExecutionContext context, int index)
    {
        var prefix = Prefix(context.Card, index);
        var operation = context.Card.Generated.Operations[index];
        if (prefix is null || !CardEffectRules.IsMultiplicativeDependencyPrefix(prefix)
            || !(CardEffectRules.IsRepeatableDependencyModifier(operation) || CardEffectRules.IsDependencyCountRepeatModifier(operation))) return 1;
        return Math.Max(0, Multiplier(context, index));
    }
}
