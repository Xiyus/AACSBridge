namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// RuntimeSpec 翻译 handler 的注册表，按 (Opcode, Variant) 键。只在 Mod 初始化阶段填充——
/// 与 CombatSolver 对它自己注册表施加的纪律相同。0.0.4 刻意保持为空：没有 handler，就不注册
/// （fail closed）。
/// </summary>
public sealed class OperationHandlerRegistry
{
    public static OperationHandlerRegistry Instance { get; } = new();

    private readonly Dictionary<OperationKey, IOperationHandler> _handlers = new();

    private OperationHandlerRegistry() { }

    /// <summary>注册一个 handler。重复键是编程错误，直接抛异常。</summary>
    public void Register(IOperationHandler handler)
    {
        if (!_handlers.TryAdd(handler.Key, handler))
            throw new InvalidOperationException($"OperationHandler 重复注册：opcode='{handler.Key.Opcode}' variant='{handler.Key.Variant}'。");
    }

    public bool IsRegistered(OperationKey key) => _handlers.ContainsKey(key);

    public IOperationHandler? TryGet(OperationKey key) => _handlers.TryGetValue(key, out var handler) ? handler : null;

    public IReadOnlyCollection<OperationKey> Keys => _handlers.Keys;

    public int Count => _handlers.Count;
}
