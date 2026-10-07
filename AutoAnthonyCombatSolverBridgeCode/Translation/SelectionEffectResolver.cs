using AutoAnthony;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using AutoAnthonyCombatSolverBridge.CombatSolver;

namespace AutoAnthonyCombatSolverBridge.Translation;

internal static class SelectionEffectResolver
{
    internal static bool TryExecute(OperationExecutionContext context)
    {
        if (context.Operation.CardTargetSlot is null) return false;
        var selected = context.SelectedCards;
        var simulator = context.Mirror.Simulator;
        switch (context.Shape.Spec.Variant)
        {
            case "ncr_addvoidtoselectedhandcard":
                if (selected.FirstOrDefault() is { } ethereal && !ethereal.Preview.Keywords.Contains(CardKeyword.Ethereal))
                    ethereal.MutablePreview.AddKeyword(CardKeyword.Ethereal);
                return true;
            case "ncr_addretaintoselectedhandcard":
                if (selected.FirstOrDefault() is { } retained && !retained.Preview.Keywords.Contains(CardKeyword.Retain))
                    retained.MutablePreview.AddKeyword(CardKeyword.Retain);
                return true;
            case "r_putselectedhandcardsondraw":
            case "r_putselectedhandcardondraw":
                simulator.AddToPile(selected, PileType.Draw, CardPilePosition.Top);
                if (context.Resolution is { } moved) moved.LastMovedCard = selected.FirstOrDefault();
                return true;
            case "r_copyselectedcolorlesscard":
                if (selected.FirstOrDefault() is { } copied)
                    simulator.AddGeneratedCardToCombat(copied.CreateClone(), PileType.Hand, context.Card.Owner,
                        CardPilePosition.Bottom, CardGenerationResultKind.Fixed);
                return true;
            case "cl_transformselectedhandcards":
            case "i_proxyatomic_begone":
            case "i_proxyatomic_guards":
            case "i_proxyatomic_charge":
            case "i_proxyatomic_seance":
                ChaosDerivativeMirror.Transform(simulator, context.Card, context.Shape.OperationIndex, selected);
                return true;
            case "r_playselectedskillmultipletimes":
                if (selected.FirstOrDefault() is { } played)
                    for (var repeat = 0; repeat < ChaosOperationExecutor.ExecutableOperationCount(context.Operation, context.ExecutableAmount); repeat++)
                    {
                        simulator.AutoPlay(played, nestedChoiceSourceId: context.Card.Id.Entry);
                        if (simulator.HasPendingChoice) break;
                    }
                return true;
            default:
                return false;
        }
    }
}
