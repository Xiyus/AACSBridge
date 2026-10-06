using AutoAnthony;
using CombatSolver;
using CombatSolver.Engine.Common;

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

/// <summary>Detached saved state. Never mutate a receiver shared by sibling branches.</summary>
internal sealed class ChaosCompositePredictionState : IPredictionStateForkable, IPredictionForkBoundary
{
    public HashSet<int> ActiveTriggers { get; private set; } = [];
    public Dictionary<int, int> Activations { get; private set; } = [];
    public ChaosCompositePower Snapshot { get; private set; }
    public int[] FingerprintValues { get; private set; }
    public long CapturedValuesHash { get; }
    public long DefinitionHash { get; }
    public long ProfileHash { get; }

    public ChaosCompositePredictionState(ChaosCompositePower source)
    {
        Snapshot = PredictionUtils.CloneModelForSimulation(source);
        Snapshot.CapturedOperationValues = source.CapturedOperationValues.ToArray();
        FingerprintValues = Snapshot.CaptureMultiplayerState();
        if (FingerprintValues.Length < 34 || FingerprintValues[0] != 5)
            throw PredictionUnsupportedException.ForContent("ChaosCompositePower 隐藏状态 schema 不是 5。", typeof(ChaosCompositePower));
        CapturedValuesHash = Hash(string.Join(',', Snapshot.CapturedOperationValues));
        DefinitionHash = Hash(Snapshot.SourceTinkeredDefinitionPayload);
        ProfileHash = Hash(Snapshot.ProfileId);
    }

    public void Refresh() => FingerprintValues = Snapshot.CaptureMultiplayerState();

    public object Fork(PredictionForkContext context)
    {
        AssertForkable();
        var copy = (ChaosCompositePredictionState)MemberwiseClone();
        copy.Snapshot = PredictionUtils.CloneModelForSimulation(Snapshot);
        copy.Snapshot.CapturedOperationValues = Snapshot.CapturedOperationValues.ToArray();
        copy.FingerprintValues = FingerprintValues.ToArray();
        copy.ActiveTriggers = [];
        copy.Activations = new(Activations);
        return copy;
    }

    public void AssertForkable()
    {
        if (ActiveTriggers.Count > 0) throw new InvalidOperationException("不能在复合 Power 触发收益结算中 Fork。");
    }

    internal static long Hash(string text)
    {
        unchecked
        {
            ulong value = 14695981039346656037;
            foreach (var character in text) value = (value ^ character) * 1099511628211;
            return (long)value;
        }
    }
}
