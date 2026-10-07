using CombatSolver;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.CardSelection;

namespace AutoAnthonyCombatSolverBridge.Patches;

// AA legitimately calls FromHand with no source. Solver 0.50.1's observation
// prefix dereferences source.Id; retain observation while accepting that native API contract.
[HarmonyPatch(typeof(HandObservationPatch), nameof(HandObservationPatch.Prefix))]
internal static class ChaosHandObservationPatch
{
    private static bool Prefix(Player player, CardSelectorPrefs prefs, Func<CardModel, bool>? filter,
        AbstractModel? source, ref NativeChoiceRequest? __4)
    {
        if (source is not null) return true;
        var options = player.PlayerCombatState!.Hand.Cards.Where(filter ?? (_ => true)).ToArray();
        __4 = NativeChoiceRuntime.Observe(NativeChoiceSurfaceKind.Hand, player, options, prefs.MinSelect, prefs.MaxSelect,
            options.Length > 0 && (prefs.RequireManualConfirmation || options.Length > prefs.MinSelect),
            requireManualConfirmation: prefs.RequireManualConfirmation, sourceId: "");
        return false;
    }
}
