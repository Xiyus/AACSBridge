using AutoAnthonyCombatSolverBridge.Translation;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// draw_cards(immediate/self) 的精确镜像。
/// 复刻 ChaosOperationExecutor.TryExecuteStructuredCommon L834-837：
/// CardPileCmd.Draw(choiceContext, amount, owner) → simulator.Draw(owner, amount)。
/// 返回值记录到本次出牌的局部结算状态，供后续条件读取。
/// </summary>
public sealed class DrawHandler : IOperationHandler
{
    public string Describe => "draw_cards(immediate)：simulator.Draw 精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target) switch
        {
            ("immediate", "self") => null,
            _ => $"draw_cards 的 (variant={spec.Variant}, target={spec.Target}) 组合不在 0.1.0 支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var drawn = context.Mirror.Simulator.Draw(context.Card.Owner, context.ExecutableAmount);
        context.Resolution?.RecordDrawnTypes(drawn.Select(card => card.Preview.Type));
    }
}
