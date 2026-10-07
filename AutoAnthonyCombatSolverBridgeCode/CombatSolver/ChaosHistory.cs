using AutoAnthony;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

// Prediction history has no turn timestamp. Keep a fork-owned boundary into it, while native
// root entries retain HappenedThisTurn. This avoids counting prior simulated turns or starts.
internal static class ChaosHistory
{
    private sealed class Boundary : IPredictionStateForkable
    {
        public int Index;
        public object Fork(PredictionForkContext context) => MemberwiseClone();
    }

    internal static void StartSide(CombatPredictionSimulator simulator)
        => simulator.StateStore.Get(ModelDb.Power<ChaosCompositePower>(), static () => new Boundary()).Index
            = simulator.History.EntryCount;

    internal static IEnumerable<CardPlay> Finished(CombatPredictionSimulator simulator, Player owner, bool thisTurn = true)
    {
        var combat = (SimulatedCombatState)simulator.State.CombatState;
        var root = combat._rootHistory.CardPlaysFinished
            .Where(entry => entry.CardPlay.Player == owner && (!thisTurn || entry.HappenedThisTurn(combat)))
            .Select(entry => entry.CardPlay);
        var start = thisTurn ? simulator.StateStore.GetReadOnly(ModelDb.Power<ChaosCompositePower>(), static () => new Boundary()).Index : 0;
        return root.Concat(simulator.History.Entries.Skip(start).OfType<CombatPredictionCardPlayFinishedEntry>()
            .Where(entry => entry.CardPlay.Player == owner).Select(entry => entry.CardPlay));
    }

    internal static CardModel CurrentCard(CombatPredictionSimulator simulator, CardPlay play)
        => simulator.State.FindCard(play.Card)?.Preview ?? play.Card;
}
