namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// 某个 RuntimeSpec 形状没有已注册 handler 时抛出。这是 fail-closed 契约：未知操作必须中止
/// 受影响的预测，绝不静默变成空操作——否则 CombatSolver 会搜索出错误预测真实战斗的路线。
/// </summary>
public sealed class UnsupportedRuntimeSpecException : Exception
{
    public string Opcode { get; }
    public string Variant { get; }

    public UnsupportedRuntimeSpecException(string opcode, string variant)
        : base($"opcode='{opcode}' variant='{variant}' 没有已注册的 OperationHandler。")
    {
        Opcode = opcode;
        Variant = variant;
    }
}
