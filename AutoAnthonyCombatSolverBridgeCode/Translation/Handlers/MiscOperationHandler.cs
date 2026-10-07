using AutoAnthony;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using AutoAnthonyCombatSolverBridge.CombatSolver;

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

public sealed class MiscOperationHandler : IOperationHandler
{
    public string Describe => "牌堆变形、引用卡移动、消耗攻击及其他独立操作";
    public string? ValidateSupport(OperationShape shape) => shape.Spec.Variant is "i_exhaustrandomattack" or "i_addexhaustedattackdamage"
        or "i_transform" or "d_transformstatusestofuel" or "d_returneventcardtohand" or "cl_puteventcardondrawtop"
        or "i_addcardreward" or "i_replayattack" or "d_replayeventcard" or "loss" or "next_n_turns" ? null : "独立操作形状未适配";

    public void Execute(OperationExecutionContext context)
    {
        var simulator = context.Mirror.Simulator;
        var combat = (SimulatedCombatState)context.Mirror.CombatState;
        var owner = context.Card.Owner;
        switch (context.Shape.Spec.Variant)
        {
            case "i_replayattack":
            case "d_replayeventcard":
                return; // Their structural effects are owned by ModifyCardPlayCount.
            case "i_exhaustrandomattack":
            {
                var cards = context.Mirror.OwnerState.Hand.Cards.Where(card => card.Preview.Type == CardType.Attack).ToList();
                var selected = context.Mirror.Rng.CombatCardSelection.NextItem(cards);
                if (selected is null) return;
                if (context.Resolution is { } resolution)
                    resolution.LastExhaustedAttackDamage = decimal.ToInt32(CurrentDamageResolver.Get(context, selected));
                simulator.Exhaust(selected);
                context.Resolution?.ExhaustedByCard.Add(selected);
                return;
            }
            case "i_addexhaustedattackdamage":
                context.Card.ExtraDamage += context.Resolution?.LastExhaustedAttackDamage ?? 0;
                return;
            case "i_transform":
            case "d_transformstatusestofuel":
            {
                var type = context.Shape.Spec.Variant == "i_transform" ? CardType.Attack : CardType.Status;
                var selected = context.Mirror.OwnerState.Hand.Cards.Where(card => card.Preview.Type == type && card.Preview.IsTransformable).ToList();
                ChaosDerivativeMirror.Transform(simulator, context.Card, context.Shape.OperationIndex, selected);
                return;
            }
            case "d_returneventcardtohand":
                if (context.ReferencedCard is { } returned) ChaosCardPassiveMirror.ReturnToHand(simulator, returned);
                return;
            case "cl_puteventcardondrawtop":
                if (context.ReferencedCard is { } moved && moved.GetPile(simulator.State)?.Type != PileType.Draw)
                    simulator.AddToPile(moved, PileType.Draw, CardPilePosition.Top);
                return;
            case "loss" when context.Shape.Spec.Opcode == "modify_orb_slots":
                simulator.AddOrbSlots(owner, -context.ExecutableAmount);
                return;
            case "next_n_turns" when context.Shape.Spec.Opcode == "restrict_block_from_cards":
                ((ICombatPredictionEffectSink)combat).ApplyPowerFromSource(typeof(NoBlockPower), owner.Creature,
                    context.ExecutableAmount, owner.Creature, context.Card);
                return;
            case "i_addcardreward":
                if (context.Mirror.CombatState.RunState.CurrentRoom is CombatRoom)
                {
                    ((ICombatPredictionEffectSink)combat).ApplyPowerFromSource(typeof(TheHuntPower), owner.Creature, 1, owner.Creature, context.Card);
                    combat.RecordLongTermResource(CorePowerSupport.TheHuntLongTermResourceValue);
                    combat.RecordGrowthReward(GrowthSource.TheHunt);
                }
                return;
            default:
                throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
        }
    }
}
