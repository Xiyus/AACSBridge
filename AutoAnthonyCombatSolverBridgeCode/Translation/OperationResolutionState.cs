using MegaCrit.Sts2.Core.Entities.Cards;
using CombatSolver.Engine.Common;
using MegaCrit.Sts2.Core.Models;

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>Local to one complete OnPlay execution; never shared with another branch or card play.</summary>
public sealed class OperationResolutionState
{
    private CardType[] _lastDrawnTypes = [];
    public bool LastDrawnCardIsSkill => _lastDrawnTypes is [CardType.Skill];
    public void RecordDrawnTypes(IEnumerable<CardType> types) => _lastDrawnTypes = types.ToArray();

    /// <summary>源码 L46/L1171：上一次攻击是否击杀了目标（fatal 条件的数据源）。</summary>
    public bool LastAttackKilled { get; set; }
    public int? PriorAttackHitsOnTargetAtPlayStart { get; set; }
    public int LastDamageDealt { get; set; }
    public bool EndTurnRequested { get; set; }
    public Dictionary<string, List<PredictedCard>> CardSelections { get; } = new(StringComparer.Ordinal);
    public PredictedCard? LastMovedCard { get; set; }
    public PredictedCard? IterationCard { get; set; }
    public int LastExhaustedAttackDamage { get; set; }
    public Dictionary<PowerModel, int>? TargetDebuffSnapshot { get; set; }

    /// <summary>源码 ChaosExecutionState.ExhaustedByCard：本卡打出过程中消耗的卡（ForEach 触发器的数据源）。</summary>
    public List<global::CombatSolver.Engine.Common.PredictedCard> ExhaustedByCard { get; } = [];
    public List<PredictedCard> DiscardedByCard { get; } = [];

    internal void PrepareFork(PredictionForkContext context)
    {
        foreach (var card in ExhaustedByCard.Concat(DiscardedByCard))
            if (!context.TryRemap(card, out PredictedCard? _)) card.Fork(context);
        foreach (var card in CardSelections.Values.SelectMany(cards => cards).Concat(new[] { LastMovedCard, IterationCard }.OfType<PredictedCard>()))
            if (!context.TryRemap(card, out PredictedCard? _)) card.Fork(context);
    }

    internal OperationResolutionState Fork(PredictionForkContext context)
    {
        if (context.TryRemap(this, out OperationResolutionState? existing)) return existing!;
        var copy = new OperationResolutionState { LastAttackKilled = LastAttackKilled, LastDamageDealt = LastDamageDealt, EndTurnRequested = EndTurnRequested,
            PriorAttackHitsOnTargetAtPlayStart = PriorAttackHitsOnTargetAtPlayStart, _lastDrawnTypes = _lastDrawnTypes.ToArray() };
        copy.ExhaustedByCard.AddRange(ExhaustedByCard.Select(context.RequireRemap));
        copy.DiscardedByCard.AddRange(DiscardedByCard.Select(context.RequireRemap));
        foreach (var pair in CardSelections) copy.CardSelections.Add(pair.Key, pair.Value.Select(context.RequireRemap).ToList());
        copy.LastMovedCard = LastMovedCard is null ? null : context.RequireRemap(LastMovedCard);
        copy.IterationCard = IterationCard is null ? null : context.RequireRemap(IterationCard);
        copy.LastExhaustedAttackDamage = LastExhaustedAttackDamage;
        copy.TargetDebuffSnapshot = TargetDebuffSnapshot?.ToDictionary(pair => PredictionUtils.CloneModelForSimulation(pair.Key), pair => pair.Value);
        context.Register(this, copy);
        return copy;
    }
}
