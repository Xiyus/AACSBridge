namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// RuntimeSpec 翻译 handler 的注册表，按 (Opcode, Variant) 键。只在 Mod 初始化阶段填充——
/// 与 CombatSolver 对它自己注册表施加的纪律相同。同一个 handler 实例可登记在多个键下
/// （如 deal_damage 的 selected/all 两个 variant 共用一个 DamageHandler）。
/// </summary>
public sealed class OperationHandlerRegistry
{
    public static OperationHandlerRegistry Instance { get; } = new();

    private readonly Dictionary<OperationKey, IOperationHandler> _handlers = new();

    private OperationHandlerRegistry() { }

    /// <summary>注册一个 handler 到指定键。重复键是编程错误，直接抛异常。</summary>
    public void Register(OperationKey key, IOperationHandler handler)
    {
        if (!_handlers.TryAdd(key, handler))
            throw new InvalidOperationException($"OperationHandler 重复注册：opcode='{key.Opcode}' variant='{key.Variant}'。");
    }

    public bool IsRegistered(OperationKey key) => _handlers.ContainsKey(key);

    public IOperationHandler? TryGet(OperationKey key) => _handlers.TryGetValue(key, out var handler) ? handler : null;

    public IReadOnlyCollection<OperationKey> Keys => _handlers.Keys;

    public int Count => _handlers.Count;
}
