using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver;
using CombatSolver.Engine.Common;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// apply_power 的精确镜像（0.2.0：20 个 variant）。
/// 复刻 ChaosOperationExecutor 的两条分派路径：
///  1. TryExecuteStructuredSelfPower（优先）：target=self + 自增益路由 → PowerCmd.Apply&lt;X&gt;(owner, ±amount)
///  2. 敌方分派：vulnerable/weak/strength_loss/strength_loss_this_turn/strength_gain
///     （selected_enemy 或 all_enemies）、vulnerable_double（翻倍现有易伤）。
/// 镜像入口：SimulatedCombatState 实现的 ICombatPredictionEffectSink.ApplyPowerFromSource
/// （Type, target, amount, applier, cardSource）——与真实 PowerCmd.Apply(ctx, target, amount,
/// dealer, card) 的 cardSource 语义逐位对齐（反编译核实第 6 参即 cardSource，喂给
/// ModifyPowerAmountGiven 等遗物钩子）。
/// 力量/易伤等 Power 数值一律从分支状态读（combat.GetPower/GetAmount&lt;T&gt;(creature)），
/// 绝不读 live creature。
/// 不支持的 variant（poison/random、focus_loss_this_turn 的绑定 Power）由校验层拒绝。
/// </summary>
public sealed class PowerHandler : IOperationHandler
{
    public string Describe => "apply_power(20 variant)：ApplyPowerFromSource 精确镜像";

    /// <summary>自增益路由：variant → (Power 类型, 数值符号)。特殊路由（retain/blur/per_target）单独处理。</summary>
    private static readonly Dictionary<string, (Type PowerType, int Sign)> SelfRoutes = new()
    {
        ["dexterity_gain"] = (typeof(DexterityPower), 1),
        ["dexterity_loss"] = (typeof(DexterityPower), -1),
        ["dexterity_gain_this_turn"] = (typeof(AnticipatePower), 1),
        ["doom"] = (typeof(DoomPower), 1),
        ["focus_loss"] = (typeof(FocusPower), -1),
        ["thorns"] = (typeof(ThornsPower), 1),
        ["intangible"] = (typeof(IntangiblePower), 1),
        ["plating"] = (typeof(PlatingPower), 1),
        ["strength"] = (typeof(StrengthPower), 1),
        ["strength_this_turn"] = (typeof(SetupStrikePower), 1),
        ["vigor"] = (typeof(VigorPower), 1),
        ["strength_loss"] = (typeof(StrengthPower), -1),
        ["strength_loss_this_turn"] = (typeof(ManglePower), 1),
    };

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        if (spec.Target == "self" && (SelfRoutes.ContainsKey(spec.Variant)
                                      || spec.Variant is "retain_hand_this_turn" or "blur"
                                          or "strength_per_target_vulnerable"))
            return null;
        return (spec.Variant, spec.Target) switch
        {
            ("vulnerable", "selected_enemy") => null,
            ("vulnerable", "all_enemies") => null,
            ("weak", "selected_enemy") => null,
            ("weak", "all_enemies") => null,
            ("strength_loss", "selected_enemy") => null,
            ("strength_loss_this_turn", "selected_enemy") => null,
            ("strength_loss_this_turn", "all_enemies") => null,
            ("strength_gain", "selected_enemy") => null,
            ("vulnerable_double", "selected_enemy") => null,
            ("poison", "selected_enemy") => null,
            ("poison", "all_enemies") => null,
            _ => $"apply_power 的 (variant={spec.Variant}, target={spec.Target}) 组合不在 0.2.0 支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var spec = context.Shape.Spec;
        var card = context.Card;
        var mirror = context.Mirror;
        if (mirror.CombatState is not ICombatPredictionEffectSink effects)
            throw new InvalidOperationException("apply_power 需要实现了效果汇的分支战斗状态。");
        var owner = card.Owner.Creature;

        // 1) 自增益路由（TryExecuteStructuredSelfPower 优先于敌方分派）
        if (spec.Target == "self")
        {
            switch (spec.Variant)
            {
                case "retain_hand_this_turn":
                    // 源码：固定施加 1 层，不读操作数值
                    effects.ApplyPowerFromSource(typeof(RetainHandPower), owner, 1, owner, card);
                    return;
                case "blur":
                    // 源码：固定施加 1 层
                    effects.ApplyPowerFromSource(typeof(BlurPower), owner, 1, owner, card);
                    return;
                case "strength_per_target_vulnerable":
                {
                    // 源码：力量 = 目标当前易伤层数 × max(0, amount)；无目标记警告后 no-op
                    var target = mirror.CardPlay.Target;
                    if (target is null)
                        return;
                    var strength = GetBranchAmount<VulnerablePower>(context, target) * Math.Max(0, context.ExecutableAmount);
                    effects.ApplyPowerFromSource(typeof(StrengthPower), owner, strength, owner, card);
                    return;
                }
                default:
                {
                    var (powerType, sign) = SelfRoutes[spec.Variant];
                    effects.ApplyPowerFromSource(powerType, owner, sign * context.ExecutableAmount, owner, card);
                    return;
                }
            }
        }

        // 2) 敌方分派
        var amount = context.ExecutableAmount;
        switch (spec.Variant)
        {
            case "vulnerable_double":
            {
                // 源码：目标当前易伤 > 0 时施加等量易伤（翻倍）
                var target = mirror.CardPlay.Target;
                if (target is null)
                    return;
                var current = GetBranchAmount<VulnerablePower>(context, target);
                if (current > 0)
                    effects.ApplyPowerFromSource(typeof(VulnerablePower), target, current, owner, card);
                return;
            }
            case "vulnerable":
                ApplyToEnemies(context, effects, typeof(VulnerablePower), amount);
                return;
            case "weak":
                ApplyToEnemies(context, effects, typeof(WeakPower), amount);
                return;
            case "strength_loss":
                ApplyToEnemies(context, effects, typeof(StrengthPower), -amount);
                return;
            case "strength_loss_this_turn":
                ApplyToEnemies(context, effects, typeof(ManglePower), amount);
                return;
            case "strength_gain":
                ApplyToEnemies(context, effects, typeof(StrengthPower), amount);
                return;
            case "poison":
                ApplyToEnemies(context, effects, typeof(PoisonPower), amount);
                return;
            default:
                throw new UnsupportedRuntimeSpecException(spec.Opcode, spec.Variant);
        }
    }

    /// <summary>敌方分派：all_enemies 逐敌施加（与源码的列表重载/foreach 等价），否则施加到出牌目标。</summary>
    private static void ApplyToEnemies(OperationExecutionContext context, ICombatPredictionEffectSink effects,
        Type powerType, int amount)
    {
        var card = context.Card;
        var owner = card.Owner.Creature;
        if (context.Shape.Spec.Target == "all_enemies")
        {
            foreach (var enemy in context.Mirror.CombatState.HittableEnemies)
                effects.ApplyPowerFromSource(powerType, enemy, amount, owner, card);
            return;
        }

        var target = context.Mirror.CardPlay.Target;
        if (target is null)
            return;    // 源码语义：无目标 = 成功 no-op
        effects.ApplyPowerFromSource(powerType, target, amount, owner, card);
    }

    /// <summary>从分支状态读目标生物的 Power 层数（绝不读 live creature）。</summary>
    private static int GetBranchAmount<TPower>(OperationExecutionContext context, Creature creature)
        where TPower : PowerModel
        => (context.Mirror.CombatState as SimulatedCombatState
            ?? throw new InvalidOperationException("需要 SimulatedCombatState 分支状态。"))
            .GetAmount<TPower>(creature);
}
