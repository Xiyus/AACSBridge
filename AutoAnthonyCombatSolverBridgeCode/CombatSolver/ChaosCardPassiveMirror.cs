using AutoAnthony;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Attack;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.TurnEnd;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

internal static class ChaosCardPassiveMirror
{
    internal static bool IsOperation(string template) => template is "R:CostDownWhenDrawn" or "R:DamageUpWhenDrawn"
        or "NCR:CostDownPerVoidPlayed" or "NCR:CostDownWhenCreatureDies" or "NCR:WheneverCreatureDies"
        or "D:CostDownWhenStatusGenerated" or "D:WheneverStatusGenerated" or "NCR:SetCostZeroIfOstyAttacked"
        or "NCR:ReturnFromDiscardOnHighCostPlay" or "NCR:WheneverHighCostCardPlayed" or "R:ReturnAfterSkillsPlayed"
        or "C:whileInCombat" or "C:whileInCombatSkillCostReduction" or "R:ReturnThisToHand" or "R:PutThisOnDraw"
        or "R:AtTurnEndWhenTopOfDraw" or "R:PlayAtTurnEndIfTopOfDraw" or "CL:ReturnThisToHand";
    internal static bool ReturnsNextTurn(ChaosCardModel card) => card.Generated.Operations.Any(op => op.Template == "CL:ReturnThisToHand");

