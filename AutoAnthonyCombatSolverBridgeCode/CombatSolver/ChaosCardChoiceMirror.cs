using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Models.CardPools;
using global::CombatSolver.Engine.InCombat.Extensions;

// 命名空间说明见 AutoAnthonyFacade.cs：文件级 using 从全局解析，外部 CombatSolver 命名空间
// （CardChoiceSpec/PlanChoiceEffect/PlanCardChoice）不受本文件所在子命名空间遮蔽。

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

/// <summary>
/// 每个选择在其原始操作位置构造原生 CardChoiceSpec，并由求解器展开候选与记录计划。
/// OwnChoice 保持为空，避免尾部再次选择。卡槽选择先记录引用，再执行对应消费者；
/// 复制、变形和重放使用桥的精确结算，其他选择复用原生 PlanChoiceEffect。
/// </summary>
internal static class ChaosCardChoiceMirror
{
    /// <summary>
    /// 构造选牌 spec；卡上没有受支持的选择型操作时返回 null（= 这张牌没有选择）。
    /// 0.5.0 只支持卡上恰好一个选择型操作（校验层拒绝多选卡）。
    /// </summary>
    public static CardChoiceSpec? BuildSpec(CombatPredictionSimulator simulator, PredictedCard playedCard, ChaosCardModel card)
        => null; // Choices now execute in operation order inside OnPlay.

