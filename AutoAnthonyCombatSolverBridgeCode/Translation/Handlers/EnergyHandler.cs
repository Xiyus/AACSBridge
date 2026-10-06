using AutoAnthonyCombatSolverBridge.Translation;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// gain_energy(immediate/self) 的精确镜像。
/// 复刻 ChaosOperationExecutor.TryExecuteStructuredCommon L855-857：
/// PlayerCmd.GainEnergy(amount, owner) → simulator.GainEnergy(owner, amount)。
/// 注：AutoAnthony 没有 lose_energy opcode（能量代价经 X 费/modify_cost 表达），无需实现。
/// </summary>
public sealed class EnergyHandler : IOperationHandler
{
    public string Describe => "gain_energy(immediate)：simulator.GainEnergy 精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target) switch
        {
            ("immediate", "self") => null,
            _ => $"gain_energy 的 (variant={spec.Variant}, target={spec.Target}) 组合不在 0.1.0 支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        context.Mirror.Simulator.GainEnergy(context.Card.Owner, context.ExecutableAmount);
    }
}
