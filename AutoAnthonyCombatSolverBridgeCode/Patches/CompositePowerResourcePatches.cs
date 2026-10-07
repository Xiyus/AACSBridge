using AutoAnthony;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using AutoAnthonyCombatSolverBridge.CombatSolver;

namespace AutoAnthonyCombatSolverBridge.Patches;

// The pinned solver implements these hooks as ordered native switches instead of registries.
// Add Chaos entries to the same order. Only activate this mirror when a Chaos power is present.
[HarmonyPatch(typeof(PowerLifecycleSupport), nameof(PowerLifecycleSupport.ResolvePowerAmountChanges))]
internal static class CompositePowerAmountChangedPatch
{
    private static bool Prefix(CombatPredictionSimulator simulator, SimulatedCombatState combat)
    {
        if (!combat.EffectivePowers().OfType<ChaosCompositePower>().Any()) return true;
        while (true)
        {
            var changes = combat.DrainPowerAmountChanges();
            if (changes.Length == 0) return false;
            foreach (var change in changes)
            foreach (var listener in combat.EffectivePowers().ToArray())
            {
                if (listener.Amount <= 0) continue;
                switch (listener)
                {
                    case ChaosCompositePower chaos:
                        if (change.Delta > 0 && change.Applier == chaos.Owner && change.Power is VulnerablePower)
                            ChaosCompositePowerMirror.Fire(chaos, simulator, "vulnerable_applied", eventCreature: change.Power.Owner);
                        if (change.Delta != 0 && change.Applier == chaos.Owner && change.Power is DoomPower)
                            ChaosCompositePowerMirror.Fire(chaos, simulator, "doom_applied", eventCreature: change.Power.Owner);
                        if (change.Delta != 0 && change.Applier == chaos.Owner && change.Power.Owner.IsEnemy
                            && change.Power.GetTypeForAmount(change.Delta) == PowerType.Debuff && change.Power is not ITemporaryPower)
                            ChaosCompositePowerMirror.Fire(chaos, simulator, "enemy_debuff_applied", eventCreature: change.Power.Owner);
                        break;
                    case ShroudPower when ReferenceEquals(change.Applier, listener.Owner) && change.Power is DoomPower:
                        simulator.GainBlock(listener.Owner, listener.Amount, ValueProp.Unpowered);
                        break;
                    case SleightOfFleshPower when change.Delta != 0 && change.Power.GetTypeForAmount(change.Delta) == PowerType.Debuff
                        && change.Power.Owner.IsEnemy && ReferenceEquals(change.Applier, listener.Owner) && change.Power is not ITemporaryPower:
                        using (simulator.PushDamageSource(CombatDamageSource.For(CombatDamageSourceKind.Power, nameof(SleightOfFleshPower))))
                            simulator.Damage(change.Power.Owner, listener.Amount, ValueProp.Unpowered, listener.Owner);
                        break;
                    case ViciousPower when change.Delta > 0 && change.Power is VulnerablePower
                        && ReferenceEquals(change.Applier, listener.Owner) && listener.Owner.Player is { } player:
                        simulator.Draw(player, listener.Amount);
                        break;
                }
                if (simulator.HasPendingChoice)
                {
                    simulator.RejectExecutionContinuation();
                    return false;
                }
            }
        }
    }
}

[HarmonyPatch(typeof(PowerLifecycleSupport), nameof(PowerLifecycleSupport.AfterEnergySpent))]
internal static class CompositePowerEnergySpentPatch
{
    private static bool Prefix(CombatPredictionSimulator simulator, SimulatedCombatState combat, PredictedCard card, int amount)
    {
        if (!combat.EffectivePowers().OfType<ChaosCompositePower>().Any()) return true;
        if (amount <= 0) return false;
        foreach (var power in combat.EffectivePowers().ToArray())
        {
            if (power.Amount <= 0 || power.Owner != card.Preview.Owner.Creature) continue;
            if (power is ChaosCompositePower chaos)
                Spend(chaos, simulator, amount, stars: false);
            else if (power is OrbitPower orbit)
            {
                var count = combat.AdvanceOrbitEnergy(orbit, amount);
                if (count > 0) simulator.GainEnergy(card.Preview.Owner, power.Amount * count);
            }
        }
        return false;
    }

    internal static void Spend(ChaosCompositePower power, CombatPredictionSimulator simulator, int amount, bool stars)
    {
        var state = ChaosCompositePowerMirror.Read(simulator, power);
        var snapshot = state.Snapshot;
        var template = stars ? "A:whenOneStarSpent" : "A:whenEnergySpent";
        var index = snapshot.Definition.Card.Operations.ToList().FindIndex(op => op.Template == template);
        if (index < 0) return;
        var threshold = Math.Max(1, snapshot.EffectiveOperationAmount(index, stars ? 1 : 4));
        var accumulated = (stars ? snapshot.StarsSpentTowardTrigger : snapshot.EnergySpentTowardRefund) + amount;
        var count = accumulated / threshold;
        if (stars) snapshot.StarsSpentTowardTrigger = accumulated % threshold;
        else snapshot.EnergySpentTowardRefund = accumulated % threshold;
        state.Refresh();
        for (var i = 0; i < count; i++)
            ChaosCompositePowerMirror.Fire(power, simulator, stars ? "stars_spent_threshold" : "energy_spent_threshold", onlyTrigger: index);
    }
}

[HarmonyPatch(typeof(PowerLifecycleSupport), nameof(PowerLifecycleSupport.AfterStarsSpent))]
internal static class CompositePowerStarsSpentPatch
{
    private static bool Prefix(CombatPredictionSimulator simulator, SimulatedCombatState combat, PredictedCard card, int amount)
    {
        if (!combat.EffectivePowers().OfType<ChaosCompositePower>().Any()) return true;
        if (amount <= 0 || !combat.TriggerRelicsAfterStarsSpent(simulator, card.Preview.Owner, amount)) return false;
        foreach (var power in combat.EffectivePowers().ToArray())
        {
            if (power.Amount <= 0 || power.Owner != card.Preview.Owner.Creature) continue;
            if (power is ChaosCompositePower chaos) CompositePowerEnergySpentPatch.Spend(chaos, simulator, amount, stars: true);
            else if (power is ChildOfTheStarsPower) simulator.GainBlock(power.Owner, power.Amount * amount, ValueProp.Unpowered);
            if (simulator.HasPendingChoice)
            {
                simulator.RejectExecutionContinuation();
                return false;
            }
        }
        return false;
    }
}
