using AutoAnthonyCombatSolverBridge.Translation;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// heal(immediate/self) 的精确镜像。
/// 复刻 ChaosOperationExecutor.TryExecuteStructuredCommon L879-881：
/// CreatureCmd.Heal(owner, amount) → simulator.Heal(owner, amount)。
/// </summary>
public sealed class HealHandler : IOperationHandler
{
    public string Describe => "heal(immediate)：simulator.Heal 精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target) switch
        {
            ("immediate", "self") => null,
            _ => $"heal 的 (variant={spec.Variant}, target={spec.Target}) 组合不在 0.1.0 支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        context.Mirror.Simulator.Heal(context.Card.Owner.Creature, context.ExecutableAmount);
    }
}
