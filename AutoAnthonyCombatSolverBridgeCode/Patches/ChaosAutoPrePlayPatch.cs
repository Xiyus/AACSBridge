using AutoAnthony;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Cards;
using AutoAnthonyCombatSolverBridge.CombatSolver;

namespace AutoAnthonyCombatSolverBridge.Patches;

[HarmonyPatch(typeof(SimulatedCombatState), nameof(SimulatedCombatState.TriggerAutoPrePlayEarly),
    [typeof(CombatPredictionSimulator), typeof(Player), typeof(int), typeof(TurnStartChoiceCursor), typeof(ISet<uint>)])]
internal static class ChaosAutoPrePlayPatch
{
    private static bool Prefix(SimulatedCombatState __instance, CombatPredictionSimulator simulator, Player player,
        int turnNumber, TurnStartChoiceCursor choices, ISet<uint> processedEnemyDeaths, ref bool __result)
    {
        var exhaust = simulator.State.GetPlayerCombatState(player).ExhaustPile.Cards.ToArray();
        if (!exhaust.Any(card => card.Preview is ChaosCardModel chaos && chaos.Generated.Operations.Any(op => op.Template == "R:AtTurnStartIfInExhaust"))) return true;
        simulator.State.GetPlayerCombatState(player).Phase = PlayerTurnPhase.AutoPrePlay;
        simulator.AcknowledgeExecutionDispatch();
        for (var index = 0; index < exhaust.Length; index++)
        {
            var card = exhaust[index];
            if (card.Preview is ChaosCardModel) ChaosCardExhaustMirror.Execute(simulator, card, "turn_start_if_self_in_exhaust");
            else if (card.Preview is Bombardment)
                __instance.AutoPlayWithChoice(simulator, card, card.Preview.Id.Entry,
                    $"{card.Preview.Id.Entry}+{card.Preview.CurrentUpgradeLevel}#{index}", choices, processedEnemyDeaths);
            if (simulator.HasPendingChoice)
            {
                simulator.RejectExecutionContinuation();
                __result = true;
                return false;
            }
        }
        __result = __instance.ContinueAutoPrePlay(simulator, player, turnNumber, processedEnemyDeaths,
            Array.Empty<PredictedCard>(), SimulatedCombatState.AutoPrePlayStage.Scheduled);
        return false;
    }
}
