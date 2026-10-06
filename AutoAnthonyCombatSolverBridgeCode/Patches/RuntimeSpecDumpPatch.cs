using AutoAnthony;
using MegaCrit.Sts2.Core.Models;
using HarmonyLib;
using AutoAnthonyCombatSolverBridge.AutoAnthony;
using AutoAnthonyCombatSolverBridge.Diagnostics;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Patches;

/// <summary>
/// 里程碑 ③ + ④：证明桥能在真实战斗中识别 AutoAnthony 生成牌，并打印每个操作的结构化
/// OperationRuntimeSpec（绝不打印本地化文本）。设计上只读：本 postfix 观察
/// AfterCardEnteredCombat，绝不修改游戏状态，因此不可能造成任何失步。
///
/// 为什么选这个钩子：卡牌进入战斗牌堆时，游戏会把 AfterCardEnteredCombat(card) 广播给战斗中的
/// 卡牌，因此战斗开始时每张牌组卡（包括每张生成牌）都会以 card == 接收者的形态经过这里恰好一次。
/// AutoAnthony 自己也重写这个方法（并跳过克隆），说明这是一个稳定、经过实战检验的接缝。
/// </summary>
[HarmonyPatch(typeof(ChaosCardModel), nameof(ChaosCardModel.AfterCardEnteredCombat))]
public static class RuntimeSpecDumpPatch
{
    // CardModel 实例存活整局；每个实例每次会话转储一次足以验证管线，也让日志保持可读。
    private static readonly HashSet<CardModel> Dumped = [];

    public static void Postfix(ChaosCardModel __instance, CardModel card)
    {
        try
        {
            if (!ReferenceEquals(card, __instance) || __instance.IsClone)
                return;
            if (!Dumped.Add(__instance))
                return;
            Dump(__instance);
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
