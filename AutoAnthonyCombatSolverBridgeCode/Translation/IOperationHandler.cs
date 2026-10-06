using ChaosCardGenerator;

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// 一个可翻译的 RuntimeSpec 形状。里程碑 0.1.0 会把它变成执行接缝：handler 将收到解析后的
/// spec 与 CombatSolver 镜像上下文，且只修改*模拟*分支状态（绝不触碰真实战斗）。在那之前，
/// 注册表先行存在，是为了在注册任何镜像之前就能对路由面做审计、记录和与 AutoAnthony 组件目录
/// （ChaosCardGenerator/Data/catalog_runtime_specs.json）的比对。
/// </summary>
public interface IOperationHandler
{
    /// <summary>路由键：本 handler 翻译的 (Opcode, Variant)。</summary>
    OperationKey Key { get; }

    /// <summary>供日志与目录审计使用的人类可读描述。</summary>
    string Describe { get; }
}
