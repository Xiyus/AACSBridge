using AutoAnthony;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using AutoAnthonyCombatSolverBridge.CombatSolver;

namespace AutoAnthonyCombatSolverBridge.Patches;

[HarmonyPatch(typeof(CardChoiceSupport), nameof(CardChoiceSupport.Apply))]
internal static class ChaosChoiceEffectsPatch
{
    private static bool Prefix(CombatPredictionSimulator simulator, SimulatedCombatState combat,
        PredictedCard playedCard, PlanCardChoice choice, ref bool __result)
    {
        ChaosCompositePowerMirror.ResetBudget(simulator, choiceResume: true);
        if (playedCard.Preview is ChaosCardModel && choice.ContextId.StartsWith("aa.slot.", StringComparison.Ordinal))
        {
            simulator.AcknowledgeExecutionDispatch();
            var binding = simulator.StateStore.Get(playedCard.Original, static () => new ChaosCardSlotMirror.BindingState());
            if (binding.Resolution is null || binding.Slot is null) throw new InvalidOperationException("选择丢失了卡槽结算上下文");
            var sourcePile = simulator.State.GetPlayerCombatState(playedCard.Preview.Owner).GetCardPile(choice.SourcePile)!;
            binding.Resolution.CardSelections[binding.Slot] = choice.Cards.Select(token => CardChoiceSupport.Find(sourcePile.Cards, token)).ToList();
            __result = true;
            return false;
        }
        if (playedCard.Preview is not ChaosCardModel card || !choice.ContextId.StartsWith("aa.operation.", StringComparison.Ordinal)
            || !int.TryParse(choice.ContextId.AsSpan("aa.operation.".Length), out var index)
            || index < 0 || index >= card.Generated.Operations.Count) return true;
        var source = simulator.State.GetPlayerCombatState(card.Owner).GetCardPile(choice.SourcePile);
        if (source is not null && simulator.StateStore.TryGetReadOnly<ChaosCardSlotMirror.BindingState>(playedCard.Original, out var currentBinding)
            && currentBinding?.Resolution is { } resolution)
        {
            var records = choice.Cards.Select(token => CardChoiceSupport.Find(source.Cards, token)).ToList();
            if (choice.Effect == PlanChoiceEffect.Exhaust) resolution.ExhaustedByCard.AddRange(records);
            if (choice.Effect == PlanChoiceEffect.Discard) resolution.DiscardedByCard.AddRange(records);
            if (choice.Effect is PlanChoiceEffect.MoveToHand or PlanChoiceEffect.MoveToDrawTop) resolution.LastMovedCard = records.FirstOrDefault();
        }
        if (choice.Effect == PlanChoiceEffect.Modify && card.Generated.Operations[index].Template == "I:ProxyAtomic_Transfigure")
        {
            simulator.AcknowledgeExecutionDispatch();
            var hand = simulator.State.GetPlayerCombatState(card.Owner).Hand.Cards;
            if (choice.Cards.FirstOrDefault() is { } token)
            {
                var selectedCard = CardChoiceSupport.Find(hand, token).MutablePreview;
                if (!selectedCard.EnergyCost.CostsX && selectedCard.EnergyCost.GetWithModifiers(CostModifiers.None) >= 0)
                    selectedCard.EnergyCost.AddThisCombat(1);
                selectedCard.BaseReplayCount++;
            }
            __result = true;
            return false;
        }
        if (choice.Effect == PlanChoiceEffect.AutoPlayRepeated)
        {
            simulator.AcknowledgeExecutionDispatch();
            var hand = simulator.State.GetPlayerCombatState(card.Owner).Hand.Cards;
            if (choice.Cards.FirstOrDefault() is { } token)
            {
                var selectedCard = CardChoiceSupport.Find(hand, token);
                var count = ChaosOperationExecutor.ExecutableOperationCount(card.Generated.Operations[index], card.OperationAmount(index));
                for (var repeat = 0; repeat < count; repeat++)
                {
                    simulator.AutoPlay(selectedCard, nestedChoiceSourceId: card.Id.Entry);
                    if (simulator.HasPendingChoice) break;
                }
            }
            __result = !simulator.HasPendingChoice;
            if (simulator.HasPendingChoice) simulator.RejectExecutionContinuation();
            return false;
        }
        if (choice.Effect is not (PlanChoiceEffect.Transform or PlanChoiceEffect.Duplicate or PlanChoiceEffect.Nightmare)) return true;
        simulator.AcknowledgeExecutionDispatch();
        var pile = simulator.State.GetPlayerCombatState(card.Owner).GetCardPile(choice.SourcePile)!;
        var selected = choice.Cards.Select(token => CardChoiceSupport.Find(pile.Cards, token)).ToList();
        switch (choice.Effect)
        {
            case PlanChoiceEffect.Transform:
                __result = ChaosDerivativeMirror.Transform(simulator, card, index, selected);
                break;
            case PlanChoiceEffect.Duplicate:
                if (selected.Count > 0)
                    simulator.AddGeneratedCardToCombat(selected[0].CreateClone(), PileType.Hand, card.Owner,
                        CardPilePosition.Bottom, CardGenerationResultKind.Fixed);
                __result = !simulator.HasPendingChoice;
                break;
            case PlanChoiceEffect.Nightmare:
                if (selected.Count > 0)
                {
                    var power = combat.AddPowerInstance<NightmarePower>(card.Owner.Creature,
                        card.OperationAmount(index), card.Owner.Creature);
                    combat.SetNightmareSelection(power, selected[0]);
                }
                __result = !simulator.HasPendingChoice;
                break;
        }
        if (simulator.HasPendingChoice) simulator.RejectExecutionContinuation();
        return false;
    }
}
