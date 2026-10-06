using MegaCrit.Sts2.Core.Entities.Cards;

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>Local to one complete OnPlay execution; never shared with another branch or card play.</summary>
public sealed class OperationResolutionState
{
    private CardType[] _lastDrawnTypes = [];
    public bool LastDrawnCardIsSkill => _lastDrawnTypes is [CardType.Skill];
    public void RecordDrawnTypes(IEnumerable<CardType> types) => _lastDrawnTypes = types.ToArray();

    /// <summary>源码 L46/L1171：上一次攻击是否击杀了目标（fatal 条件的数据源）。</summary>
    public bool LastAttackKilled { get; set; }

    /// <summary>源码 ChaosExecutionState.ExhaustedByCard：本卡打出过程中消耗的卡（ForEach 触发器的数据源）。</summary>
    public List<global::CombatSolver.Engine.Common.PredictedCard> ExhaustedByCard { get; } = [];
}
