using System.Reflection;
using AutoAnthony;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using HarmonyLib;
using AutoAnthonyCombatSolverBridge.AutoAnthony;
using AutoAnthonyCombatSolverBridge.Diagnostics;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Patches;

/// <summary>
/// 里程碑 ③ + ④：证明桥能在真实战斗中识别 AutoAnthony 生成牌，并打印每个操作的结构化
/// OperationRuntimeSpec（绝不打印本地化文本）。设计上只读：本 postfix 观察
/// CombatState.AddCard，绝不修改游戏状态，因此不可能造成任何失步。
///
/// 为什么选这个钩子（实机验证的结论）：CombatState.AddCard 是每张进入战斗的卡必经的注册入口
/// ——包括开局时牌组克隆进战斗（CloneCard → AddCard）和战斗中生成的卡。最初尝试的
/// ChaosCardModel.AfterCardEnteredCombat 只在"战斗中从战斗外进入牌堆"时触发（如战斗中生成牌），
/// 开局牌组建战斗不经过它——实机日志证实该路径零触发。AutoAnthony 的伴侣 Mod CardTinkering
/// 也同时补丁这两个接缝，AddCard 是覆盖面完整的那一个。
/// </summary>
[HarmonyPatch]
public static class RuntimeSpecDumpPatch
{
    /// <summary>
    /// 目标：CombatState 上所有第一个参数为 CardModel 的 AddCard 重载
    /// （private AddCard(CardModel) 与 public AddCard(CardModel, Player)）。
    /// </summary>
    private static IEnumerable<MethodBase> TargetMethods() => typeof(CombatState)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(method => method.Name == "AddCard"
                         && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(CardModel));

    // 每场战斗的牌组都是新的可变克隆实例（引用不同），因此每个战斗实例转储一次；
    // 真正的复制牌（CloneOf != null，如 Shiv 类）跳过——它们的定义与源牌相同。
    private static readonly HashSet<CardModel> Dumped = [];

    public static void Postfix(CardModel card)
    {
        try
        {
            if (card is not ChaosCardModel chaos || chaos.IsClone)
                return;
            if (!Dumped.Add(chaos))
                return;
            Dump(chaos);
        }
        catch (Exception exception)
        {
            BridgeLog.Warn($"{card.Id} 的 RuntimeSpec 转储失败：{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void Dump(ChaosCardModel chaos)
    {
        if (!ChaosCardResolver.TryResolve(chaos, out var resolved))
        {
            BridgeLog.Warn($"检测到 CHAOS_CARD id={chaos.Id}，但其定义在当前状态下无法解析——跳过 spec 转储（绝不猜测）。");
            return;
        }

        BridgeLog.Dump(RuntimeSpecReader.DescribeCard(resolved));
        for (var index = 0; index < resolved.OperationCount; index++)
        {
            BridgeLog.Dump(RuntimeSpecReader.DescribeOperation(index, resolved.Operations[index]));
            if (resolved.RuntimeSpecs[index] is { } spec)
                BridgeLog.Dump(RuntimeSpecReader.DescribeSpec(spec));
        }

        if (resolved.PersistedSpecs is { Count: var persisted } && persisted != resolved.OperationCount)
            BridgeLog.Warn($"{chaos.Id} 的持久化 spec 数 {persisted} 与操作数 {resolved.OperationCount} 不一致——0.1.0 依赖它之前需要先核查快照一致性。");
    }
}
