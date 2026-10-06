using AutoAnthonyCombatSolverBridge.Translation;
using MegaCrit.Sts2.Core.Commands;
using CombatSolver.Engine.InCombat.Simulation;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// deal_damage 的精确镜像（selected / all 两个 variant）。
/// 复刻 ChaosOperationExecutor.TryExecuteStructuredDamage 的非 Power 直打路径：
///   amount = OperationAmount（+外部加成）→ damage 槽覆盖 → hits 槽（默认 1）→
///   + ExtraDamage（DamageAndHits 无修饰符路径）→ DamageCmd.Attack(...).WithHitCount(hits)
///   .FromCard(card, cardPlay).Targeting(...).Simulate(simulator)
/// 源码语义细节：hits == 0 是成功 no-op；selected 无目标也是成功 no-op。
/// Power 类型卡走 Unpowered 逐 hit CreatureCmd.Damage 路径，0.1.0 不支持（校验层拒绝）。
/// </summary>
public sealed class DamageHandler : IOperationHandler
{
    public string Describe => "deal_damage(selected/all)：DamageCmd.Attack 精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target) switch
        {
            ("selected", "selected_enemy") => null,
            ("all", "all_enemies") => null,
            _ => $"deal_damage 的 (variant={spec.Variant}, target={spec.Target}) 组合不在 0.1.0 支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var spec = context.Shape.Spec;
        var card = context.Card;

        var amount = context.ExecutableAmount;
        var damage = context.RuntimeValue("damage", amount);
        var hits = Math.Max(0, context.RuntimeValue("hits", 1));
        // DamageAndHits 的无修饰符路径（校验层已拒绝全部 Modifier 操作）：
        // damage = baseDamage + ExtraDamage；hits 下限 0。
        var finalDamage = damage + card.ExtraDamage;
        if (hits == 0)
            return;    // 源码语义：零 hits = 成功 no-op，不是失败

        var mirror = context.Mirror;
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
