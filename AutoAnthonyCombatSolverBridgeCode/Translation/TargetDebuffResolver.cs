using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace AutoAnthonyCombatSolverBridge.Translation;

internal static class TargetDebuffResolver
{
    internal static Dictionary<PowerModel, int> Capture(CombatPredictionSimulator simulator, Creature target)
    {
        var combat = (SimulatedCombatState)simulator.State.CombatState;
        var debuffs = combat.EffectivePowers().Where(power => power.Owner == target && power.TypeForCurrentAmount == PowerType.Debuff)
            .ToDictionary(power => PredictionUtils.CloneModelForSimulation(power), power => power.Amount);
        foreach (var pair in debuffs.ToArray())
        {
            if (pair.Key is not ITemporaryPower temporary) continue;
            var internalPower = debuffs.Keys.FirstOrDefault(power => power.Id == temporary.InternallyAppliedPower.Id);
            if (internalPower is not null) debuffs[internalPower] += pair.Value;
        }
        return debuffs;
    }

    internal static void Copy(OperationExecutionContext context)
    {
        if (context.Target is not { } target) return;
        var simulator = context.Mirror.Simulator;
        var combat = (SimulatedCombatState)context.Mirror.CombatState;
        var debuffs = context.Resolution?.TargetDebuffSnapshot ?? Capture(simulator, target);
        foreach (var enemy in combat.HittableEnemies.ToArray())
        {
            if (enemy == target) continue;
            foreach (var pair in debuffs)
            {
                if (pair.Value == 0) continue;
                var clone = PredictionUtils.CloneModelForSimulation(pair.Key);
                combat.ApplyClonedPower(clone, enemy, pair.Value, pair.Key.Applier);
                PowerLifecycleSupport.ResolvePowerAmountChanges(simulator, combat);
                if (simulator.HasPendingChoice) return;
            }
        }
    }
}
