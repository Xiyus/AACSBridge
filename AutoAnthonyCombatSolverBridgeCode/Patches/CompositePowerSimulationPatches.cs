using System.Reflection;
using System.Text;
using AutoAnthony;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using CombatSolver.Engine.Common;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using AutoAnthonyCombatSolverBridge.CombatSolver;

namespace AutoAnthonyCombatSolverBridge.Patches;

[HarmonyPatch(typeof(global::CombatSolver.Engine.InCombat.Mirrors.HookMirrors), "AfterCardExhausted")]
internal static class ChaosCardSelfExhaustPatch
{
    private static void Postfix(CombatPredictionSimulator simulator, PredictedCard card)
        => ChaosCardExhaustMirror.Execute(simulator, card);
}

[HarmonyPatch(typeof(CombatPredictionSimulator), nameof(CombatPredictionSimulator.ManualPlay))]
internal static class CompositePowerManualBoundaryPatch
{
    private static void Prefix(CombatPredictionSimulator __instance) => ChaosCompositePowerMirror.ResetBudget(__instance);
}

[HarmonyPatch(typeof(CombatPredictionSimulator), "SimulateEndPlayerTurnBeforeOrbPassives")]
internal static class CompositePowerEndBoundaryPatch
{
    private static void Prefix(CombatPredictionSimulator __instance) => ChaosCompositePowerMirror.ResetBudget(__instance);
}

[HarmonyPatch(typeof(global::CombatSolver.Engine.InCombat.Mirrors.HookMirrors), "BeforeSideTurnStart")]
internal static class CompositePowerStartBoundaryPatch
{
    private static void Prefix(CombatPredictionSimulator simulator) => ChaosCompositePowerMirror.ResetBudget(simulator);
}

// Pinned solver-only seams. No real game hook or AutoAnthony execution method is patched.
[HarmonyPatch(typeof(PersistentPowerSupport), nameof(PersistentPowerSupport.TriggerAfterSideTurnStart))]
internal static class CompositePowerSideStartPatch
{
    private static void Postfix(CombatPredictionSimulator simulator, SimulatedCombatState combat,
        CombatSide side, IReadOnlyList<Creature> participants, bool __result)
    {
        if (__result) ChaosCompositePowerMirror.Start(simulator, combat, side, participants);
        else if (combat.EffectivePowers().OfType<ChaosCompositePower>().Any())
            throw ChaosCompositePowerMirror.Unsupported("回合开始阶段产生未适配的选择续接");
    }
}

[HarmonyPatch(typeof(EndTurnPowerSupport), nameof(EndTurnPowerSupport.TriggerRegular))]
internal static class CompositePowerSideEndPatch
{
    private static void Postfix(CombatPredictionSimulator simulator, SimulatedCombatState combat,
        CombatSide side, IEnumerable<Creature> participants, bool __result)
    {
        if (__result) ChaosCompositePowerMirror.End(simulator, combat, side, participants);
        else if (combat.EffectivePowers().OfType<ChaosCompositePower>().Any())
            throw ChaosCompositePowerMirror.Unsupported("回合结束阶段产生未适配的选择续接");
    }
}

// PowerHiddenStateMirrors does not extend continuation stamps. Append an exact (not hashed)
// saved-state representation on both sides, so a hidden counter mismatch invalidates reuse.
[HarmonyPatch]
internal static class CompositePowerContinuationPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ContinuationStamp), "AppendPowers",
        [typeof(StringBuilder), typeof(IEnumerable<PowerModel>), typeof(CombatPredictionSimulator)])
        ?? throw new MissingMethodException("ContinuationStamp.AppendPowers");

    private static void Postfix(StringBuilder text, IEnumerable<PowerModel> powers, CombatPredictionSimulator? simulator)
    {
        var states = powers.OfType<ChaosCompositePower>().Where(p => p.Amount != 0).Select(power =>
        {
            var snapshot = simulator is null ? power : ChaosCompositePowerMirror.Read(simulator, power).Snapshot;
            // Base64 cannot introduce the stamp's semicolon field delimiter.
            static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
            return $"{power.Owner.CombatId}:{string.Join(',', snapshot.CaptureMultiplayerState())}:" +
                   $"{Encode(snapshot.ProfileId)}:{Encode(snapshot.SourceTinkeredDefinitionPayload)}";
        }).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (states.Length > 0) text.Append(";AA6=").AppendJoin('|', states);
    }
}
