using CombatSolver;
using CombatSolver.Engine.Common;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace AutoAnthonyCombatSolverBridge.Translation;

// Native temporary attribute powers modify the attribute before installing their
// restoration marker. Applying only the marker invents a bonus when it expires.
internal static class PowerApplicationResolver
{
    internal static void Apply(ICombatPredictionEffectSink effects, Type type, Creature target, int amount,
        Creature? applier, CardModel? source)
    {
        if (effects is not SimulatedCombatState combat)
            throw new InvalidOperationException("Power 施加需要分支战斗状态");
        if (type == typeof(ManglePower) || type == typeof(PiercingWailPower))
        {
            combat.ApplyTemporaryStrengthLoss(type, target, amount, applier, source);
            return;
        }
        if (type == typeof(SetupStrikePower) || type == typeof(AnticipatePower))
        {
            combat.BeginCardPowerApplication(source);
            try
            {
                if (type == typeof(SetupStrikePower)) combat.ApplyTemporaryStrengthGain<SetupStrikePower>(target, amount, applier);
                else combat.ApplyTemporaryDexterity<AnticipatePower>(target, amount, applier);
            }
            finally { combat.CompleteCardPowerApplication(source); }
            return;
        }
        effects.ApplyPowerFromSource(type, target, amount, applier, source);
    }
}
