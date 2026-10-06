using MegaCrit.Sts2.Core.Entities.Cards;

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>Local to one complete OnPlay execution; never shared with another branch or card play.</summary>
public sealed class OperationResolutionState
{
    private CardType[] _lastDrawnTypes = [];
    public bool LastDrawnCardIsSkill => _lastDrawnTypes is [CardType.Skill];
    public void RecordDrawnTypes(IEnumerable<CardType> types) => _lastDrawnTypes = types.ToArray();
}
