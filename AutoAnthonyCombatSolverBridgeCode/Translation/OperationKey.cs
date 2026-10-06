using ChaosCardGenerator;

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// 一个 RuntimeSpec 形状的路由键：Opcode + Variant——与 AutoAnthony 的 ChaosOperationExecutor
/// 分派结构化执行用的是同一对。spec 不带 variant 时 Variant 为 ""。
/// </summary>
public readonly record struct OperationKey(string Opcode, string Variant)
{
    public static OperationKey FromSpec(OperationRuntimeSpec spec)
        => new(spec.Opcode, string.IsNullOrEmpty(spec.Variant) ? "" : spec.Variant);
}