    internal static void Entered(CombatPredictionSimulator simulator, PredictedCard predicted)
    {
        var current = (SimulatedCombatState)simulator.State.CombatState;
        if (predicted.Preview.Tags.Contains(CardTag.Shiv))
            foreach (var power in current.EffectivePowers().OfType<ChaosCompositePower>())
            {
                var state = ChaosCompositePowerMirror.Read(simulator, power);
                if (predicted.Preview.Owner == state.OwnerPlayer && state.Snapshot.HasRule("derivative_retain"))
                    predicted.MutablePreview.AddKeyword(CardKeyword.Retain);
            }
        if (predicted.Preview is not ChaosCardModel || predicted.Preview.IsClone) return;
        var card = (ChaosCardModel)predicted.MutablePreview;
        var combat = (SimulatedCombatState)simulator.State.CombatState;
        var voidReduction = Total(card, "NCR:CostDownPerVoidPlayed");
        if (voidReduction > 0)
            card.EnergyCost.AddThisCombat(-voidReduction * ChaosHistory.Finished(simulator, card.Owner, thisTurn: false)
                .Count(play => ChaosHistory.CurrentCard(simulator, play).Keywords.Contains(CardKeyword.Ethereal)));
        if (card.Generated.Operations.Any(op => op.Template == "NCR:SetCostZeroIfOstyAttacked")
            && simulator.State.GetOsty(card.Owner) is { } osty && combat.GetCreatureAttacksThisTurn(osty) > 0)
            card.EnergyCost.SetThisTurn(0);
        var type = card.Generated.Operations.Any(op => op.Template == "C:whileInCombat") ? CardType.Attack
            : card.Generated.Operations.Any(op => op.Template == "C:whileInCombatSkillCostReduction") ? CardType.Skill : (CardType?)null;
        if (type is null) return;
        var template = type == CardType.Attack ? "C:whileInCombat" : "C:whileInCombatSkillCostReduction";
        var index = card.Generated.Operations.ToList().FindIndex(op => op.Template == template);
        card.EnergyCost.AddThisTurn(-Math.Max(1, card.OperationAmount(index))
            * ChaosHistory.Finished(simulator, card.Owner).Count(play => play.Card.Type == type));
    }
    internal static void Register<T>() where T : ChaosCardModel
    {
        AfterAutoPostPlayPhaseEnteredMirrors.Registry.Register<T>((card, c) =>
        {
            if (c.Player != card.Owner || c.State.FindCard(card) is not { } predicted) return;
            if (predicted.GetPile(c.State)?.Type == PileType.Exhaust)
                ChaosCardExhaustMirror.Execute(c.Simulator, predicted, "turn_end_if_self_in_exhaust");
            if (c.Simulator.HasPendingChoice) return;
            if (card.Generated.Operations.Any(op => op.Template == "R:PlayAtTurnEndIfTopOfDraw")
                && ReferenceEquals(c.Simulator.State.GetPlayerCombatState(card.Owner).DrawPile.Cards.FirstOrDefault(), predicted))
            {
                c.Simulator.AutoPlay(predicted, nestedChoiceSourceId: predicted.Preview.Id.Entry);
                if (c.Simulator.HasPendingChoice) c.Simulator.RejectExecutionContinuation();
            }
        });
        AfterCardDrawnMirrors.Registry.Register<T>((card, c) =>
        {
            if (!c.Card.References(card) || c.IntrinsicCardHandled) return;
            c.IntrinsicCardHandled = true;
            var preview = (ChaosCardModel)c.Card.MutablePreview;
            for (var index = 0; index < preview.Generated.Operations.Count; index++)
            {
                var op = preview.Generated.Operations[index];
                if (op.Template == "R:CostDownWhenDrawn")
                    preview.EnergyCost.AddThisCombat(-Math.Max(1, preview.OperationAmount(index)));
                else if (op.Template == "R:DamageUpWhenDrawn")
                    preview.ExtraDamage += Math.Max(0, preview.OperationAmount(index));
            }
        });
        BeforeCardPlayedMirrors.Registry.Register<T>((card, c) =>
        {
            if (c.PreviewCard.Owner != card.Owner) return;
            var predicted = c.State.FindCard(card);
            if (predicted is null) return;
            var preview = (ChaosCardModel)predicted.MutablePreview;
            if (c.PreviewCard.Keywords.Contains(CardKeyword.Ethereal))
            {
                var reduction = Total(preview, "NCR:CostDownPerVoidPlayed");
                if (reduction > 0) preview.EnergyCost.AddThisCombat(-reduction);
            }
            var countedType = preview.Generated.Operations.Any(op => op.Template == "C:whileInCombat") ? CardType.Attack
                : preview.Generated.Operations.Any(op => op.Template == "C:whileInCombatSkillCostReduction") ? CardType.Skill : (CardType?)null;
            var template = c.PreviewCard.Type == countedType ? countedType == CardType.Attack
                ? "C:whileInCombat" : "C:whileInCombatSkillCostReduction" : null;
            var index = preview.Generated.Operations.ToList().FindIndex(op => op.Template == template);
            if (index >= 0) preview.EnergyCost.AddThisTurn(-Math.Max(1, preview.OperationAmount(index)));
        });
        AfterCardGeneratedForCombatMirrors.Registry.Register<T>((card, c) =>
        {
            if (c.Creator != card.Owner || c.PreviewCard.Owner != card.Owner || c.PreviewCard.Type != CardType.Status) return;
            if (c.State.FindCard(card) is not { } predicted) return;
            var preview = (ChaosCardModel)predicted.MutablePreview;
            var reduction = Total(preview, "D:CostDownWhenStatusGenerated");
            if (reduction > 0) preview.EnergyCost.AddUntilPlayed(-reduction);
        });
        AfterAttackMirrors.Registry.Register<T>((card, c) =>
        {
            if (c.Command.Attacker != c.Simulator.State.GetOsty(card.Owner)
                || !card.Generated.Operations.Any(op => op.Template == "NCR:SetCostZeroIfOstyAttacked")) return;
            if (c.State.FindCard(card) is { } predicted) predicted.MutablePreview.EnergyCost.SetThisTurn(0);
        });
        AfterCardPlayedMirrors.LateRegistry.Register<T>((card, c) =>
        {
            if (c.PreviewCard.Owner != card.Owner || c.State.FindCard(card) is not { } predicted) return;
            var preview = (ChaosCardModel)predicted.MutablePreview;
            var operations = preview.Generated.Operations;
            var index = operations.ToList().FindIndex(op => op.Template == "NCR:ReturnFromDiscardOnHighCostPlay");
            if (index >= 0)
            {
                var linked = operations[index].Parameters.GetValueOrDefault("triggerIndex", -1);
                var threshold = linked >= 0 && linked < index ? preview.OperationAmount(linked) : 2;
                if (c.CardPlay.Resources.EnergyValue >= threshold && predicted.GetPile(c.State)?.Type == PileType.Discard)
                    ReturnToHand(c.Simulator, predicted);
            }
            if (c.PreviewCard.Type != CardType.Skill || predicted.GetPile(c.State)?.Type == PileType.Hand) return;
            var skills = ((SimulatedCombatState)c.CombatState).GetSkillCardsPlayedThisTurn(card.Owner.Creature) + 1;
            if (skills > 0 && operations.Select((op, i) => (op, i)).Any(item =>
                    item.op.Template == "R:ReturnAfterSkillsPlayed" && skills % Math.Max(2, preview.OperationAmount(item.i)) == 0))
                ReturnToHand(c.Simulator, predicted);
        });
    }

    private static int Total(ChaosCardModel card, string template) => card.Generated.Operations
        .Select((op, index) => (op, index)).Where(item => item.op.Template == template)
        .Sum(item => Math.Max(1, card.OperationAmount(item.index)));

    internal static void ReturnToHand(CombatPredictionSimulator simulator, PredictedCard card)
    {
        if (card.GetPile(simulator.State)?.Type == PileType.Hand) return;
        if (simulator.State.GetPlayerCombatState(card.Preview.Owner).Hand.Cards.Count >= simulator.GetMaxHandSize(card.Preview.Owner)) return;
        simulator.AddToPile(card, PileType.Hand);
    }

    internal static void ExhaustMove(CombatPredictionSimulator simulator, PredictedCard card, bool causedByEthereal)
    {
        if (card.Preview is not ChaosCardModel preview || causedByEthereal || preview._postPlayExhaustMovePile is not { } destination) return;
        ((ChaosCardModel)card.MutablePreview)._postPlayExhaustMovePile = null;
        if (card.GetPile(simulator.State)?.Type == PileType.Exhaust)
            simulator.AddToPile(card, destination, destination == PileType.Draw ? CardPilePosition.Top : CardPilePosition.Bottom);
    }
}
