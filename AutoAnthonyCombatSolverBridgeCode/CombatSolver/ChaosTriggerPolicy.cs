using AutoAnthony;
using ChaosCardGenerator;

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

/// <summary>The bounded 0.6 contract. Every linked payload is checked before arming.</summary>
internal static class ChaosTriggerPolicy
{
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
            ("turn_start" or "turn_end" or "card_played" or "attack_played" or "skill_played"
                or "power_played" or "card_drawn" or "card_exhausted", "combat") => true,
            ("strike_card_drawn" or "ethereal_card_drawn" or "card_drawn_during_turn"
                or "first_status_drawn_each_turn", "combat") => true,
            _ => false
        };
        if (!supported || trigger.ThresholdSlot is not null
            || (trigger.DurationSlot is not null && trigger.Kind != "next_turns_start"))
            return $"触发器 {trigger.Kind}/{trigger.Lifetime} 不在 0.6.0 支持矩阵";
        if (spec.Values.Any(value => value.Source != "fixed"))
            return "触发器计数只支持 fixed 值源";
        return null;
    }

    internal static string? ValidatePayload(OperationRuntimeSpec spec)
    {
        // No choices, card mutations, event references, or effects which recursively emit our events.
        // This also keeps trigger execution atomic: no pending-choice continuation is approximated.
        if (spec.Condition is not null || spec.Trigger is not null
            || spec.Flags.Any(flag => flag is not
                ("damage_reference" or "block_reference" or "energy_reference" or "random_enemy_reference"
                 or "has_numeric_literal" or "scalable_reward_wording" or "printed_damage_value"
                 or "printed_block_value" or "immediate_block_gain" or "all_enemies_reference"
                 or "heal_reference" or "leading_heal_reference" or "zero_damage" or "count_unit_reference"))
            || spec.Values.Any(value => value.Source != "fixed"))
            return "触发收益含条件、额外标记或非 fixed 值源";
        return (spec.Opcode, spec.Variant, spec.Target) switch
        {
            ("gain_block", "immediate", "self") => null,
            ("gain_energy", "immediate", "self") => null,
            ("heal", "immediate", "self") => null,
            ("deal_damage", "all", "all_enemies") => null,
            ("deal_damage", "random", "random_enemy") => null,
            _ => "0.6.0 触发收益只支持自身格挡/能量/治疗、全体或随机伤害"
        };
    }
}
