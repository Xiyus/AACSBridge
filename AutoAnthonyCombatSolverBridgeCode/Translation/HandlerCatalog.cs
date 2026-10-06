namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// 0.1.0 翻译表：把支持的 (Opcode, Variant) 形状登记进注册表。
/// 支持矩阵与 AutoAnthony 组件目录（catalog_runtime_specs.json，931 条）逐形状核对过：
///  - deal_damage：selected/selected_enemy（131 条）与 all/all_enemies（32 条），fixed 值；
///  - gain_block / draw_cards / gain_energy：immediate/self，各 78/50/30 条，fixed 值；
///  - lose_hp：immediate/self（8 条）与 immediate/selected_enemy（2 条）；
///  - heal：immediate/self（1 条）。
/// 其余形状（X 值源、随机目标引用、事件目标、历史计数、阈值翻倍等）由镜像层的校验
/// fail-closed 拒绝，等 0.4.0+ 的对应里程碑。
/// </summary>
public static class HandlerCatalog
{
    public static void RegisterAll(OperationHandlerRegistry registry)
    {
        var damage = new Handlers.DamageHandler();
        registry.Register(new OperationKey("deal_damage", "selected"), damage);
        registry.Register(new OperationKey("deal_damage", "all"), damage);
        registry.Register(new OperationKey("gain_block", "immediate"), new Handlers.BlockHandler());
        registry.Register(new OperationKey("draw_cards", "immediate"), new Handlers.DrawHandler());
        registry.Register(new OperationKey("gain_energy", "immediate"), new Handlers.EnergyHandler());
        registry.Register(new OperationKey("lose_hp", "immediate"), new Handlers.LoseHpHandler());
        registry.Register(new OperationKey("heal", "immediate"), new Handlers.HealHandler());
    }
}
