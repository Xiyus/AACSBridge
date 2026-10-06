using AutoAnthonyCombatSolverBridge.Translation;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// gain_stars(immediate/self) + gain_max_hp(immediate/self) 的精确镜像。
/// gain_stars：PlayerCmd.GainStars → simulator.GainStars
/// gain_max_hp：CreatureCmd.GainMaxHp → simulator.GainMaxHp
/// </summary>
public sealed class GainStarsHandler : IOperationHandler
{
    public string Describe => "gain_stars/gain_max_hp(immediate)：simulator 精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Opcode, spec.Variant, spec.Target) switch
        {
            ("gain_stars", "immediate", "self") => null,
            ("gain_max_hp", "immediate", "self") => null,
            _ => $"{spec.Opcode} 的 (variant={spec.Variant}, target={spec.Target}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        if (context.Shape.Spec.Opcode == "gain_max_hp")
        {
            if (context.ExecutableAmount == 0) return;
            context.Mirror.Simulator.GainMaxHp(context.Card.Owner.Creature, context.ExecutableAmount);
            return;
        }
        context.Mirror.Simulator.GainStars(context.Card.Owner, context.ExecutableAmount);
    }
}