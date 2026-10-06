using System.Reflection;
using System.Runtime.Loader;
using AutoAnthony;
using ChaosCardGenerator;
using AutoAnthonyCombatSolverBridge.Bootstrap;

// Dependencies remain the installed binaries; never replace them with publicized copies at runtime.
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var directory in args)
    {
        var path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
    }
    return null;
};
Run();

void Run()
{
    var assembly = typeof(CompatibilityGuard).Assembly;
    var policy = assembly.GetType("AutoAnthonyCombatSolverBridge.CombatSolver.ChaosTriggerPolicy")!;
    var validateTrigger = policy.GetMethod("ValidateTrigger", BindingFlags.Static | BindingFlags.NonPublic)!;
    var validatePayload = policy.GetMethod("ValidatePayload", BindingFlags.Static | BindingFlags.NonPublic)!;
    var checks = 0;
    void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        checks++;
    }
    OperationRuntimeSpec Spec(string opcode, string variant, string target, RuntimeTriggerSpec? trigger = null,
        RuntimeValueSlot[]? values = null, string[]? flags = null) =>
        new(1, opcode, variant, target, "none", "none", "any", flags ?? [], values ?? [], Trigger: trigger);
    GeneratorOperation Op(OperationRuntimeSpec spec, string template = "A:whenCardPlayed",
        OperationScope scope = OperationScope.AbilityTrigger) =>
        new(template, scope, "UNUSED TEXT", new Dictionary<string, int>(), RuntimeSpec: spec);
    string? TriggerReason(GeneratorOperation op) => (string?)validateTrigger.Invoke(null, [op, op.RuntimeSpec!]);
    string? PayloadReason(OperationRuntimeSpec spec) => (string?)validatePayload.Invoke(null, [spec]);

    foreach (var kind in new[] { "turn_start", "turn_end", "card_played", "attack_played", "skill_played",
                 "power_played", "card_drawn", "card_exhausted" })
        Check(TriggerReason(Op(Spec("trigger", "event", "self", new(kind, "combat")))) is null, kind);
    var delayed = Op(Spec("trigger", "event", "self", new("next_turn_start", "next_turn")),
        "C:NextTurnStart", OperationScope.ConditionalTrigger);
    Check(TriggerReason(delayed) is null, "next turn");
    var repeated = Op(Spec("trigger", "event", "self", new("next_turns_start", "next_n_turns", DurationSlot: "duration"),
        [new("duration", 3)]), "C:NextTurnsStart", OperationScope.ConditionalTrigger);
    Check(TriggerReason(repeated) is null, "next N turns");
    Check(TriggerReason(delayed with { Parameters = new Dictionary<string, int> { ["triggerIndex"] = 0 } }) is not null,
        "nested trigger rejected");
    Check(TriggerReason(delayed with { RuntimeSpec = delayed.RuntimeSpec! with { Trigger = new("unknown", "next_turn") } }) is not null,
        "unknown trigger rejected");
    Check(TriggerReason(delayed with { RuntimeSpec = delayed.RuntimeSpec! with { Condition = new("unknown", "self") } }) is not null,
        "condition rejected");
    Check(TriggerReason(Op(Spec("trigger", "event", "self", new("card_played", "this_turn")))) is not null,
        "temporary lifetime rejected");
    foreach (var tuple in new[] { ("gain_block", "immediate", "self"), ("gain_energy", "immediate", "self"),
                 ("heal", "immediate", "self"), ("deal_damage", "all", "all_enemies"), ("deal_damage", "random", "random_enemy") })
        Check(PayloadReason(Spec(tuple.Item1, tuple.Item2, tuple.Item3)) is null, "accepted payload " + tuple.Item1);
    Check(PayloadReason(Spec("draw_cards", "immediate", "self")) is not null, "recursive draw rejected");
    Check(PayloadReason(Spec("deal_damage", "selected", "selected_enemy")) is not null, "selected target rejected");
    Check(PayloadReason(Spec("gain_block", "immediate", "self", values: [new("block", 0, "energy_x")])) is not null,
        "dynamic triggered values rejected");
    Check(PayloadReason(Spec("gain_block", "immediate", "self", flags: ["unknown"])) is not null, "unknown flag rejected");
    var atoms = Enum.GetValues<GeneratedCharacter>().SelectMany(character => CharacterComponentCatalogs.Get(character).Atoms).ToArray();
    var triggerAtoms = 0;
    var payloadAtoms = 0;
    foreach (var atom in atoms)
    {
        var spec = OperationRuntimeSpecCompiler.GetOrCompile(atom);
        var op = new GeneratorOperation(atom.Template, atom.Scope, "ignored", new Dictionary<string, int>(), RuntimeSpec: spec);
        if (TriggerReason(op) is null) triggerAtoms++;
        if (PayloadReason(spec) is null) payloadAtoms++;
    }
    Check(triggerAtoms > 0 && payloadAtoms > 0, "installed catalog includes supported trigger and payload atoms");
    Console.WriteLine($"Catalog: {triggerAtoms} trigger atoms, {payloadAtoms} atomic payoff atoms (card-level validation still required)");

    // Validate private seams and hashes against the actual installed binaries, with a negative hash control.
    var solver = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("CombatSolver"));
    var aa = typeof(ChaosCompositePower).Assembly;
    static BridgeModFacts Facts(Assembly target) => new(target.GetName().Name!, target.Location, null, null,
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(target.Location))).ToLowerInvariant(),
        null, null, [], []);
    var seams = typeof(CompatibilityGuard).GetMethod("VerifyCompositeSeams", BindingFlags.NonPublic | BindingFlags.Static)!;
    var failures = new List<string>();
    seams.Invoke(null, [Facts(aa), Facts(solver), solver, failures]);
    Check(failures.Count == 0, "installed binary seams: " + string.Join("; ", failures));
    seams.Invoke(null, [Facts(aa), Facts(solver) with { Sha256 = "wrong" }, solver, failures]);
    Check(failures.Count == 1, "changed solver hash must fail closed");

    // Capture and fork the real Power implementation, without executing a real combat command.
    var power = new ChaosCompositePower();
    typeof(MegaCrit.Sts2.Core.Models.AbstractModel).GetProperty("IsMutable")!.SetValue(power, true);
    power.WaitForNextTurn = true;
    power.RemainingTurnTriggers = 3;
    power.CapturedOperationValues = [1, 7];
    var stateType = assembly.GetType("AutoAnthonyCombatSolverBridge.CombatSolver.ChaosCompositePredictionState")!;
    var state = Activator.CreateInstance(stateType, power)!;
    var snapshot = (ChaosCompositePower)stateType.GetProperty("Snapshot")!.GetValue(state)!;
    var fork = stateType.GetMethod("Fork")!.Invoke(state, [null])!;
    var forkSnapshot = (ChaosCompositePower)stateType.GetProperty("Snapshot")!.GetValue(fork)!;
    forkSnapshot.WaitForNextTurn = false;
    forkSnapshot.RemainingTurnTriggers--;
    forkSnapshot.CapturedOperationValues[1] = 99;
    stateType.GetMethod("Refresh")!.Invoke(fork, null);
    Check(snapshot.WaitForNextTurn && snapshot.RemainingTurnTriggers == 3, "sibling delayed state isolated");
    Check(snapshot.CapturedOperationValues[1] == 7 && power.CapturedOperationValues[1] == 7, "captured values deep fork");
    var rootVector = (int[])stateType.GetProperty("FingerprintValues")!.GetValue(state)!;
    var forkVector = (int[])stateType.GetProperty("FingerprintValues")!.GetValue(fork)!;
    Check(!rootVector.SequenceEqual(forkVector), "hidden state changes fingerprint");
    var active = (HashSet<int>)stateType.GetProperty("ActiveTriggers")!.GetValue(state)!;
    active.Add(0);
    try
    {
        stateType.GetMethod("Fork")!.Invoke(state, [null]);
        throw new Exception("active trigger fork unexpectedly succeeded");
    }
    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { checks++; }
    active.Clear();

    // Exact continuation serialization must distinguish counters, not only public Amount.
    var owner = (MegaCrit.Sts2.Core.Entities.Creatures.Creature)System.Runtime.CompilerServices.RuntimeHelpers
        .GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature));
    typeof(MegaCrit.Sts2.Core.Models.PowerModel).GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(power, owner);
    var stampPatch = assembly.GetType("AutoAnthonyCombatSolverBridge.Patches.CompositePowerContinuationPatch")!;
    var append = stampPatch.GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static)!;
    string Stamp()
    {
        var text = new System.Text.StringBuilder();
        append.Invoke(null, [text, new MegaCrit.Sts2.Core.Models.PowerModel[] { power }, null]);
        return text.ToString();
    }
    typeof(MegaCrit.Sts2.Core.Models.PowerModel).GetField("_amount", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(power, 1);
    var firstStamp = Stamp();
    power.RemainingTurnTriggers--;
    Check(firstStamp != Stamp(), "continuation detects hidden counter mismatch");
    power.ProfileId = "profile;测试";
    Check(Stamp().Count(character => character == ';') == 1, "metadata cannot inject stamp fields");

    // Exercise every action registration against the unmodified installed solver. This catches
    // override-signature and duplicated-registry errors which a successful compile cannot detect.
    var mirror = assembly.GetType("AutoAnthonyCombatSolverBridge.CombatSolver.ChaosCompositePowerMirror")!;
    mirror.GetMethod("Register")!.Invoke(null, null);
    var hidden = solver.GetType("CombatSolver.PowerHiddenStateMirrors")!;
    var slots = (System.Collections.IDictionary)hidden.GetField("Registry", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    Check(slots.Contains(typeof(ChaosCompositePower)), "hidden state registry installed");
    var captures = (System.Collections.IDictionary)hidden.GetField("RootCaptures", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    Check(captures.Contains(typeof(ChaosCompositePower)), "root state capture installed");
    var harmony = new HarmonyLib.Harmony("AutoAnthonyCombatSolverBridge.contract-checks");
    try
    {
        harmony.PatchAll(assembly);
        Check(HarmonyLib.Harmony.GetAllPatchedMethods().Any(method =>
            method.DeclaringType?.FullName == "CombatSolver.ContinuationStamp" && method.Name == "AppendPowers"),
            "continuation seam patches actual method");
    }
    finally { harmony.UnpatchAll(harmony.Id); }
    Console.WriteLine($"PASS {checks} bridge contract / binary seam / hidden-state isolation checks");
}
