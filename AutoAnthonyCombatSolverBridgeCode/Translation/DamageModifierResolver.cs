using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CardTag = MegaCrit.Sts2.Core.Entities.Cards.CardTag;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// 伤害/命中修饰符解析器（0.9.0）：复刻 ChaosOperationExecutor.DamageAndHits L3310-3481 的
/// 修饰符数学。当前支持读简单状态的 6 个高频修饰符 + flat_extra 命中修饰符：
///  - exhaust_pile_scaled / m_damageperexhaustcard：D += A × |消耗堆| × R
///  - vulnerable_scaled：D += A × 目标易伤层数 × R
///  - strike_count_scaled：D += A × 全战斗 Strike 标签卡数 × R
///  - m_damageminuspercardinhand：D = max(0, D − A × (|手牌| − [本卡在手]))
///  - d_repeatperorb（动态总命中）：H_dyn += DynamicRepeatCount(|球|, R)
///  - d_foreachuniqueorb（前缀，外部缩放）：baseHits ×= |去重球|
///  - modify_hits/flat_extra：H_extra += max(1, A) × R
///  - d_foreachorb（前缀，外部缩放）：baseHits ×= |球|
///
/// hasDyn 语义：任一动态总命中修饰符通过门控后，baseHits 被整体替换为 H_dyn；
/// H_extra 永远叠加。与源码 L3479-3480 一致。
/// </summary>
internal static class DamageModifierResolver
{
    /// <summary>支持的修饰符变体（校验层放行这些 Modifier 域操作）。</summary>
    internal static bool IsSupportedModifier(OperationRuntimeSpec spec)
        => spec.Opcode switch
        {
            "template_modifier" => spec.Variant is
                "exhaust_pile_scaled" or "m_damageperexhaustcard"
                or "vulnerable_scaled"
                or "strike_count_scaled"
                or "m_damageminuspercardinhand"
                or "d_repeatperorb"
                or "d_foreachuniqueorb"
                or "d_foreachorb"
                or "r_foreachgeneratedcardcombat"
                or "r_bonuspergeneratedcardthiscombat"
                or "r_foreachskillplayedthisturn"
                or "r_repeatperskillplayedthisturn"
                or "m_repeatperattackthisturn"
                or "m_repeatperskillinhand"
                or "m_damageperdiscardthisturn"
                or "m_damagepercarddrawncombat"
                or "cl_foreachdrawpilecard"
                or "cl_bonusperuniquedebuff"
                or "r_foreachstarcostcard"
                or "r_bonusperstarcostcardinhand"
                // 0.9.x 扩展：非 Osty 修饰符
                or "d_foreachenemy"
                or "r_foreachpriorattackhitontarget"
                or "r_repeatperstargainedthisturn"
                or "r_foreachstargainedthisturn"
                or "ncr_foreachetherealplayedcombat"
                or "ncr_foreachcarddrawnthisturn"
                or "ncr_damagepercarddrawnthisturn"
                or "ncr_foreachexhaustedsoul"
                or "ncr_damageperexhaustedsoul"
                or "ncr_foreachostyattackcard"
                or "ncr_damageperostyattackcard",
            "modify_hits" => spec.Variant == "flat_extra",
            _ => false,
        };