    internal static CardChoiceSpec? BuildOperationSpec(CombatPredictionSimulator simulator, PredictedCard playedCard, ChaosCardModel card, int operationIndex, int? amountOverride = null)
    {
        var operations = card.Generated.Operations;
        for (var index = 0; index < operations.Count; index++)
        {
            if (index != operationIndex) continue;
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
            if (amountOverride is { } resolvedAmount) amount = resolvedAmount;

            switch ((spec.Opcode, spec.Variant, spec.SourceZone, spec.DestinationZone))
            {
                case ("template_independent_action", "i_upgrade", "none", "none"):
                {
                    var options = owner.Hand.Cards.Where(candidate => candidate.Preview.IsUpgradable).ToList();
                    var all = card.IsUpgraded && card.Generated.Upgrade?.Effects.Any(effect =>
                        effect.Kind == CardUpgradeKind.SelectAllCards && effect.OperationIndex == index) == true;
                    return NativeSpec(PlanChoiceEffect.Upgrade, PileType.Hand, all ? options.Count : 1, options);
                }
                case ("template_independent_action", "cl_exhaustuptohandcards", "none", "none"):
                {
                    var options = owner.Hand.Cards;
                    var maximum = Math.Min(Math.Max(0, amount), options.Count);
                    return maximum == 0 ? null : new CardChoiceSpec(PlanChoiceEffect.Exhaust, PileType.Hand, 0, maximum,
                        options, options, ReplacementValue: 0d);
                }
                case ("template_independent_action", "i_grantslytohandskillthisturn", "none", "none"):
                    return NativeSpec(PlanChoiceEffect.ApplySly, PileType.Hand, 1,
                        owner.Hand.Cards.Where(candidate => candidate.Preview.Type == CardType.Skill && !candidate.Preview.IsSlyThisTurn).ToList());
                case ("template_self_action", "ncr_addvoidtoselectedhandcard", "none", "none"):
                    return NativeSpec(PlanChoiceEffect.ApplyEthereal, PileType.Hand, 1,
                        owner.Hand.Cards.Where(candidate => !candidate.Preview.Keywords.Contains(CardKeyword.Ethereal)).ToList());
                case ("template_self_action", "ncr_addretaintoselectedhandcard", "none", "none"):
                    return NativeSpec(PlanChoiceEffect.ApplyRetain, PileType.Hand, 1,
                        owner.Hand.Cards.Where(candidate => !candidate.Preview.Keywords.Contains(CardKeyword.Retain)).ToList());
                case ("template_independent_action", "cl_putselectedhandcardondrawtop", "none", "none"):
                    return NativeSpec(PlanChoiceEffect.MoveToDrawTop, PileType.Hand, 1, owner.Hand.Cards);
                case ("template_independent_action", "i_copyselectedcardnextturn", "none", "none"):
                    return NativeSpec(PlanChoiceEffect.Nightmare, PileType.Hand, 1, owner.Hand.Cards);
                case ("template_independent_action", "i_proxyatomic_transfigure", "none", "none"):
                    return NativeSpec(PlanChoiceEffect.Modify, PileType.Hand, 1, owner.Hand.Cards);
                case ("template_self_action", "r_playselectedskillmultipletimes", "none", "none"):
                    return ChaosOperationExecutor.ExecutableOperationCount(operation, amount) == 0 ? null : NativeSpec(
                        PlanChoiceEffect.AutoPlayRepeated, PileType.Hand, 1, owner.Hand.Cards.Where(candidate =>
                            candidate.Preview.Type == CardType.Skill && !candidate.Preview.Keywords.Contains(CardKeyword.Unplayable)).ToList());
                case ("exhaust_card", "selected", "hand", "none"):
                case ("exhaust_card", "referenced", "none", "none"):
                case ("exhaust_card", "referenced", "hand", "none"):
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
                case ("move_card", "selected", "draw", "hand"):
                {
                    // 源码：从抽牌堆选 1 张入手（无类型过滤）
                    var draw = owner.DrawPile.Cards;
                    return NativeSpec(PlanChoiceEffect.MoveToHand, PileType.Draw, 1, draw);
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
                case ("template_self_action", "r_putselectedhandcardondraw", "none", "none"):
                {
                    // 源码：从手牌选 1 张放到抽牌堆顶
                    var hand = owner.Hand.Cards;
                    return NativeSpec(PlanChoiceEffect.MoveToDrawTop, PileType.Hand, 1, hand);
                }
                case ("template_independent_action", "i_proxyatomic_dredge", "none", "none"):
                {
                    // 源码 L2711-2715：从弃牌堆选 N 张入手（Dredge 代理）
                    var discard = owner.DiscardPile.Cards;
                    var dredgeCount = Math.Max(1, card.OperationAmount(
                        card.Generated.Operations.ToList().FindIndex(op =>
                            op.Template == "I:ProxyAtomic_Dredge")));
                    return NativeSpec(PlanChoiceEffect.MoveToHand, PileType.Discard, dredgeCount, discard);
                }
                case ("choose_generated_card", "random_current_character", "current_character_pool", "hand"):
                case ("choose_generated_card", "random_colorless", "colorless_pool", "hand"):
                case ("choose_generated_card", "random_other_character_attack", "other_character_pools", "hand"):
                {
                    // 源码 L2471-2500：生成 N 张随机卡 → 玩家选 1 张入手
                    // 镜像：分支 RNG 生成候选 → GenerateToHand 选择
                    var candidateCount = ChaosOperationExecutor.GeneratedCardChoiceCandidateCount(card.OperationAmount(index));
                    if (candidateCount == 0) return null;
                    var constraint = card.Owner.RunState.CardMultiplayerConstraint;
                    IEnumerable<CardModel> pool = spec.Variant switch
                    {
                        "random_colorless" => ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(card.Owner.UnlockState, constraint),
                        "random_other_character_attack" => OtherCharacterPools(card.Owner.UnlockState.CharacterCardPools,
                                card.Owner.Character.CardPool)
                            .SelectMany(candidate => candidate.GetUnlockedCards(card.Owner.UnlockState, constraint))
                            .Where(candidate => candidate.Type == CardType.Attack),
                        _ => card.Owner.Character.CardPool.GetUnlockedCards(card.Owner.UnlockState, constraint),
                    };
                    var generated = pool.Where(ChaosOperationExecutor.CanBeRandomlyGeneratedInCombat)
                        .GetDistinctForCombat(card.Owner, candidateCount, simulator.Rng.CombatCardGeneration, constraint);
                    var options = generated.ToList();
                    if (options.Count == 0) return null;
                    if (ChaosOperationExecutor.GeneratedCardsAreUpgraded(card, index))
                        foreach (var generatedCard in options.Where(candidate => candidate.Preview.IsUpgradable)) generatedCard.Upgrade();
                    simulator.History.CardGenerationOptions(options);
                    return new CardChoiceSpec(PlanChoiceEffect.GenerateToHand, PileType.None, 0, 1,
                        options, options, ReplacementValue: 0d);
                }
                case ("template_self_action", "cl_choosefromrandomdrawcards", "none", "none"):
                {
                    // 源码 L1398-1408：从随机抽牌堆卡中选 1 张入手
                    var draw = owner.DrawPile.Cards;
                    if (draw.Count == 0) return null;
                    var optionCount = ChaosOperationExecutor.ExecutableGeneratedCardCount(amount);
                    if (optionCount == 0) return null;
                    var sampled = draw.ToList().StableShuffle(simulator.Rng.CombatCardSelection).Take(Math.Min(optionCount, 4)).ToHashSet();
                    return NativeSpec(PlanChoiceEffect.MoveToHand, PileType.Draw, 1, draw.Where(sampled.Contains).ToList());
                }
                case ("template_self_action", "r_putselectedhandcardsondraw", "none", "none"):
                {
                    // 源码：从手牌选 N 张放到抽牌堆
                    var hand = owner.Hand.Cards;
                    return NativeSpec(PlanChoiceEffect.MoveToDrawTop, PileType.Hand,
                        ChaosOperationExecutor.SelectionCountForEffect(operation, amount), hand);
                }
                case ("template_self_action", "r_copyselectedcolorlesscard", "none", "none"):
                {
                    var hand = owner.Hand.Cards.Where(candidate => candidate.Preview.VisualCardPool.IsColorless).ToList();
                    return NativeSpec(PlanChoiceEffect.Duplicate, PileType.Hand, 1, hand);
                }
                case ("template_self_action", "cl_transformselectedhandcards", "none", "none"):
                {
                    var hand = owner.Hand.Cards.Where(candidate => candidate.Preview.IsTransformable).ToList();
                    return NativeSpec(PlanChoiceEffect.Transform, PileType.Hand,
                        ChaosOperationExecutor.SelectionCountForEffect(operation, amount), hand);
                }
                case ("template_independent_action", "i_proxyatomic_begone", "none", "none"):
                case ("template_independent_action", "i_proxyatomic_guards", "none", "none"):
                {
                    // 源码 L2794-2825：从手牌选 N 张变形（Begone/Guards 代理）
                    var hand = owner.Hand.Cards.Where(candidate => candidate.Preview.IsTransformable).ToList();
                    if (spec.Variant == "i_proxyatomic_guards")
                        return hand.Count == 0 ? null : new CardChoiceSpec(PlanChoiceEffect.Transform, PileType.Hand, 0, hand.Count, hand, hand, ReplacementValue: 0d);
                    return NativeSpec(PlanChoiceEffect.Transform, PileType.Hand,
                        ChaosOperationExecutor.SelectionCountForEffect(operation, amount), hand);
                }
                case ("template_independent_action", "i_proxyatomic_charge", "none", "none"):
                case ("template_independent_action", "i_proxyatomic_seance", "none", "none"):
                {
                    // 源码 L2807-2835：从抽牌堆选 N 张变形（Charge/Seance 代理）
                    var draw = owner.DrawPile.Cards.Where(candidate => candidate.Preview.IsTransformable).ToList();
                    return NativeSpec(PlanChoiceEffect.Transform, PileType.Draw,
                        Math.Max(1, card.OperationAmount(index)), draw);
                }
                case ("exhaust_card", "selected", "draw", "none"):
                {
                    // 源码：从抽牌堆选 1 张消耗
                    var draw = owner.DrawPile.Cards;
                    return NativeSpec(PlanChoiceEffect.Exhaust, PileType.Draw, 1, draw);
                }
            }
        }

        return null;
    }

    internal static IReadOnlyList<CardPoolModel> OtherCharacterPools(IEnumerable<CardPoolModel> unlocked, CardPoolModel own)
    {
        var pools = unlocked.ToList();
        // Match AA's unlocked-pool order and single-pool fallback exactly.
        if (pools.Count > 1) pools.Remove(own);
        return pools;
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
            ("exhaust_card", "referenced", "none", "none") => true,
            ("exhaust_card", "referenced", "hand", "none") => true,
            ("template_independent_action", "i_upgrade" or "cl_exhaustuptohandcards" or "i_grantslytohandskillthisturn"
                or "cl_putselectedhandcardondrawtop" or "i_copyselectedcardnextturn" or "i_proxyatomic_transfigure", "none", "none") => true,
            ("template_self_action", "ncr_addvoidtoselectedhandcard" or "ncr_addretaintoselectedhandcard", "none", "none") => true,
            ("template_self_action", "r_playselectedskillmultipletimes", "none", "none") => true,
            ("discard_card", "selected", "hand", "none") => true,
            ("move_card", "selected", "discard", "hand") => true,
            ("move_card", "selected", "discard", "draw") => true,
            ("move_card", "selected", "draw", "hand") => true,
            ("template_independent_action", "cl_moveselectedattackdrawtohand", "none", "none") => true,
            ("template_independent_action", "cl_moveselectedskilldrawtohand", "none", "none") => true,
            ("template_self_action", "r_movediscardcardtodrawtop", "none", "none") => true,
            ("template_self_action", "r_putselectedhandcardondraw", "none", "none") => true,
            ("template_independent_action", "i_proxyatomic_dredge", "none", "none") => true,
            ("choose_generated_card", "random_current_character", "current_character_pool", "hand") => true,
            ("choose_generated_card", "random_colorless", "colorless_pool", "hand") => true,
            ("choose_generated_card", "random_other_character_attack", "other_character_pools", "hand") => true,
            ("template_self_action", "cl_choosefromrandomdrawcards", "none", "none") => true,
            ("template_self_action", "r_putselectedhandcardsondraw", "none", "none") => true,
            ("template_self_action", "r_copyselectedcolorlesscard", "none", "none") => true,
            ("template_self_action", "cl_transformselectedhandcards", "none", "none") => true,
            ("template_independent_action", "i_proxyatomic_begone", "none", "none") => true,
            ("template_independent_action", "i_proxyatomic_guards", "none", "none") => true,
            ("template_independent_action", "i_proxyatomic_charge", "none", "none") => true,
            ("template_independent_action", "i_proxyatomic_seance", "none", "none") => true,
            ("exhaust_card", "selected", "draw", "none") => true,
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
        var clamped = Math.Min(count, options.Count);
        return new CardChoiceSpec(effect, source, clamped, clamped, options, options,
            ReplacementValue: 0d,
            IsImplicitAllSelection: options.Count <= count);
    }
}
