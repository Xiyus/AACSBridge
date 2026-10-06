using AutoAnthonyCombatSolverBridge.Translation;
using MegaCrit.Sts2.Core.Entities.Cards;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// draw_and_discard(nonzero_cost) 的精确镜像（Scrape 式：抽 N 张，弃掉其中费用非 0 的）。
/// 复刻 ChaosOperationExecutor 的 D:DrawAndDiscardNonZero（L2271-2278）：
///   drawn = CardPileCmd.Draw(amount, owner)
///   → discard = drawn.Where(CostsX || GetWithModifiers(All) != 0)
///   → CardCmd.Discard(discard)
/// 镜像：simulator.Draw(owner, amount) 返回抽到的 PredictedCard，费用判断读 Preview 的
/// 分支状态（费用修饰是分支可变值），再 simulator.Discard(列表)。
/// </summary>
public sealed class DrawAndDiscardHandler : IOperationHandler
{
    public string Describe => "draw_and_discard(nonzero_cost)：抽后弃非零费精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target, spec.CardFilter) switch
        {
            ("nonzero_cost", "self", "cost_nonzero") => null,
            _ => $"draw_and_discard 的 (variant={spec.Variant}, target={spec.Target}, filter={spec.CardFilter}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var drawn = context.Mirror.Simulator.Draw(context.Card.Owner, context.ExecutableAmount);
        var discard = drawn
            .Where(candidate => candidate.Preview.EnergyCost.CostsX
                || candidate.Preview.EnergyCost.GetWithModifiers(CostModifiers.All) != 0)
            .ToList();
        if (discard.Count > 0)
            context.Mirror.Simulator.Discard(discard);
    }
}
