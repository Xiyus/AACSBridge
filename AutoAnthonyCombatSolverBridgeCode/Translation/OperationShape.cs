using ChaosCardGenerator;

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// 一个待翻译操作的完整形状：操作索引（用于值解析/升级投影）、生成期 scope、当前生效的
/// RuntimeSpec（经 EffectiveRuntimeSpec 升级投影后的执行视角）。
/// </summary>
public readonly record struct OperationShape(
    int OperationIndex,
    OperationScope Scope,
    OperationRuntimeSpec Spec);
