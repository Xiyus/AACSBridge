using ChaosCardGenerator;

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// 一个可翻译的 RuntimeSpec 形状。0.1.0 起 handler 承载执行：把结构化 spec 精确翻译成
/// CombatSolver 模拟分支上的操作（只写模拟状态，绝不触碰真实战斗）。
/// <see cref="ValidateSupport"/> 是 fail-closed 的第一道闸：返回原因字符串表示该形状无法
/// 精确翻译，镜像层据此中止整场搜索（部分正确的预测比没有预测更糟）。
/// </summary>
public interface IOperationHandler
{
    /// <summary>供日志与目录审计使用的人类可读描述。</summary>
    string Describe { get; }

    /// <summary>
    /// 校验本 handler 能否精确翻译该形状；不能则返回原因（用于异常消息），能则返回 null。
    /// 只读校验，不产生任何副作用。
    /// </summary>
    string? ValidateSupport(OperationShape shape);

    /// <summary>
    /// 执行翻译。只在 <see cref="ValidateSupport"/> 通过之后调用；只写模拟分支状态。
    /// </summary>
    void Execute(OperationExecutionContext context);
}
