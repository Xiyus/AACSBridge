using AutoAnthony;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

internal static class ChaosDerivativeMirror
{
    internal static PredictedCard Create(CombatPredictionSimulator simulator, ChaosCardModel source, int index)
    {
        // SimulatedCombatState.CreateCard creates detached prediction-owned models. The resolver's
        // enchant/upgrade steps mutate only that new model and preserve the full derivative slot.
        var generated = ChaosDerivativeResolver.Create(simulator.State.CombatState, source.Owner,
            source.Generated.Operations[index], ChaosOperationExecutor.DerivativeIsUpgraded(source, index));
        return PredictedCard.FromGenerated(generated);
    }

    internal static void Add(CombatPredictionSimulator simulator, ChaosCardModel source, int index, PileType pile, int count,
        CardPilePosition position = CardPilePosition.Bottom)
    {
        if (count <= 0) return;
        var cards = Enumerable.Range(0, count).Select(_ => Create(simulator, source, index)).ToList();
        simulator.AddGeneratedCardsToCombat(cards, pile, source.Owner, position, CardGenerationResultKind.Fixed);
    }

    internal static bool Transform(CombatPredictionSimulator simulator, ChaosCardModel source, int operationIndex,
        IReadOnlyList<PredictedCard> selected)
    {
        var rows = new List<(PredictedCard Old, PredictedCard New, SimCardPile Pile, int Index)>();
        foreach (var old in selected)
        {
            var replacement = Create(simulator, source, operationIndex);
            var (pile, index) = CardChoiceSupport.RemoveTransformedCard(simulator, old);
            rows.Add((old, replacement, pile, index));
        }
        rows.Sort((left, right) => left.Pile.Type != right.Pile.Type
            ? left.Pile.Type.CompareTo(right.Pile.Type) : left.Index.CompareTo(right.Index));
        foreach (var row in rows)
            if (!CardChoiceSupport.AddTransformedCard(simulator, row.Old, row.New, row.Pile, row.Index, CardGenerationResultKind.Fixed))
            {
                simulator.RejectExecutionContinuation();
                return false;
            }
        return true;
    }
}