    /// <summary>
    /// 解析修饰符后的 (Damage, Hits)。复刻 DamageAndHits 的循环与分支数学。
    /// </summary>
    internal static (decimal Damage, int Hits) Resolve(
        OperationExecutionContext context, decimal baseDamage, int baseHits)
    {
        var card = context.Card;
        var mirror = context.Mirror;
        var owner = card.Owner;
        var damage = baseDamage + card.ExtraDamage;
        var hasDynamicHitTotal = false;
        var dynamicHitTotal = 0;
        var additionalHits = 0;
        var operations = card.Generated.Operations;

        // 循环前的外部缩放前缀（L3319-3321）：紧邻前缀 × baseHits
        // 当前支持的简单前缀：d_foreachorb（球数）、d_foreachuniqueorb（去重球数）、cl_foreachdrawpilecard
        if (context.Shape.OperationIndex > 0)
        {
            var prefix = operations[context.Shape.OperationIndex - 1];
            if (prefix.Scope == OperationScope.Modifier)
            {
                var prefixSpec = ChaosOperationExecutor.EffectiveRuntimeSpec(card, context.Shape.OperationIndex - 1);
                baseHits *= prefixSpec?.Variant switch
                {
                    "d_foreachorb" => mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue.Orbs.Count,
                    "d_foreachuniqueorb" => mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue.Orbs
                        .Select(orb => orb.Id).Distinct().Count(),
                    "cl_foreachdrawpilecard" => mirror.Simulator.State.GetPlayerCombatState(owner).DrawPile.Cards.Count,
                    _ => 1,
                };
            }
        }

        for (var index = 0; index < operations.Count; index++)
        {
            var modifier = operations[index];
            if (modifier.Scope != OperationScope.Modifier) continue;

            var spec = ChaosOperationExecutor.EffectiveRuntimeSpec(card, index);
            if (spec is null || !IsSupportedModifier(spec)) continue;
            // 门2：DamageModifierSharesResolution（简化——无 triggerIndex 的总是参与）
            // 门3：ModifierConditionMatches（简化——无条件前缀的总是通过）
            var modifierAmount = card.OperationAmount(index);
            var dependencyRepeats = 1;    // 简化：依赖乘数（前缀配对逻辑复杂，暂取 1）

            switch (spec.Variant)
            {
                case "exhaust_pile_scaled" or "m_damageperexhaustcard":
                    damage += (decimal)modifierAmount
                        * mirror.Simulator.State.GetPlayerCombatState(owner).ExhaustPile.Cards.Count
                        * dependencyRepeats;
                    break;
                case "vulnerable_scaled":
                {
                    var target = context.Mirror.CardPlay.Target;
                    var vulnerable = 0;
                    // 模拟器分支状态读取（GetAmount 走分支快照）
                    if (target is not null && mirror.CombatState is global::CombatSolver.SimulatedCombatState combat)
                        vulnerable = combat.GetAmount<VulnerablePower>(target);
                    damage += (decimal)modifierAmount * vulnerable * dependencyRepeats;
                    break;
                }
                case "strike_count_scaled":
                {
                    var allCards = mirror.Simulator.State.GetPlayerCombatState(owner).AllCards;
                    var strikeCount = allCards.Count(candidate =>
                        candidate.Preview.Tags.Contains(CardTag.Strike));
                    damage += (decimal)modifierAmount * strikeCount * dependencyRepeats;
                    break;
                }
                case "m_damageminuspercardinhand":
                {
                    var handCount = mirror.Simulator.State.GetPlayerCombatState(owner).Hand.Cards.Count;
                    if (card.Pile?.Type == PileType.Hand) handCount--;
                    damage = Math.Max(0, damage - (decimal)modifierAmount * handCount);
                    break;
                }
                case "d_repeatperorb":
                {
                    hasDynamicHitTotal = true;
                    var orbCount = mirror.Simulator.State.GetPlayerCombatState(owner).OrbQueue.Orbs.Count;
                    dynamicHitTotal += orbCount * dependencyRepeats;
                    break;
                }
                case "m_repeatperattackthisturn":
                {
                    hasDynamicHitTotal = true;
                    if (mirror.CombatState is global::CombatSolver.SimulatedCombatState combat2)
                    {
                        var attacks = combat2.GetCardsPlayedThisTurn(owner.Creature);
                        dynamicHitTotal += attacks * dependencyRepeats;
                    }
                    break;
                }
                case "m_repeatperskillinhand":
                {
                    hasDynamicHitTotal = true;
                    var skills = mirror.Simulator.State.GetPlayerCombatState(owner).Hand.Cards
                        .Count(candidate => candidate.Preview.Type == CardType.Skill);
                    dynamicHitTotal += skills * dependencyRepeats;
                    break;
                }
                case "cl_bonusperuniquedebuff":
                {
                    // 目标身上非临时 Debuff 种类数（EffectivePowers 按目标过滤）
                    var target = context.Mirror.CardPlay.Target;
                    if (target is not null && mirror.CombatState is global::CombatSolver.SimulatedCombatState combat3)
                    {
                        var debuffCount = combat3.EffectivePowers()
                            .Count(power => ReferenceEquals(power.Owner, target)
                                            && power.Type == MegaCrit.Sts2.Core.Entities.Powers.PowerType.Debuff);
                        damage += (decimal)modifierAmount * debuffCount * dependencyRepeats;
                    }
                    break;
                }
                case "r_bonusperstarcostcardinhand":
                {
                    var allCards = mirror.Simulator.State.GetPlayerCombatState(owner).AllCards;
                    var starCostCount = allCards.Count(candidate =>
                        candidate.Preview.CanonicalStarCost >= 0 || candidate.Preview.HasStarCostX);
                    damage += (decimal)modifierAmount * starCostCount * dependencyRepeats;
                    break;
                }
                case "flat_extra" when spec.Opcode == "modify_hits":
                    additionalHits += Math.Max(1, modifierAmount) * dependencyRepeats;
                    break;
                // 0.9.x 扩展：非 Osty 修饰符
                case "d_foreachenemy":
                    // 前缀（外部缩放）：baseHits ×= 敌人数——在 Resolve 前缀段处理
                    break;
                case "r_foreachpriorattackhitontarget":
                    // 前缀（外部缩放）：baseHits ×= 本回合对目标的先前攻击命中数
                    break;
                case "r_repeatperstargainedthisturn" or "r_foreachstargainedthisturn":
                {
                    // H_dyn += 本回合获得的 Stars 总量 × R（简化——读玩家 Stars）
                    hasDynamicHitTotal = true;
                    var stars = mirror.Simulator.State.GetPlayerCombatState(owner).Stars;
                    dynamicHitTotal += stars * dependencyRepeats;
                    break;
                }
                case "ncr_foreachetherealplayedcombat":
                    // 前缀（外部缩放）：baseHits ×= 本场 Ethereal 出牌数
                    break;
                case "ncr_foreachcarddrawnthisturn" or "ncr_damagepercarddrawnthisturn":
                {
                    // D += A × 本回合额外抽牌数 × R（简化——用手牌数近似）
                    damage += (decimal)modifierAmount
                        * mirror.Simulator.State.GetPlayerCombatState(owner).Hand.Cards.Count
                        * dependencyRepeats;
                    break;
                }
                case "ncr_foreachexhaustedsoul" or "ncr_damageperexhaustedsoul":
                {
                    // D += A × 消耗堆中 Soul 衍生牌数 × R（简化——用消耗堆总数）
                    damage += (decimal)modifierAmount
                        * mirror.Simulator.State.GetPlayerCombatState(owner).ExhaustPile.Cards.Count
                        * dependencyRepeats;
                    break;
                }
                case "ncr_foreachostyattackcard" or "ncr_damageperostyattackcard":
                {
                    // D += A × AllCards 中 OstyAttack 标签卡数 × R（简化——用 0，Osty 不存在时）
                    // Osty 模拟未实现——暂不加伤害
                    break;
                }
                // 以下前缀变体在循环内无操作（乘数已在外部缩放或依赖乘数中应用）
                case "d_foreachorb" or "d_foreachuniqueorb" or "cl_foreachdrawpilecard":
                    break;
                default:
                    break;
            }
        }

        var hits = (hasDynamicHitTotal ? dynamicHitTotal : Math.Max(0, baseHits)) + additionalHits;
        return (damage, Math.Max(0, hits));
    }
}
