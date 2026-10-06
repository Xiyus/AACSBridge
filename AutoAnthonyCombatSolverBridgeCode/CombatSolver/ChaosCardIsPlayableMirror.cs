using AutoAnthony;
using CombatSolver.Engine.InCombat.Mirrors.Cards;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

/// <summary>
/// ChaosCardModel.IsPlayable 的镜像。复刻 ChaosCardModel 的重写（L462-464）：
/// base.IsPlayable &amp;&amp;（无"抽牌堆为空才可打"子句，或抽牌堆确实为空）。
/// base.IsPlayable 恒为 true（CardModel 基类默认，CombatSolver 镜像注释确认），能量/目标/
/// Unplayable 检查在游戏出牌管线里，不在这个 getter——所以镜像只需复刻这一个子句，
/// 读的是模拟分支的抽牌堆（与 GrandFinale 镜像同款写法）。
/// </summary>
internal static class ChaosCardIsPlayableMirror
{
    public static bool Evaluate(ChaosCardModel card, CardIsPlayableMirrorContext context)
        => !card.Generated.Operations.Any(operation => operation.Template == "C:playableIfDrawPileEmpty")
           || context.State.GetPlayerCombatState(card.Owner).DrawPile.IsEmpty;
}
