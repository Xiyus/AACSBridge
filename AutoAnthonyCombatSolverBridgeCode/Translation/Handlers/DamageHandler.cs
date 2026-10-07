using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using AutoAnthonyCombatSolverBridge.CombatSolver;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// deal_damage 的精确镜像（selected / all 两个 variant）。
/// 复刻 ChaosOperationExecutor.TryExecuteStructuredDamage 的两条路径：
///  - 普通卡（Attack/Skill 等）：DamageCmd.Attack(damage).WithHitCount(hits).FromCard(card, cardPlay)
///    .Targeting(...).Simulate(simulator)——游戏攻击管线（力量/虚弱/易伤由模拟器施加）；
///  - Power 卡（0.2.0）：Unpowered 逐 hit CreatureCmd.Damage 路径——每 hit 一次
///    simulator.Damage(目标集, damage, Unpowered, dealer, card, cardPlay)，all 时每 hit
///    重读 HittableEnemies（死亡敌人即时退出，与源码循环一致）。
/// 数值链：amount = OperationAmount(+外部加成) → damage 槽覆盖 → hits 槽（默认 1）→
/// + ExtraDamage（DamageAndHits 无伤害修饰符路径）。
/// 源码语义细节：hits == 0 是成功 no-op；selected 无目标也是成功 no-op。
/// </summary>
public sealed class DamageHandler : IOperationHandler
{
    public string Describe => "deal_damage(selected/all)：DamageCmd.Attack + Power Unpowered 逐 hit 精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target) switch
        {
            ("selected", "selected_enemy") => null,
            ("selected", "event_enemy") => null,
            ("all", "all_enemies") => null,
            ("random", "random_enemy") => null,
            ("selected_energy_x_hits" or "selected_energy_x_threshold" or "cards_played_combat", "selected_enemy") => null,
            ("random_star_x_hits", "random_enemy") => null,
            _ => $"deal_damage 的 (variant={spec.Variant}, target={spec.Target}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var spec = context.Shape.Spec;
        var card = context.Card;

        var amount = context.ExecutableAmount;
        decimal damage = context.RuntimeValue("damage", amount);
        damage = ChaosOperationExecutor.ResolveTriggeredRollingDamage(card.Generated.Operations,
            context.Shape.OperationIndex, damage, context.EventAmount, context.IsTriggered);
        var hits = Math.Max(0, context.RuntimeValue("hits", 1));
        if (spec.Variant == "cards_played_combat")
            damage = ChaosHistory.Finished(context.Mirror.Simulator, card.Owner, thisTurn: false).Count();
        if (spec.Variant == "selected_energy_x_threshold")
        {
            var thresholdIndex = Enumerable.Range(0, card.Generated.Operations.Count)
                .FirstOrDefault(index => ChaosOperationExecutor.EffectiveRuntimeSpec(card, index).Condition?.Kind == "energy_x_at_least", -1);
            var threshold = thresholdIndex >= 0
                ? ChaosOperationExecutor.RuntimeSpecValue(card, thresholdIndex, "threshold", 0)
                : context.RuntimeValue("threshold", 0);
            if (threshold > 0 && hits >= threshold && (spec.Flags.Contains("legacy_inline_double_x")
                || Enumerable.Range(0, card.Generated.Operations.Count)
                    .Any(index => ChaosOperationExecutor.EffectiveRuntimeSpec(card, index).Variant == "r_doubleenergyx"))) hits *= 2;
        }
        // 0.9.0：修饰符解析（DamageModifierResolver 复刻 DamageAndHits 数学）
        // ——含 ExtraDamage 叠加、前缀缩放 baseHits、动态总命中替换、附加命中叠加
        var (finalDamage, resolvedHits) = DamageModifierResolver.Resolve(context, damage, hits);
        hits = resolvedHits;
        if (hits == 0)
            return;    // 源码语义：零 hits = 成功 no-op，不是失败
        if (context.Resolution is { } local) local.LastDamageDealt = 0;

        var mirror = context.Mirror;

        // Power 卡：Unpowered 逐 hit 路径（源码 L1119-1153）
        if (card.Type == CardType.Power || context.IsTriggered && !context.UsePoweredCardDamage)
        {
            var props = context.DamageProps;    // Power → ValueProp.Unpowered
            var dealer = card.Owner.Creature;
            if (spec.Target == "all_enemies")
            {
                for (var hit = 0; hit < hits; hit++)
                    RecordKills(mirror.Simulator.Damage(mirror.CombatState.HittableEnemies.ToArray(), finalDamage,
                        props, dealer, mirror.Card, mirror.CardPlay), context);
                return;
            }

            if (spec.Target == "random_enemy")
            {
                // 源码 L1183-1194：每 hit 独立从分支 CombatTargets 流抽一个敌人
                for (var hit = 0; hit < hits; hit++)
                {
                    var randomTarget = mirror.Rng.CombatTargets.NextItem(mirror.CombatState.HittableEnemies);
                    if (randomTarget is null)
                        continue;
                    RecordKills(mirror.Simulator.Damage([randomTarget], finalDamage,
                        props, dealer, mirror.Card, mirror.CardPlay), context);
                }
                return;
            }

            var powerTarget = context.Target;
            if (powerTarget is null)
                return;    // 源码语义：无目标 = 成功 no-op
            for (var hit = 0; hit < hits; hit++)
                RecordKills(mirror.Simulator.Damage([powerTarget], finalDamage,
                    props, dealer, mirror.Card, mirror.CardPlay), context);
            return;
        }

        // 普通卡：攻击命令管线
        switch (spec.Target)
        {
            case "selected_enemy":
            case "event_enemy":
            {
                // 与源码 state.Target ?? cardPlay.Target 一致（immediate 执行时两者同源）
                var target = context.Target;
                if (target is null)
                    return;    // 源码语义：无目标 = 成功 no-op
                var attack = DamageCmd.Attack(finalDamage)
                    .WithHitCount(hits)
                    .FromCard(card, mirror.CardPlay)
                    .Targeting(target);
                attack.Simulate(mirror.Simulator);
                RecordKills(attack.Results.SelectMany(result => result).ToArray(), context);
                return;
            }
            case "all_enemies":
            {
                var repeatOnKill = Enumerable.Range(0, card.Generated.Operations.Count)
                    .Any(index => ChaosOperationExecutor.EffectiveRuntimeSpec(card, index).Variant == "m_repeatareaonkill");
                if (repeatOnKill)
                {
                    var remaining = hits;
                    while (remaining-- > 0)
                    {
                        var echo = DamageCmd.Attack(finalDamage).FromCard(card, mirror.CardPlay).TargetingAllOpponents(mirror.CombatState);
                        echo.Simulate(mirror.Simulator);
                        if (mirror.Simulator.HasPendingChoice) return;
                        var results = echo.Results.SelectMany(result => result).ToArray();
                        remaining += results.Count(result => result.WasTargetKilled);
                        RecordKills(results, context);
                    }
                    return;
                }
                var attack = DamageCmd.Attack(finalDamage)
                    .WithHitCount(hits)
                    .FromCard(card, mirror.CardPlay)
                    .TargetingAllOpponents(mirror.CombatState);
                attack.Simulate(mirror.Simulator);
                RecordKills(attack.Results.SelectMany(result => result).ToArray(), context);
                return;
            }
            case "random_enemy":
                // 镜像层已按源码复刻 ExecuteWithResolvedTarget 的显式随机目标抽取
                // （消耗一次 CombatTargets，结果不影响本路径）；命令自身的逐 hit 随机
                // 目标由模拟器用分支 RNG 结算（与真实命令的 RNG 用法一致）。
                var randomAttack = DamageCmd.Attack(finalDamage)
                    .WithHitCount(hits)
                    .FromCard(card, mirror.CardPlay)
                    .TargetingRandomOpponents(mirror.CombatState);
                randomAttack.Simulate(mirror.Simulator);
                RecordKills(randomAttack.Results.SelectMany(result => result).ToArray(), context);
                return;
            default:
                throw new UnsupportedRuntimeSpecException(spec.Opcode, spec.Variant);
        }
    }

    /// <summary>源码 L1171：state.LastAttackKilled |= results.Any(r => r.WasTargetKilled)。</summary>
    private static void RecordKills(IReadOnlyList<global::MegaCrit.Sts2.Core.Entities.Creatures.DamageResult> results,
        OperationExecutionContext context)
    {
        if (context.Resolution is { } resolution)
        {
            resolution.LastAttackKilled |= results.Any(result => result.WasTargetKilled);
            resolution.LastDamageDealt += decimal.ToInt32(results.Sum(result => result.TotalDamage + result.OverkillDamage));
        }
    }
}
