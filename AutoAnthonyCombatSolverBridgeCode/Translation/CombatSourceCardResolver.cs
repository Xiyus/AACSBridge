using AutoAnthony;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace AutoAnthonyCombatSolverBridge.Translation;

// AA 0.3.139 applies three host-cost effects to the originating combat card,
// even when a delayed trigger executes through a detached reconstructed source.
internal static class CombatSourceCardResolver
{
    internal static ChaosCardModel Resolve(OperationExecutionContext context)
    {
        var simulator = context.Mirror.Simulator;
        var source = context.Card;
        var predicted = simulator.State.FindCard(source);
        if (predicted?.GetPile(simulator.State)?.Type.IsCombatPile() == true)
            return (ChaosCardModel)predicted.MutablePreview;
        var candidates = simulator.State.GetPlayerCombatState(source.Owner).AllCards
            .Where(card => card.Preview is ChaosCardModel && card.GetPile(simulator.State)?.Type.IsCombatPile() == true).ToList();
        var selected = SelectSource(source, candidates.Select(card => (ChaosCardModel)card.Preview).ToArray());
        var wrapper = candidates.FirstOrDefault(card => ReferenceEquals(card.Preview, selected));
        return wrapper is null ? source : (ChaosCardModel)wrapper.MutablePreview;
    }

    internal static ChaosCardModel SelectSource(ChaosCardModel source, IReadOnlyList<ChaosCardModel> candidates)
    {
        if (source.DeckVersion is { } deck)
        {
            var byDeck = candidates.FirstOrDefault(card => ReferenceEquals(card.DeckVersion, deck));
            if (byDeck is not null) return byDeck;
        }
        return candidates.FirstOrDefault(card => card.Definition.Slot == source.Definition.Slot
            && card.RuntimeProfileId == source.RuntimeProfileId
            && card.EffectiveDefinitionPayload == source.EffectiveDefinitionPayload) ?? source;
    }
}
