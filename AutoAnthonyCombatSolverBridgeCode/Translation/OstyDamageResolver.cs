using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.ValueProps;

namespace AutoAnthonyCombatSolverBridge.Translation;

internal static class OstyDamageResolver
{
    internal static void Execute(OperationExecutionContext context, Creature? target)
    {
        var simulator = context.Mirror.Simulator;
        if (simulator.State.GetOsty(context.Card.Owner) is not { } osty) return;
        var (damage, hits) = DamageModifierResolver.Resolve(context, context.ExecutableAmount, 1);
        if (context.Resolution is { } state) state.LastDamageDealt = 0;
        for (var index = 0; index < Math.Max(0, hits); index++)
        {
            IReadOnlyList<DamageResult> results;
            if (context.Card.Type == CardType.Power)
                results = simulator.Damage(target is null ? context.Mirror.CombatState.HittableEnemies.ToArray() : [target],
                    damage, ValueProp.Unpowered, osty, context.Mirror.Card, context.Mirror.CardPlay);
            else
            {
                var attack = DamageCmd.Attack(damage).FromOsty(osty, context.Card, context.Mirror.CardPlay);
                if (target is null) attack.TargetingAllOpponents(context.Mirror.CombatState);
                else attack.Targeting(target);
                attack.Simulate(simulator);
                results = attack.Results.SelectMany(result => result).ToArray();
            }
            if (context.Resolution is { } resolution)
            {
                resolution.LastAttackKilled |= results.Any(result => result.WasTargetKilled);
                resolution.LastDamageDealt += decimal.ToInt32(results.Sum(result => result.TotalDamage));
            }
            if (simulator.HasPendingChoice) return;
        }
    }
}
