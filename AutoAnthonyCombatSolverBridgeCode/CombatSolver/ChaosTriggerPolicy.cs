using AutoAnthony;
using ChaosCardGenerator;
using AutoAnthonyCombatSolverBridge.Translation;

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

/// <summary>The bounded 0.6 contract. Every linked payload is checked before arming.</summary>
internal static class ChaosTriggerPolicy
{
    private static readonly Lazy<HashSet<string>> KnownFlags = new(() => Enum.GetValues<GeneratedCharacter>()
        .SelectMany(character => CharacterComponentCatalogs.Get(character).Atoms)
        .SelectMany(atom => OperationRuntimeSpecCompiler.GetOrCompile(atom).Flags)
        .Concat(new[] { "requires_event_amount_payload", "requires_event_card_payload", "requires_referenced_card_payload",
            "event_enemy_reference", "host_discard_lifecycle", "legacy_inline_double_x", "upgrade_generated" })
        .ToHashSet(StringComparer.Ordinal));
    internal static string? ValidateTrigger(GeneratorOperation operation, OperationRuntimeSpec spec)
    {
        if (!ChaosOperationExecutor.RequiresCompositePower(operation) || spec.Condition is not null
            || spec.Opcode != "trigger" || spec.Trigger is not { } trigger
            || operation.Parameters.ContainsKey("triggerIndex") || operation.CardTargetSlot is not null)
            return "不是已建模的复合 Power 触发器";
        var supported = (trigger.Kind, trigger.Lifetime) switch
        {
            ("next_turn_start", "next_turn") => true,
            ("next_turns_start", "next_n_turns") => true,
            ("turns_elapsed", "delayed") => true,
            ("cards_drawn_threshold" or "cards_played_this_turn_threshold", "combat") => true,
            ("turn_start" or "turn_end" or "card_played" or "attack_played" or "skill_played"
                or "power_played" or "card_drawn" or "card_exhausted", "combat") => true,
            // Batch AQ-2：this_turn lifetime（回合结束过期——TurnLimitedTriggerExpired 机制已有）
            ("attack_played", "this_turn") => true,
            ("vulnerable_enemy_damage_reduction", "this_turn") => true,
            ("energy_cost_at_least_card_played", "combat") => true,
            ("energy_spent_threshold" or "stars_spent_threshold", "combat") => true,
            ("first_card_played_each_turn" or "first_attack_played_each_turn" or "first_zero_cost_attack_played_each_turn"
                or "nth_attack_played_this_turn" or "first_attack_or_skill_each_turn" or "soul_played"
                or "ethereal_card_played" or "derivative_played" or "next_attack", "combat") => true,
            ("next_attack" or "next_attacks_this_turn", "this_turn") => true,
            ("block_gained" or "owner_hp_lost_during_turn" or "osty_hp_lost" or "card_generated"
                or "status_generated" or "orb_channeled" or "lightning_orb_evoked" or "stars_spent_or_gained"
                or "draw_pile_shuffled" or "attack_damaged_enemy" or "attack_dealt_damage" or "attack_received"
                or "vulnerable_applied" or "doom_applied" or "enemy_debuff_applied", "combat") => true,
            ("card_drawn" or "card_played" or "attack_received", "this_turn") => true,
            ("strike_card_drawn" or "ethereal_card_drawn" or "card_drawn_during_turn"
                or "first_status_drawn_each_turn", "combat") => true,
            _ => false
        };
        if (!supported || (trigger.ThresholdSlot is not null
                && (trigger.Kind is not ("energy_cost_at_least_card_played" or "energy_spent_threshold" or "stars_spent_threshold" or "nth_attack_played_this_turn"
                    or "cards_drawn_threshold" or "cards_played_this_turn_threshold" or "turns_elapsed" or "next_attacks_this_turn")
                    || trigger.ThresholdSlot != "threshold"))
            || (trigger.DurationSlot is not null && trigger.Kind is not ("next_turns_start" or "next_attacks_this_turn")))
            return $"触发器 {trigger.Kind}/{trigger.Lifetime} 不在 0.6.0 支持矩阵";
        if (spec.Values.Any(value => value.Source != "fixed"))
            return "触发器计数只支持 fixed 值源";
        return null;
    }

    internal static string? ValidatePayload(OperationRuntimeSpec spec)
    {
        if (spec.Condition is not null || spec.Trigger is not null) return "触发收益不能嵌套触发器/条件";
        if (spec.Flags.Any(flag => !KnownFlags.Value.Contains(flag))) return "触发收益含未知标记";
        if (spec.Values.Any(value => value.Source is not ("fixed" or "energy_x" or "star_x" or "special_x")))
            return "触发收益含未知值源";
        if (ChaosCardChoiceMirror.IsSupportedSelection(spec) || DamageModifierResolver.IsSupportedModifier(spec)
            || spec.Variant is "i_replayattack" or "d_replayeventcard") return null;
        var handler = OperationHandlerRegistry.Instance.TryGet(OperationKey.FromSpec(spec));
        return handler is null ? $"触发收益无 handler：{spec.Opcode}/{spec.Variant}"
            : handler.ValidateSupport(new OperationShape(0, OperationScope.NonTargeted, spec));
    }
}
