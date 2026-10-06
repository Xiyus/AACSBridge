using AutoAnthonyCombatSolverBridge.Translation;
using MegaCrit.Sts2.Core.ValueProps;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// lose_hp(immediate) 的精确镜像（self / selected_enemy / random_enemy 三个 target）。
/// 复刻 ChaosOperationExecutor.TryExecuteStructuredCommon L865-878：
///  - self：CreatureCmd.Damage(ctx, owner, amount, Unblockable|Unpowered|Move, card, cardPlay)
///    —— 6 参重载内部 dealer = cardSource?.Owner.Creature（反编译核实）；
///  - selected_enemy：CreatureCmd.Damage(ctx, target, amount, Unblockable|Unpowered,
///    card.Owner.Creature, card, cardPlay)——显式 dealer = 玩家生物；
///  - random_enemy（0.4.0）：镜像层已复刻 ExecuteWithResolvedTarget 的显式随机抽取
///    （分支 CombatTargets 流），目标 = 抽取结果 ?? cardPlay.Target（源码
///    state.Target ?? cardPlay.Target 的回落语义）。
/// 所有路径的 dealer 都是玩家生物，镜像为 simulator.Damage([...], amount, props,
/// ownerCreature, card, cardPlay)。
/// </summary>
public sealed class LoseHpHandler : IOperationHandler
{
    public string Describe => "lose_hp(immediate)：simulator.Damage(Unblockable|Unpowered) 精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target) switch
        {
            ("immediate", "self") => null,
            ("immediate", "selected_enemy") => null,
            ("immediate", "random_enemy") => null,
            _ => $"lose_hp 的 (variant={spec.Variant}, target={spec.Target}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var card = context.Card;
        var amount = context.ExecutableAmount;
        var mirror = context.Mirror;

        if (context.Shape.Spec.Target == "self")
        {
            mirror.Simulator.Damage([card.Owner.Creature], amount,
                ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move,
                card.Owner.Creature, mirror.Card, mirror.CardPlay);
            return;
        }

        // selected_enemy：出牌目标；random_enemy：镜像层抽取的显式随机目标
        // （复刻源码 state.Target ?? cardPlay.Target——随机抽取为 null 时回落出牌目标）
        var target = context.Shape.Spec.Target == "random_enemy"
            ? context.ResolvedTarget ?? mirror.CardPlay.Target
            : mirror.CardPlay.Target;
        if (target is null)
            return;    // 源码语义：无目标 = 成功 no-op
        mirror.Simulator.Damage([target], amount,
            ValueProp.Unblockable | ValueProp.Unpowered,
            card.Owner.Creature, mirror.Card, mirror.CardPlay);
    }
}
