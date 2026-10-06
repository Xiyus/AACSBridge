using AutoAnthonyCombatSolverBridge.Translation;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// gain_stars(immediate/self) 的精确镜像。
/// 复刻 ChaosOperationExecutor.TryExecuteStructuredCommon L862-864：
/// PlayerCmd.GainStars(amount, owner) → simulator.GainStars(owner, amount)。
/// </summary>
public sealed class GainStarsHandler : IOperationHandler
{
    public string Describe => "gain_stars(immediate)：simulator.GainStars 精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target) switch
        {
            ("immediate", "self") => null,
            _ => $"gain_stars 的 (variant={spec.Variant}, target={spec.Target}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        context.Mirror.Simulator.GainStars(context.Card.Owner, context.ExecutableAmount);
    }
}