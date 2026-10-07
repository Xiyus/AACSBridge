using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using AutoAnthonyCombatSolverBridge.Translation;
using AutoAnthonyCombatSolverBridge.Bootstrap;

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

internal static class ChaosCardExhaustMirror
{
    internal static bool IsTrigger(GeneratorOperation operation) =>
        operation.CardTargetSlot == "thisCard" && operation.RuntimeSpec?.Trigger?.Kind == "self_exhausted";
    internal static bool IsLifecycleTrigger(GeneratorOperation operation) => IsTrigger(operation)
        || operation.RuntimeSpec?.Trigger?.Kind == "turn_end_if_self_in_exhaust"
        || operation.Template == "R:AtTurnStartIfInExhaust";

    internal static string? Validate(ChaosCardModel card, int triggerIndex)
    {
        var trigger = card.Generated.Operations[triggerIndex];
        var spec = trigger.RuntimeSpec;
        if (!IsLifecycleTrigger(trigger) || trigger.Scope != OperationScope.ConditionalTrigger
            || trigger.Parameters.ContainsKey("triggerIndex") || spec?.Opcode != "trigger"
            || spec.Condition is not null || spec.Trigger is not { Lifetime: "immediate", ThresholdSlot: null, DurationSlot: null })
            return "本卡消耗触发器形状未适配";
        var hasPayload = false;
        for (var index = triggerIndex + 1; index < card.Generated.Operations.Count; index++)
        {
            var effect = card.Generated.Operations[index];
            if (effect.Parameters.GetValueOrDefault("triggerIndex", -1) != triggerIndex) continue;
            hasPayload = true;
            if (effect.Scope is OperationScope.AbilityTrigger or OperationScope.ConditionalTrigger or OperationScope.AbilityRule)
                return "本卡消耗收益含嵌套操作或事件卡槽";
            var payload = ChaosOperationExecutor.EffectiveRuntimeSpec(card, index);
            var reason = ChaosTriggerPolicy.ValidatePayload(payload);
            if (reason is not null) return reason;
            if (effect.Scope == OperationScope.Modifier || ChaosCardChoiceMirror.IsSupportedSelection(payload)) continue;
            var handler = OperationHandlerRegistry.Instance.TryGet(OperationKey.FromSpec(payload));
            if (handler is null) return $"本卡消耗收益无 handler：{payload.Opcode}/{payload.Variant}";
            reason = handler.ValidateSupport(new OperationShape(index, effect.Scope, payload));
            if (reason is not null) return reason;
        }
        return hasPayload ? null : "本卡消耗触发器没有收益";
    }

    // Called after the global exhaust listeners, just like AA's Hook.AfterCardExhausted postfix.
    internal static void Execute(CombatPredictionSimulator simulator, PredictedCard predicted, string kind = "self_exhausted")
    {
        if (predicted.Preview is not ChaosCardModel preview) return;
        var triggers = Enumerable.Range(0, preview.Generated.Operations.Count)
            .Where(index => IsLifecycleTrigger(preview.Generated.Operations[index])
                && preview.Generated.Operations[index].RuntimeSpec?.Trigger?.Kind == kind).ToArray();
        if (triggers.Length == 0) return;
        if (!BridgeBootstrap.IsReady)
            throw ChaosCompositePowerMirror.Unsupported("桥未完成初始化，拒绝本卡消耗预测");
        if (simulator.HasPendingChoice)
        {
            simulator.RejectExecutionContinuation();
            return;
        }
        var card = (ChaosCardModel)predicted.MutablePreview;
        foreach (var triggerIndex in triggers)
        {
            var reason = Validate(card, triggerIndex);
            if (reason is not null) throw ChaosCompositePowerMirror.Unsupported(reason);
        }
        var play = new CardPlay
        {
            Card = card, Player = card.Owner, Target = null, ResultPile = PileType.Discard,
            Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
            IsAutoPlay = true, PlayIndex = 0, PlayCount = 1
        };
        foreach (var triggerIndex in triggers)
        {
            Creature? target = null;
            if (!ChaosClauseMirror.ResolveTarget(simulator, card, triggerIndex, null, ref target)) continue;
            if (!ChaosClauseMirror.Execute(simulator, predicted, card, play, triggerIndex, new OperationResolutionState(),
                predicted, 0, target, powered: card.Type != CardType.Power)) return;
        }
    }
}
