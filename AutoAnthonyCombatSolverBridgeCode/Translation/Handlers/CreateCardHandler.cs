using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Extensions;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// create_card 的随机生成精确镜像（0.7.0：4 个 variant）。
/// 复刻源码的四条路径，逐一对齐 RNG 选取算法（distinct=TakeRandom / 带放回=逐次 NextItem）
/// 与候选过滤：
///  - random_colorless（TryExecuteStructuredCommon L842-854）：无色池，
///    CanBeRandomlyGeneratedInCombat + 排除本卡，distinct 选取，入手；
///  - current_character_random（CreateCard L2951-2954）：当前角色池，
///    CanBeRandomlyGeneratedInCombat，distinct 选取，按 DestinationZone 入手/弃牌堆；
///  - random_zero_cost（CL:AddRandomZeroCostCardsToHand L1311-1323）：角色池，
///    额外 EnergyCost{Canonical:0, CostsX:false}，**带放回**选取（GetForCombat）；
///  - random_attack_zero_cost_this_turn（I:Create L3182-3201）：角色池攻击牌，
///    distinct 选取 + SetToFreeThisTurn。
/// 镜像入口：模拟器的生成扩展（分支 CombatCardGeneration 流 + 根资格缓存 + PredictedCard
/// 创建），与原生生成卡镜像（ManifestAuthority/Metamorphosis/TinkerTime）同一套 API。
/// 生成的卡进入模拟牌堆后由既有镜像递归覆盖（混沌卡走本桥、原生卡走原生镜像）。
/// </summary>
public sealed class CreateCardHandler : IOperationHandler
{
    public string Describe => "create_card(4 variant)：分支 RNG 随机生成精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return (spec.Variant, spec.Target, spec.SourceZone, spec.DestinationZone) switch
        {
            ("random_colorless", "generated_card", "colorless_pool", "hand") => null,
            ("current_character_random", "generated_card", "current_character_pool", "hand") => null,
            ("random_zero_cost", "generated_card", "current_character_pool", "hand") => null,
            ("random_attack_zero_cost_this_turn", "generated_card", "current_character_pool", "hand") => null,
            _ => $"create_card 的 (variant={spec.Variant}, target={spec.Target}, zones={spec.SourceZone}->{spec.DestinationZone}) 组合不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var spec = context.Shape.Spec;
        var operation = context.Card.Generated.Operations[context.Shape.OperationIndex];
        var count = ChaosOperationExecutor.ExecutableOperationCount(operation, context.ExecutableAmount);
        if (count == 0)
            return;    // 源码语义：count=0 = no-op

        var mirror = context.Mirror;
        var owner = context.Card.Owner;
        var rng = mirror.Rng.CombatCardGeneration;
        var constraint = mirror.CardMultiplayerConstraint;

        // AutoAnthony 的候选过滤（CanBeRandomlyGeneratedInCombat 排除受限效果）+ 各变体的附加条件
        Func<CardModel, bool>? filter = spec.Variant switch
        {
            "random_colorless" => candidate =>
                ChaosOperationExecutor.CanBeRandomlyGeneratedInCombat(candidate)
                && candidate.Id != context.Card.Id,
            "current_character_random" => ChaosOperationExecutor.CanBeRandomlyGeneratedInCombat,
            "random_zero_cost" => candidate =>
                ChaosOperationExecutor.CanBeRandomlyGeneratedInCombat(candidate)
                && candidate.EnergyCost is { Canonical: 0, CostsX: false },
            "random_attack_zero_cost_this_turn" => candidate =>
                ChaosOperationExecutor.CanBeRandomlyGeneratedInCombat(candidate)
                && candidate.Type == CardType.Attack,
            _ => null,
        };

        IEnumerable<PredictedCard> generated = spec.Variant switch
        {
            // distinct 选取（TakeRandom）——与源码 CardFactory.GetDistinctForCombat 同算法
            "random_colorless" =>
                mirror.Simulator.GetDistinctUnlockedColorlessForCombat(owner, count, rng, constraint, filter),
            "current_character_random" =>
                mirror.Simulator.GetDistinctUnlockedCharacterCardsForCombat(owner, count, rng, constraint, filter),
            // 带放回选取（逐次 NextItem）——与源码 CardFactory.GetForCombat 同算法
            "random_zero_cost" =>
                mirror.Simulator.GetUnlockedCharacterCardsForCombat(owner, count, rng, constraint, filter),
            // distinct 选取 + 本回合免费
            "random_attack_zero_cost_this_turn" =>
                mirror.Simulator.GetDistinctUnlockedCharacterCardsForCombat(owner, count, rng, constraint, filter)
                    .Select(generatedCard => generatedCard.SetToFreeThisTurn()),
            _ => throw new UnsupportedRuntimeSpecException(spec.Opcode, spec.Variant),
        };

        // 源码：DestinationZone == "discard" ? Discard : Hand（默认 Bottom，与 CardPileCmd 一致）
        var pile = spec.DestinationZone == "discard" ? PileType.Discard : PileType.Hand;
        mirror.Simulator.AddGeneratedCardsToCombat(generated.ToList(), pile, owner);
    }
}
