using AutoAnthony;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using AutoAnthonyCombatSolverBridge.CombatSolver;

namespace AutoAnthonyCombatSolverBridge.Patches;

[HarmonyPatch(typeof(SimulatedCombatState), "ReturnsToHandAfterPlaying")]
internal static class ChaosReturningCardEligibilityPatch
{
    private static void Postfix(CardModel card, ref bool __result)
    {
        if (card is ChaosCardModel chaos && ChaosCardPassiveMirror.ReturnsNextTurn(chaos)) __result = true;
    }
}

[HarmonyPatch(typeof(SimulatedCombatState), "ContinueBeforeHandDraw")]
internal static class ChaosReturningCardExecutionPatch
{
    private static bool Prefix(SimulatedCombatState __instance, CombatPredictionSimulator simulator, Player player,
        TurnStartChoiceCursor choices, IReadOnlyList<PredictedCard> returningCards, SimulatedCombatState.BeforeHandDrawStage stage,
        int nextIndex, ref bool __result)
    {
        if (!returningCards.Any(card => card.Preview is ChaosCardModel)) return true;
        if (stage == SimulatedCombatState.BeforeHandDrawStage.Relics && __instance.PrepareRelicsBeforeHandDraw(simulator, player, choices))
        {
            simulator.AppendExecutionContinuation(new SimulatedCombatState.BeforeHandDrawFrame(player, returningCards,
                SimulatedCombatState.BeforeHandDrawStage.ReturningCards));
            __result = true;
            return false;
        }
        if (__instance._returnToHandNextTurn is { } eligible)
        {
            for (var index = nextIndex; index < returningCards.Count; index++)
            {
                var card = returningCards[index];
                if (stage == SimulatedCombatState.BeforeHandDrawStage.RemoveReturnedCard)
                {
                    eligible.Remove(card);
                    stage = SimulatedCombatState.BeforeHandDrawStage.ReturningCards;
                    continue;
                }
                if (card.Preview.HasBeenRemovedFromState) { eligible.Remove(card); continue; }
                if (card.GetPile(simulator.State)?.Type != PileType.Hand)
                {
                    if (card.Preview is ChaosCardModel) ChaosCardPassiveMirror.ReturnToHand(simulator, card);
                    else simulator.AddToPile(card, PileType.Hand);
                    if (simulator.HasPendingChoice)
                    {
                        simulator.AppendExecutionContinuation(new SimulatedCombatState.BeforeHandDrawFrame(player, returningCards,
                            SimulatedCombatState.BeforeHandDrawStage.RemoveReturnedCard, index));
                        __result = true;
                        return false;
                    }
                }
                eligible.Remove(card);
            }
        }
        __result = false;
        return false;
    }
}
