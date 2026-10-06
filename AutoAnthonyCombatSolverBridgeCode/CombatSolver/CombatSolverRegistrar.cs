using System.Reflection;
using AutoAnthony;
using CombatSolver.Engine.InCombat.Mirrors.Cards;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using MegaCrit.Sts2.Core.Models;
using AutoAnthonyCombatSolverBridge.Bootstrap;
using AutoAnthonyCombatSolverBridge.Diagnostics;
using AutoAnthonyCombatSolverBridge.Translation;

// 命名空间说明见 AutoAnthonyFacade.cs：本文件位于 AutoAnthonyCombatSolverBridge.CombatSolver 之下，
// 桥根命名空间内部裸标识符 "CombatSolver" 会绑定到我们自己的子命名空间。外部 CombatSolver 类型
// 一律通过文件级 using + 非限定名引用（如下所示），本代码库内永远不要写限定名 CombatSolver.Xxx。

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

/// <summary>
/// 负责向 CombatSolver 的适配注册表写入的所有操作。0.1.0 起正式登记：
///  - 翻译表（HandlerCatalog → OperationHandlerRegistry）；
///  - CardOnPlayMirrors / CardIsPlayableMirrors 的 ChaosCardModel 镜像。
///
/// 关键事实（源码核实）：镜像派发按**精确运行时类型**匹配——receiver 是保持运行时类型的
/// MutablePreview 克隆（如 ChaosCard042），只登记抽象基类 ChaosCardModel 永远匹配不到。
/// 因此必须反射枚举全部具体卡类（AutoAnthony 六个家族 500+ 个 sealed 类，加上其他已加载
/// 程序集里的 ExternalChaosCardModel 子类）逐个登记。
///
/// 时机红线：全部登记在 Mod 初始化期完成、早于第一次战斗根捕获——普通注册表按精确类型
/// 缓存查询结果，某类型一旦被查过，迟到登记静默无效。
/// </summary>
public static class CombatSolverRegistrar
{
    public static void Initialize(CompatibilityReport report)
    {
        // 1. 翻译表
        HandlerCatalog.RegisterAll(OperationHandlerRegistry.Instance);
        BridgeLog.Info($"翻译表就绪：{OperationHandlerRegistry.Instance.Count} 个 (Opcode, Variant) 形状。");

        // 2. 镜像登记（逐具体类型）
        var registered = 0;
        var failures = new List<string>();
        foreach (var type in EnumerateConcreteChaosCardTypes())
        {
            try
            {
                RegisterMirrorsForType(type);
                registered++;
            }
            catch (Exception exception)
            {
                // 单个类型登记失败（如重复登记/校验异常）不吞掉：记录后继续，最后汇总报告。
                failures.Add($"{type.FullName}: {exception.Message}");
            }
        }

        BridgeLog.Info($"已登记 {registered} 个具体 Chaos 卡类型的 OnPlay/IsPlayable 镜像。");
        foreach (var failure in failures)
            BridgeLog.Warn($"镜像登记失败：{failure}");
        if (failures.Count > 0)
            BridgeLog.Warn("存在登记失败的类型：这些卡在预测中会按未适配处理（fail-closed）。");

        var cs = report.CombatSolver;
        BridgeLog.Info($"CombatSolver 适配面核验：缺失类型 {cs.MissingTypes.Count} 个、缺失成员 {cs.MissingMembers.Count} 个。");
        BridgeLog.Info("0.4.0 生效范围：即时牌 + apply_power(20 variant) + Power 卡伤害 + strength_scaled 修饰符 + " +
                       "牌堆移动 + X 费卡（OnPlay X 解析复刻：Hook.ModifyXValue 分支状态 + SetResolvedXValues）+ " +
                       "随机目标（显式抽取消耗分支 CombatTargets 流，与真实 RNG 消耗一致）。" +
                       "矩阵外的卡（选牌、触发器等）保守排除（模拟中不可打），绝不给出错误预测。");
    }

    // --- 具体卡类型枚举 -------------------------------------------------------------------------

    /// <summary>
    /// 枚举全部具体（非抽象）ChaosCardModel 子类：AutoAnthony 本体的六个家族 + 其他已加载
    /// 程序集里的 ExternalChaosCardModel 子类（外部角色 Mod）。晚于本桥加载的外部角色 Mod
    /// 无法覆盖（其卡会按未适配 fail-closed）——这类 Mod 需声明对本桥的依赖才能保证顺序。
    /// </summary>
    private static IEnumerable<Type> EnumerateConcreteChaosCardTypes()
    {
        var chaosType = typeof(ChaosCardModel);
        var bridgeAssembly = typeof(MainFile).Assembly;
        var reported = new HashSet<Assembly>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == bridgeAssembly || !reported.Add(assembly))
                continue;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type is not null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || type == chaosType || !chaosType.IsAssignableFrom(type))
                    continue;
                yield return type;
            }
        }
    }

    // --- 泛型登记桥 -------------------------------------------------------------------------------

    /// <summary>
    /// 泛型登记入口：约束到 ChaosCardModel 使 handler 的方法组转换（参数逆变）成立。
    /// 经 <see cref="RegisterMirrorsForType"/> 反射调用以覆盖全部具体类型。
    /// </summary>
    private static void RegisterMirrorsFor<TCard>() where TCard : ChaosCardModel
    {
        CardOnPlayMirrors.Registry.Register<TCard>(ChaosCardOnPlayMirror.Execute);
        CardIsPlayableMirrors.Registry.Register<TCard>(ChaosCardIsPlayableMirror.Evaluate);
    }

    // 注意：反射查找必须按 IsGenericMethodDefinition 消歧——本类同时存在泛型与非泛型的同名
    // 重载，按名字直接 GetMethod 会抛 AmbiguousMatchException（0.1.0 首次实机测试的实际教训）。
    private static readonly MethodInfo RegisterMirrorsForMethod = typeof(CombatSolverRegistrar)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .Single(method => method.Name == nameof(RegisterMirrorsFor) && method.IsGenericMethodDefinition);

    private static void RegisterMirrorsForType(Type type)
        => RegisterMirrorsForMethod.MakeGenericMethod(type).Invoke(null, null);
}
