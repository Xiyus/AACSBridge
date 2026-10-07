using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace AutoAnthonyCombatSolverBridge.Translation;

internal static class CurrentDamageResolver
{
    internal static decimal Get(OperationExecutionContext context, PredictedCard card)
    {
        var preview = card.Preview;
        decimal damage = 0;
        if (preview is ChaosCardModel chaos)
        {
            var index = chaos.Generated.Operations.ToList().FindIndex(CardEffectRules.IsEnemyDamage);
            if (index >= 0)
            {
                var play = new CardPlay { Card = preview, Player = preview.Owner, Target = null, ResultPile = PileType.Discard,
                    Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 }, PlayIndex = 0, PlayCount = 1, IsAutoPlay = true };
                var mirror = new CardOnPlayMirrorContext { Simulator = context.Mirror.Simulator, Card = card, CardPlay = play };
                var execution = new OperationExecutionContext(mirror, chaos,
                    new OperationShape(index, chaos.Generated.Operations[index].Scope, ChaosOperationExecutor.EffectiveRuntimeSpec(chaos, index)),
                    Resolution: new OperationResolutionState());
                damage = DamageModifierResolver.Resolve(execution, chaos.OperationAmount(index), 1).Damage;
            }
        }
        else if (preview.DynamicVars.TryGetValue("CalculatedDamage", out var calculated) && calculated is CalculatedVar variable)
        {
            if (!CalculatedVarSpecRegistry.TryCalculate(variable, context.Mirror.Simulator, card, null, out damage))
                throw new InvalidOperationException($"无法从分支计算引用牌 {preview.Id} 的伤害");
        }
        else if (preview.DynamicVars.TryGetValue("Damage", out var value)) damage = value.BaseValue;
        else if (preview.DynamicVars.TryGetValue("OstyDamage", out var osty)) damage = osty.BaseValue;
        return HookMirrors.ModifyDamage(context.Mirror.Simulator, null, preview.Owner.Creature, damage,
            ChaosOperationExecutor.DamagePropsForCardEffect(preview.Type), card, null);
    }
}
