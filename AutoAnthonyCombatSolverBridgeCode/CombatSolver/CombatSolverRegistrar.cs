using AutoAnthonyCombatSolverBridge.Bootstrap;
using AutoAnthonyCombatSolverBridge.Diagnostics;

// 命名空间说明见 AutoAnthonyFacade.cs：本文件位于 AutoAnthonyCombatSolverBridge.CombatSolver 之下，
// 桥根命名空间内部裸标识符 "CombatSolver" 会绑定到我们自己的子命名空间。外部 CombatSolver 类型
// 一律通过文件级 using + 非限定名引用——而 0.0.4 刻意一个都不引用（见下）。

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

/// <summary>
/// 负责向 CombatSolver 的适配注册表写入的所有操作。0.0.4 刻意零注册，原因：
///
///  - CombatSolver 第三方适配规则明确：部分注册的镜像集比不注册更危险（缺失的部分会在预测里
///    静默变成空操作），所以桥只有在翻译层能覆盖其声明的操作目录之后，才从"观察"切到"注册"。
///  - 所有注册必须在 Mod 初始化阶段完成、早于第一次战斗根捕获：
///    AdaptedCardOnPlayMirrors/ModelPredictionStateMirrors/回合阶段表在那里冻结（迟到注册抛异常），
///    普通 MethodMirrorRegistry 表会缓存查找结果（迟到注册静默无效）。
///  - 桥的编译期引用已 publicize CombatSolver 的内部注册表（csproj 里的 Krafs.Publicizer），
///    0.1.0 可以直接调用；守卫已核验本类将要用到的每个类型/成员。
///
/// 0.1.0 计划（全部在 Mod 初始化期完成，逐 handler fail-closed）：
///    CardOnPlayMirrors.Registry.Register<ChaosCardModel>(...)        —— 通用 OnPlay 解释器
///    CardIsPlayableMirrors.Registry.Register<ChaosCardModel>(...)     —— 真实可打出条件
///    PowerDynamicVarWarmup.RegisterAdaptedCanonicalPower(...)         —— 随 ChaosCompositePower 落地（0.6.0）
///    KnownPreRootSubscriberTypeNames                                  —— 订阅者完整适配之后才动
/// </summary>
public static class CombatSolverRegistrar
{
    public static void Initialize(CompatibilityReport report)
    {
        var cs = report.CombatSolver;
        BridgeLog.Info($"CombatSolver 适配面核验完成：缺失类型 {cs.MissingTypes.Count} 个、缺失成员 {cs.MissingMembers.Count} 个（任一缺失守卫都会直接禁用桥）。");
        BridgeLog.Info("注册推迟（里程碑 0.1.0）：本构建不注册任何镜像。CombatSolver 继续把 AutoAnthony 内容视为未适配——桥只观察和转储，不改变任何预测。");
    }
}
