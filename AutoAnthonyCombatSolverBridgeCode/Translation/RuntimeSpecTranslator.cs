using ChaosCardGenerator;

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// 把一个 OperationRuntimeSpec 映射到它的翻译 handler。整个桥的 fail-closed 接缝：
/// 没有 handler 的 spec 抛 <see cref="UnsupportedRuntimeSpecException"/>，而不是被当作空操作
/// ——因为部分正确的预测比没有预测更糟。
///
/// 状态：尚未接入 CombatSolver。0.0.4 没有任何运行时调用方；0.1.0 的 CardOnPlay 镜像将在
/// 模拟分支内调用它，异常将以预测缺口/中止搜索的形式浮出，而不是给出错误路线。
/// </summary>
public static class RuntimeSpecTranslator
{
    public static IOperationHandler Translate(OperationRuntimeSpec spec)
    {
        var key = OperationKey.FromSpec(spec);
        return OperationHandlerRegistry.Instance.TryGet(key)
            ?? throw new UnsupportedRuntimeSpecException(key.Opcode, key.Variant);
    }

    public static bool CanTranslate(OperationRuntimeSpec spec)
        => OperationHandlerRegistry.Instance.IsRegistered(OperationKey.FromSpec(spec));
}
