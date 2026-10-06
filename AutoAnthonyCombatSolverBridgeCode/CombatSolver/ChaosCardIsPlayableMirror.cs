using AutoAnthony;
using CombatSolver.Engine.InCombat.Mirrors.Cards;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

/// <summary>
/// ChaosCardModel.IsPlayable 的镜像。两层语义：
///  1. 保守可打性（默认开启，AA_BRIDGE_STRICT=1 关闭）：超出支持矩阵的卡在模拟中返回
///     false——搜索只探索可精确预测的路线，矩阵外的卡不被求解器主动打出（真实游戏不受
///     影响）。这是"宁可次优、不可错误预测"的保守实现：部署的每个动作都被精确预测。
///  2. 真实合法性（复刻 ChaosCardModel 的重写 L462-464）：base.IsPlayable &amp;&amp;
///     （无"抽牌堆为空才可打"子句，或抽牌堆确实为空）。base 恒为 true（CardModel 基类
///     默认，CombatSolver 镜像注释确认），能量/目标/Unplayable 检查在游戏出牌管线里——
///     所以镜像只需复刻这一个子句，读的是模拟分支的抽牌堆（与 GrandFinale 镜像同款写法）。
/// </summary>
internal static class ChaosCardIsPlayableMirror
{
    public static bool Evaluate(ChaosCardModel card, CardIsPlayableMirrorContext context)
    {
        if (ChaosCardSupport.ConservativePlayability
            && ChaosCardSupport.GetUnsupportedReason(card, context.Card.Original) is not null)
            return false;    // 保守模式：矩阵外卡在模拟中不可打（真实游戏不受影响）

        return !card.Generated.Operations.Any(operation => operation.Template == "C:playableIfDrawPileEmpty")
               || context.State.GetPlayerCombatState(card.Owner).DrawPile.IsEmpty;
    }
}
