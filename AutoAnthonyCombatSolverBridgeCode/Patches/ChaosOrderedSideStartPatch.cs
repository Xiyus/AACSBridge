using AutoAnthony;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Simulation;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;
using AutoAnthonyCombatSolverBridge.CombatSolver;

namespace AutoAnthonyCombatSolverBridge.Patches;

[HarmonyPatch(typeof(PersistentPowerSupport), "TriggerOwnerAfterSideTurnStart")]
internal static class ChaosOrderedSideStartPatch
{
    private static bool Prefix(CombatPredictionSimulator simulator, SimulatedCombatState combat, Creature owner, ref bool __result)
    {
        if (!combat.EffectivePowers().OfType<ChaosCompositePower>().Any(power => power.Owner == owner)) return true;
        foreach (var power in combat.EffectivePowers().Where(power => power.Owner == owner).ToArray())
        {
            if (power.Amount <= 0) continue;
            var amount = power.Amount;
            switch (power)
            {
                case ChaosCompositePower chaos:
                    ChaosCompositePowerMirror.Start(simulator, combat, owner.Side, [owner], chaos);
                    break;
                case BiasedCognitionPower: combat.Apply<FocusPower>(owner, -amount, owner); break;
                case CoolantPower when owner.Player is { } player:
                    simulator.GainBlock(owner, simulator.State.GetPlayerCombatState(player).OrbQueue.Orbs.Select(orb => orb.Id).Distinct().Count() * amount, ValueProp.Unpowered);
                    break;
                case DemonFormPower: combat.Apply<StrengthPower>(owner, amount, owner); break;
                case FeralPower feral: simulator.StateStore.Get(feral, () => new FeralPredictionState(feral)).ZeroCostAttacksPlayed = 0; break;
                case FurnacePower when owner.Player is { } player: PersistentPowerSupport.Forge(simulator, player, amount); break;
                case NeurosurgePower: combat.Apply<DoomPower>(owner, amount, owner); break;
                case NoxiousFumesPower:
                    foreach (var target in combat.GetOpponentsOf(owner).Where(simulator.State.IsHittable))
                    {
                        combat.Apply<PoisonPower>(target, amount, owner);
                        PowerLifecycleSupport.ResolvePowerAmountChanges(simulator, combat);
                        if (simulator.HasPendingChoice) break;
                    }
                    break;
                case PrepTimePower: combat.Apply<VigorPower>(owner, amount, owner); break;
                case ReflectPower: combat.SetAmount<ReflectPower>(owner, amount - 1); break;
                case ShadowStepPower:
                    combat.Apply<DoubleDamagePower>(owner, amount, owner); combat.SetAmount<ShadowStepPower>(owner, 0); break;
                case WraithFormPower: combat.Apply<DexterityPower>(owner, -amount, owner); break;
                case ClarityPower: combat.SetAmount<ClarityPower>(owner, amount - 1); break;
            }
            if (simulator.HasPendingChoice)
            {
                simulator.RejectExecutionContinuation(); __result = false; return false;
            }
        }
        __result = !simulator.HasPendingChoice;
        return false;
    }
}
