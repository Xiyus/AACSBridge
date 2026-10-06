using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;

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
            ("all", "all_enemies") => null,
            _ => $"deal_damage 的 (variant={spec.Variant}, target={spec.Target}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var spec = context.Shape.Spec;
        var card = context.Card;

        var amount = context.ExecutableAmount;
        var damage = context.RuntimeValue("damage", amount);
        var hits = Math.Max(0, context.RuntimeValue("hits", 1));
        // DamageAndHits 的无伤害修饰符路径（校验层只放行 M:base/strength_scaled 格挡修饰符）：
        // damage = baseDamage + ExtraDamage；hits 下限 0。
        var finalDamage = damage + card.ExtraDamage;
        if (hits == 0)
            return;    // 源码语义：零 hits = 成功 no-op，不是失败

        var mirror = context.Mirror;

        // Power 卡：Unpowered 逐 hit 路径（源码 L1119-1153）
        if (card.Type == CardType.Power)
        {
            var props = context.DamageProps;    // Power → ValueProp.Unpowered
            var dealer = card.Owner.Creature;
            if (spec.Variant == "all")
            {
                for (var hit = 0; hit < hits; hit++)
                    mirror.Simulator.Damage(mirror.CombatState.HittableEnemies.ToArray(), finalDamage,
                        props, dealer, mirror.Card, mirror.CardPlay);
                return;
            }

            var powerTarget = mirror.CardPlay.Target;
            if (powerTarget is null)
                return;    // 源码语义：无目标 = 成功 no-op
            for (var hit = 0; hit < hits; hit++)
                mirror.Simulator.Damage([powerTarget], finalDamage,
                    props, dealer, mirror.Card, mirror.CardPlay);
            return;
        }

        // 普通卡：攻击命令管线
        switch (spec.Variant)
        {
            case "selected":
            {
                // 与源码 state.Target ?? cardPlay.Target 一致（immediate 执行时两者同源）
                var target = mirror.CardPlay.Target;
                if (target is null)
                    return;    // 源码语义：无目标 = 成功 no-op
                DamageCmd.Attack(finalDamage)
                    .WithHitCount(hits)
                    .FromCard(card, mirror.CardPlay)
                    .Targeting(target)
                    .Simulate(mirror.Simulator);
                return;
            }
            case "all":
                DamageCmd.Attack(finalDamage)
                    .WithHitCount(hits)
                    .FromCard(card, mirror.CardPlay)
                    .TargetingAllOpponents(mirror.CombatState)
                    .Simulate(mirror.Simulator);
                return;
            default:
                throw new UnsupportedRuntimeSpecException(spec.Opcode, spec.Variant);
        }
    }
}
