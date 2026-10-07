using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using AutoAnthonyCombatSolverBridge.Translation;

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

internal static class ChaosClauseMirror
{
    internal static bool ResolveTarget(CombatPredictionSimulator simulator, ChaosCardModel card, int triggerIndex,
        Creature? sourceTarget, ref Creature? target)
    {
        var unresolved = card.Generated.Operations.Skip(triggerIndex + 1).Any(op =>
            op.Parameters.GetValueOrDefault("triggerIndex", -1) == triggerIndex
            && CardEffectRules.RequiresSingleEnemyTarget(op) && !CardEffectRules.UsesExplicitRandomEnemyTarget(op));
        if (target is not null || !unresolved) return true;
        if (sourceTarget is not null && simulator.State.GetCreature(sourceTarget).IsAlive) { target = sourceTarget; return true; }
        var enemies = simulator.State.CombatState.HittableEnemies.ToArray();
        if (enemies.Length == 0) return false;
        target = simulator.Rng.CombatTargets.NextItem(enemies);
        return true;
    }
    internal static bool Execute(CombatPredictionSimulator simulator, PredictedCard predicted, ChaosCardModel card,
        CardPlay play, int triggerIndex, OperationResolutionState resolution, PredictedCard? eventCard,
        decimal eventAmount, Creature? target, bool powered, bool corruptionExhaust = true, bool triggered = true)
    {
        simulator.AcknowledgeExecutionDispatch();
        var context = new CardOnPlayMirrorContext { Simulator = simulator, Card = predicted, CardPlay = play };
        var operations = card.Generated.Operations;
        if (target is not null && operations.Skip(triggerIndex + 1).Any(op => op.Template == "NCR:CopyTargetDebuffsToOthers"
                && op.Parameters.GetValueOrDefault("triggerIndex", -1) == triggerIndex))
            resolution.TargetDebuffSnapshot = TargetDebuffResolver.Capture(simulator, target);
        for (var index = triggerIndex + 1; index < operations.Count; index++)
        {
            var op = operations[index];
            if (op.Parameters.GetValueOrDefault("triggerIndex", -1) != triggerIndex || op.Scope == OperationScope.Modifier
                || op.Template is "D:ReplayEventCard" or "I:ReplayAttack"
                || corruptionExhaust && op.Template == "N:Exhaust" && op.RuntimeSpec?.Variant == "referenced") continue;
            if (!ChaosCardSlotMirror.Resolve(context, card, index, resolution, eventCard)) return Suspend();
            var spec = ChaosOperationExecutor.EffectiveRuntimeSpec(card, index);
            var execution = new OperationExecutionContext(context, card, new OperationShape(index, op.Scope, spec),
                target, IsTriggered: triggered, Resolution: resolution, EventAmount: eventAmount, EventCard: eventCard,
                UsePoweredCardDamage: powered, HasTargetOverride: true);
            if (!DependencyResolver.Matches(execution) || DependencyResolver.Multiplier(execution) <= 0) continue;
            if (SelectionEffectResolver.TryExecute(execution))
            {
                if (simulator.HasPendingChoice) return Suspend();
                continue;
            }
            if (ChaosCardChoiceMirror.IsSupportedSelection(spec) && op.CardTargetSlot is null
                && !(spec.Opcode == "exhaust_card" && spec.Variant == "referenced" && execution.ReferencedCard is not null))
            {
                var choice = ChaosCardChoiceMirror.BuildOperationSpec(simulator, predicted, card, index, execution.ExecutableAmount);
                if (choice is not null)
                {
                    var binding = simulator.StateStore.Get(predicted.Original, static () => new ChaosCardSlotMirror.BindingState());
                    binding.Slot = null; binding.Resolution = resolution;
                    var combat = (SimulatedCombatState)context.CombatState;
                    combat.ResolveActionCardChoice(simulator, predicted, card.Id.Entry, choice,
                        combat._activeCardExecutionDeaths ?? new HashSet<uint>(), $"aa.operation.{index}");
                    if (simulator.HasPendingChoice) return Suspend();
                }
                continue;
            }
            var handler = OperationHandlerRegistry.Instance.TryGet(OperationKey.FromSpec(spec))
                ?? throw ChaosCompositePowerMirror.Unsupported($"触发收益无 handler：{spec.Opcode}/{spec.Variant}");
            if (CardEffectRules.UsesExplicitRandomEnemyTarget(op))
                execution = execution with { ResolvedTarget = simulator.Rng.CombatTargets.NextItem(context.CombatState.HittableEnemies) };
            handler.Execute(execution);
            if (simulator.HasPendingChoice) return Suspend();
        }
        if (simulator.StateStore.TryGetReadOnly<ChaosCardSlotMirror.BindingState>(predicted.Original, out var completed))
        { completed!.Resolution = null; completed.Slot = null; }
        if (resolution.EndTurnRequested && context.OwnerState.Phase == MegaCrit.Sts2.Core.Combat.PlayerTurnPhase.Play)
            ((SimulatedCombatState)context.CombatState).RequestPlayerTurnEnd();
        return true;

        bool Suspend() { simulator.RejectExecutionContinuation(); return false; }
    }
}
