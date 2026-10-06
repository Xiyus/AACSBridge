using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// 条件门控评估器（0.9.0）：复刻 ChaosOperationExecutor.ConditionMatches L3650-3693。
/// 条件操作（ConditionalTrigger scope + spec.Condition）只评估不执行；
/// 门控靠 payoff 的 Parameters["triggerIndex"] 指回条件操作索引。
/// 评估时点=条件自身列表位置，结果按索引缓存（EvaluateConditionOnce 语义）。
/// 支持的 16 种 kind（读简单状态的高频族）+ 默认放行。
/// </summary>
internal static class ConditionEvaluator
{
    /// <summary>支持的条件 kind（校验层放行这些 ConditionalTrigger 操作）。</summary>
    internal static bool IsSupportedCondition(string? kind)
        => kind switch
        {
            "fatal" or "exhaust_pile_minimum" or "card_exhausted_this_turn"
                or "owner_lost_hp_this_turn" or "target_has_vulnerable" or "target_has_poison"
                or "draw_pile_empty" or "last_drawn_card_is_skill"
                or "cards_played_this_turn_below" or "enemy_intends_attack"
                or "osty_alive" or "doom_applied_this_turn"
                or "first_play_of_this_card_this_turn" or "osty_attacked_this_turn"
                or "no_attacks_in_hand" or "hand_empty" => true,
            // 依赖前缀条件（走 DependencyConditionMatches 路径）
            "has_frost_orb" or "cards_played_this_turn_at_least" or "energy_x_at_least" => true,
            _ => false,
        };

    /// <summary>
    /// 评估条件。复刻 ConditionMatches L3650-3693 的 switch。
    /// threshold 读原始 spec BaseValue（不吃升级 delta——与源码 RequireStructured 口径一致）。
    /// </summary>
    internal static bool Evaluate(ChaosCardModel card, GeneratorOperation conditionOp,
        CardOnPlayMirrorContext mirror, Dictionary<int, bool> conditionResults)
    {
        var spec = OperationRuntimeSpecCompiler.RequireStructured(conditionOp);
        var kind = spec?.Condition?.Kind;
        if (kind is null) return true;
        var threshold = spec?.Values.FirstOrDefault(value => value.Id == "threshold")?.BaseValue ?? 1;
        var owner = card.Owner;
        var playerState = mirror.Simulator.State.GetPlayerCombatState(owner);
        var target = mirror.CardPlay.Target;

        return kind switch
        {
            // fatal：本次结算中伤害命令实际击杀的累积（桥局部状态）
            "fatal" => conditionResults.GetValueOrDefault(-1),    // -1 = LastAttackKilled 特殊键
            "exhaust_pile_minimum" => playerState.ExhaustPile.Cards.Count >= threshold,
            "draw_pile_empty" => playerState.DrawPile.IsEmpty,
            "hand_empty" => playerState.Hand.IsEmpty,
            "no_attacks_in_hand" => playerState.Hand.Cards.All(candidate =>
                candidate.Preview.Type != CardType.Attack),
            "target_has_vulnerable" => target is not null
                && mirror.CombatState is global::CombatSolver.SimulatedCombatState combat
                && combat.GetAmount<VulnerablePower>(target) > 0,
            "target_has_poison" => target is not null
                && mirror.CombatState is global::CombatSolver.SimulatedCombatState combat2
                && combat2.GetAmount<PoisonPower>(target) > 0,
            "enemy_intends_attack" => target is not null
                && mirror.CombatState is global::CombatSolver.SimulatedCombatState combat3
                && combat3.IsEnemyIntendingToAttack(target),
            "osty_alive" => mirror.Simulator.State.GetOsty(owner) is { } osty
                && mirror.Simulator.State.GetCreature(osty).IsAlive,
            // 以下需要历史计数——暂用模拟器的等价 API（简化口径）
            "cards_played_this_turn_below" => mirror.CombatState is global::CombatSolver.SimulatedCombatState combat4
                && combat4.GetCardsPlayedThisTurn(owner.Creature) < threshold,
            "card_exhausted_this_turn" => mirror.CombatState is global::CombatSolver.SimulatedCombatState combat5
                && combat5.WasCardExhaustedThisTurn(owner.Creature),
            "owner_lost_hp_this_turn" => mirror.CombatState is global::CombatSolver.SimulatedCombatState combat6
                && combat6.HasLostHpThisTurn(owner.Creature),
            "doom_applied_this_turn" => mirror.CombatState is global::CombatSolver.SimulatedCombatState combat7
                && combat7.WasDoomAppliedThisTurn(owner.Creature),
            // 简化实现（依赖前缀条件——真实走 DependencyConditionMatches）
            "has_frost_orb" => playerState.OrbQueue.Orbs.Any(orb => orb is FrostOrb),
            "cards_played_this_turn_at_least" => mirror.CombatState is global::CombatSolver.SimulatedCombatState combat8
                && combat8.GetCardsPlayedThisTurn(owner.Creature) >= threshold,
            "energy_x_at_least" => card.ResolvedEnergyXValue >= Math.Max(1, threshold),
            // 以下暂不支持（返回 false = fail-closed）
            "last_drawn_card_is_skill" => false,
            "first_play_of_this_card_this_turn" => false,
            "osty_attacked_this_turn" => mirror.CombatState is global::CombatSolver.SimulatedCombatState combat9
                && combat9.GetCreatureAttacksThisTurn(mirror.Simulator.State.GetOsty(owner)) > 0,
            _ => true,    // 默认放行（与源码一致）
        };
    }
}
