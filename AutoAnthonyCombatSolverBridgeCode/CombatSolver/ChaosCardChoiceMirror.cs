using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

// 命名空间说明见 AutoAnthonyFacade.cs：文件级 using 从全局解析，外部 CombatSolver 命名空间
// （CardChoiceSpec/PlanChoiceEffect/PlanCardChoice）不受本文件所在子命名空间遮蔽。

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

/// <summary>
/// 混沌卡的玩家选牌登记（0.5.0）：把"选一张手牌消耗/弃置/从弃牌堆取回"类操作映射成
/// CombatSolver 的原生 CardChoiceSpec——出牌路径在 OwnChoice 阶段调
/// CardChoiceSupport.GetSpec（第三方登记优先）拿到本 spec，按候选展开搜索分支，
/// 选中的结果记进计划，部署时照常应答原生选牌页面；效果由求解器的原生
/// PlanChoiceEffect（Exhaust/Discard/MoveToHand/MoveToDrawTop）施加——与
/// AutoAnthony 执行器的 CardCmd.Exhaust/Discard/CardPileCmd.Add 语义一致。
///
/// 时序说明：求解器的模型是"OnPlay 镜像先跑全部非选牌效果 → OwnChoice 阶段解析选择"，
/// 即选择在卡的结算末尾发生；真实执行器是逐操作顺序（选择可能在中途）。对"选牌在
/// 最后一个操作"的卡两者一致；选牌在前的卡存在顺序近似（终态等价，中途触发顺序
/// 可能不同）——严格 diff 会暴露问题。
/// </summary>
internal static class ChaosCardChoiceMirror
{
    /// <summary>
    /// 构造选牌 spec；卡上没有受支持的选择型操作时返回 null（= 这张牌没有选择）。
    /// 0.5.0 只支持卡上恰好一个选择型操作（校验层拒绝多选卡）。
    /// </summary>
    public static CardChoiceSpec? BuildSpec(CombatPredictionSimulator simulator, PredictedCard playedCard, ChaosCardModel card)
    {
        var operations = card.Generated.Operations;
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            OperationRuntimeSpec spec;
            try
            {
                spec = ChaosOperationExecutor.EffectiveRuntimeSpec(card, index);
            }
            catch
            {
                continue;    // 校验层已保证可解析；这里防御性跳过
            }

            if (!IsSupportedSelection(spec))
                continue;

            var owner = simulator.State.GetPlayerCombatState(card.Owner);
            var amount = card.OperationAmount(index);
            if (card.Type != CardType.Power)
                amount += card.CapturedExternalDamageBonus(index);

            switch ((spec.Opcode, spec.Variant, spec.SourceZone, spec.DestinationZone))
            {
                case ("exhaust_card", "selected", "hand", "none"):
                {
                    // 源码 Exhaust 的直选分支：count = HandExhaustSelectionCount(amount, hand.Count)
                    var hand = owner.Hand.Cards;
                    var count = ChaosOperationExecutor.HandExhaustSelectionCount(amount, hand.Count);
                    return NativeSpec(PlanChoiceEffect.Exhaust, PileType.Hand, count, hand);
                }
                case ("discard_card", "selected", "hand", "none"):
                {
                    // 源码 Discard：count >= hand.Count 时整手弃置（无选择），否则选 count 张
                    var hand = owner.Hand.Cards;
                    return NativeSpec(PlanChoiceEffect.Discard, PileType.Hand, amount, hand);
                }
                case ("move_card", "selected", "discard", "hand"):
                {
                    // 源码 MoveSelectedDiscardCardsToHand：count = min(requested, 弃牌数, 手牌空位)
                    var discard = owner.DiscardPile.Cards;
                    var requested = Math.Max(1,
                        ChaosOperationExecutor.RuntimeSpecValue(card, index, "count", amount));
                    var count = Math.Min(Math.Min(requested, discard.Count),
                        Math.Max(0, simulator.GetMaxHandSize(card.Owner) - owner.Hand.Cards.Count));
                    return NativeSpec(PlanChoiceEffect.MoveToHand, PileType.Discard, count, discard);
                }
                case ("move_card", "selected", "discard", "draw"):
                {
                    // 源码 MoveCard 的直选分支：从弃牌堆选 1 张放到抽牌堆顶
                    var discard = owner.DiscardPile.Cards;
                    return NativeSpec(PlanChoiceEffect.MoveToDrawTop, PileType.Discard, 1, discard);
                }
                case ("template_independent_action", "cl_moveselectedattackdrawtohand", "none", "none"):
                case ("template_independent_action", "cl_moveselectedskilldrawtohand", "none", "none"):
                {
                    // 源码 L1388-1396：从抽牌堆选 1 张指定类型牌入手
                    // （原生 SecretWeapon/SecretTechnique 同款形状）
                    var type = spec.Variant == "cl_moveselectedattackdrawtohand"
                        ? CardType.Attack
                        : CardType.Skill;
                    var drawAttacks = owner.DrawPile.Cards
                        .Where(candidate => candidate.Preview.Type == type)
                        .ToList();
                    return NativeSpec(PlanChoiceEffect.MoveToHand, PileType.Draw, 1, drawAttacks);
                }
                case ("template_self_action", "r_movediscardcardtodrawtop", "none", "none"):
                {
                    // 源码 L1514-1520：从弃牌堆选 1 张放到抽牌堆顶
                    var discard = owner.DiscardPile.Cards;
                    return NativeSpec(PlanChoiceEffect.MoveToDrawTop, PileType.Discard, 1, discard);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// ModDefined 结算回调——本登记全部使用原生 Effect（求解器自己施加），不会走到这里；
    /// 防御性抛错以免静默空操作。
    /// </summary>
    public static bool Apply(CombatPredictionSimulator simulator, SimulatedCombatState combat,
        PredictedCard playedCard, ChaosCardModel card, PlanCardChoice choice)
        => throw new InvalidOperationException(
            $"混沌卡 {card.Id} 的选择使用了原生 Effect，不应进入 ModDefined 结算。");

    /// <summary>受支持的选择型操作形状（与 ChaosCardOnPlayMirror 的判定保持一致）。</summary>
    internal static bool IsSupportedSelection(OperationRuntimeSpec spec)
        => (spec.Opcode, spec.Variant, spec.SourceZone, spec.DestinationZone) switch
        {
            ("exhaust_card", "selected", "hand", "none") => true,
            ("discard_card", "selected", "hand", "none") => true,
            ("move_card", "selected", "discard", "hand") => true,
            ("move_card", "selected", "discard", "draw") => true,
            ("template_independent_action", "cl_moveselectedattackdrawtohand", "none", "none") => true,
            ("template_independent_action", "cl_moveselectedskilldrawtohand", "none", "none") => true,
            ("template_self_action", "r_movediscardcardtodrawtop", "none", "none") => true,
            _ => false,
        };

    /// <summary>
    /// 构造原生选牌 spec（与 CardChoiceSupport.Spec 同款语义：空候选或 count&lt;=0 返回 null；
    /// 候选数 ≤ count 时标记隐式全选）。
    /// </summary>
    private static CardChoiceSpec? NativeSpec(PlanChoiceEffect effect, PileType source, int count,
        IReadOnlyList<PredictedCard> options)
    {
        if (options.Count == 0 || count <= 0)
            return null;
        return new CardChoiceSpec(effect, source, count, count, options, options,
            ReplacementValue: 0d,
            IsImplicitAllSelection: options.Count <= count);
    }
}
