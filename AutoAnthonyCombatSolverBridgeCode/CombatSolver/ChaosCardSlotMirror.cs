using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using AutoAnthonyCombatSolverBridge.Translation;

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

internal static class ChaosCardSlotMirror
{
    internal sealed class BindingState : IPredictionStateForkable
    {
        public string? Slot;
        public OperationResolutionState? Resolution;
        public object Fork(PredictionForkContext context)
        {
            Resolution?.PrepareFork(context);
            return new BindingState { Slot = Slot, Resolution = Resolution?.Fork(context) };
        }
    }

    internal static bool Resolve(CardOnPlayMirrorContext context, ChaosCardModel card, int index, OperationResolutionState resolution,
        PredictedCard? eventCard = null)
    {
        var operation = card.Generated.Operations[index];
        if (operation.CardTargetSlot is not { } slot || resolution.CardSelections.ContainsKey(slot)) return true;
        var simulator = context.Simulator;
        if (ChaosOperationExecutor.SkipsCardSelectionAtZero(operation, card.OperationAmount(index)))
        {
            resolution.CardSelections[slot] = [];
            return true;
        }
        var selector = ChaosOperationExecutor.CardSelectorForSlot(card.Generated.Operations, slot);
        if (selector is null)
        {
            if (eventCard is not null) resolution.CardSelections[slot] = [eventCard];
            return true;
        }
        var options = context.OwnerState.Hand.Cards.Where(candidate => selector.Template == "N_SELECT_HAND_ATTACK"
            ? candidate.Preview.Type == CardType.Attack
            : operation.Template is "I:ProxyAtomic_Begone" or "I:ProxyAtomic_Guards" or "CL:TransformSelectedHandCards"
                ? candidate.Preview.IsTransformable
            : operation.Template == "R:PlaySelectedSkillMultipleTimes"
                ? candidate.Preview.Type == CardType.Skill && !candidate.Preview.Keywords.Contains(CardKeyword.Unplayable)
            : operation.Template == "R:CopySelectedColorlessCard" ? candidate.Preview.VisualCardPool.IsColorless
            : operation.Template == "NCR:AddVoidToSelectedHandCard" ? !candidate.Preview.Keywords.Contains(CardKeyword.Ethereal)
            : operation.Template == "NCR:AddRetainToSelectedHandCard" ? !candidate.Preview.Keywords.Contains(CardKeyword.Retain) : true).ToList();
        var maximum = Math.Min(options.Count, ChaosOperationExecutor.SelectionCountForEffect(operation, card.OperationAmount(index)));
        if (maximum <= 0)
        {
            resolution.CardSelections[slot] = [];
            return true;
        }
        var binding = simulator.StateStore.Get(context.Card.Original, static () => new BindingState());
        binding.Slot = slot;
        binding.Resolution = resolution;
        var minimum = operation.Template == "I:ProxyAtomic_Guards" ? 0 : maximum;
        var spec = new CardChoiceSpec(PlanChoiceEffect.Modify, PileType.Hand, minimum, maximum, options,
            context.OwnerState.Hand.Cards, ReplacementValue: 0d);
        var combat = (SimulatedCombatState)context.CombatState;
        return combat.ResolveActionCardChoice(simulator, context.Card, string.Empty, spec,
            combat._activeCardExecutionDeaths ?? new HashSet<uint>(), $"aa.slot.{index}.{slot}");
    }
}
