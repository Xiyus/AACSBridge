using AutoAnthony;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using AutoAnthonyCombatSolverBridge.CombatSolver;

namespace AutoAnthonyCombatSolverBridge.Patches;

[HarmonyPatch(typeof(SimulatedCombatState), nameof(SimulatedCombatState.TriggerScheduledAutoPlays))]
internal static class ChaosScheduledAutoPlayPatch
{
    private static bool Prefix(SimulatedCombatState __instance, CombatPredictionSimulator simulator, Player player,
        int turnNumber, TurnStartChoiceCursor choices, ISet<uint> processedEnemyDeaths, ref bool __result)
    {
        var powers = __instance.EffectivePowers().ToArray();
        if (!powers.OfType<ChaosCompositePower>().Any(power => ChaosCompositePowerMirror.Read(simulator, power).OwnerPlayer == player
                && power.Definition.Card.Operations.Any(op => op.Template == "CL:PlayTopDrawCard"))) return true;
        foreach (var power in powers)
        {
            if (power.Amount <= 0) continue;
            if (power is ChaosCompositePower chaos && ChaosCompositePowerMirror.Read(simulator, chaos).OwnerPlayer == player
                && ChaosCompositePowerMirror.Read(simulator, chaos).Snapshot.Permanent)
                ChaosCompositePowerMirror.FireAny(chaos, simulator, ["turn_start", "turn_start_if_self_in_exhaust"], autoPrePlay: true);
            else if (power is MayhemPower && power.Owner == player.Creature)
            {
                var cards = simulator.MoveCardsForAutoPlay(player, power.Amount, CardPilePosition.Top);
                for (var index = 0; index < cards.Count && !simulator.HasPendingChoice; index++)
                    __instance.AutoPlayWithChoice(simulator, cards[index], power.Id.Entry,
                        $"{cards[index].Preview.Id.Entry}+{cards[index].Preview.CurrentUpgradeLevel}#{index}", choices, processedEnemyDeaths);
            }
            if (simulator.HasPendingChoice)
            {
                simulator.RejectExecutionContinuation(); __result = true; return false;
            }
        }
        __result = __instance.ContinueScheduledAutoPlays(simulator, player, turnNumber, processedEnemyDeaths, Array.Empty<PredictedCard>(), 0);
        return false;
    }
}
