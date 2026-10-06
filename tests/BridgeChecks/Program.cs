using System.Reflection;
using System.Runtime.Loader;
using AutoAnthony;
using ChaosCardGenerator;
using AutoAnthonyCombatSolverBridge.Bootstrap;
using AutoAnthonyCombatSolverBridge.Translation;
using CardType = MegaCrit.Sts2.Core.Entities.Cards.CardType;

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
    HandlerCatalog.RegisterAll(OperationHandlerRegistry.Instance);
    var independent = new AutoAnthonyCombatSolverBridge.Translation.Handlers.TemplateIndependentActionHandler();
    var unsupportedIndependent = new[] { "i_upgrade", "i_playtopcardandexhaust", "i_playthiscard", "cl_exhaustuptohandcards" };
    foreach (var variant in unsupportedIndependent)
        Check(independent.ValidateSupport(new(0, OperationScope.Independent,
            Spec("template_independent_action", variant, "self"))) is not null,
            "incomplete independent effect rejected: " + variant);
    foreach (var variant in new[] { "i_drawwithretain", "i_triggerpoisonnow", "i_nextskillcostszero",
                 "i_proxyatomic_foregoneconclusion", "i_proxyatomic_multicast", "i_proxyatomic_tempest" })
    {
        var spec = Spec("template_independent_action", variant, "self");
        Check(RuntimeSpecTranslator.CanTranslate(spec)
              && independent.ValidateSupport(new(0, OperationScope.Independent, spec)) is null,
            "independent effect registered and supported: " + variant);
    }

    // A supported condition must not bypass validation of its payoff, even before evaluating the condition.
    var validateOperation = assembly.GetType("AutoAnthonyCombatSolverBridge.CombatSolver.ChaosCardOnPlayMirror")!
        .GetMethod("ValidateOperation", BindingFlags.NonPublic | BindingFlags.Static)!;
    var conditionSpec = Spec("condition", "gate", "self") with { Condition = new("hand_empty", "self") };
    var conditionOp = Op(conditionSpec, "C:HandEmpty", OperationScope.ConditionalTrigger);
    foreach (var variant in unsupportedIndependent.Append("unknown_effect"))
    {
        var payoff = Op(Spec("template_independent_action", variant, "self"),
            "I:RegressionFixture", OperationScope.Independent) with
        { Parameters = new Dictionary<string, int> { ["triggerIndex"] = 0 } };
        var fixture = TestChaosCard.Create([conditionOp, payoff]);
        var reason = (string?)validateOperation.Invoke(null, [fixture, 1, payoff]);
        Check(reason is not null, "condition cannot authorize unsupported payoff: " + variant);
    }
    var supportedPayoff = Op(Spec("gain_block", "immediate", "self", values: [new("block", 3)]),
        "N:B", OperationScope.NonTargeted) with
    { Parameters = new Dictionary<string, int> { ["triggerIndex"] = 0 } };
    Check(validateOperation.Invoke(null, [TestChaosCard.Create([conditionOp, supportedPayoff]), 1, supportedPayoff]) is null,
        "supported conditional payoff still accepted");

    var evaluator = assembly.GetType("AutoAnthonyCombatSolverBridge.Translation.ConditionEvaluator")!;
    var evaluate = evaluator.GetMethod("Evaluate", BindingFlags.NonPublic | BindingFlags.Static)!;
    var local = new OperationResolutionState();
    var drawnCondition = conditionOp with { RuntimeSpec = conditionSpec with { Condition = new("last_drawn_card_is_skill", "self") } };
    bool DrawnIsSkill() => (bool)evaluate.Invoke(null, [null, drawnCondition, null, local])!;
    Check(!DrawnIsSkill(), "no draw is not one skill");
    var borrowed = new[] { CardType.Skill };
    local.RecordDrawnTypes(borrowed);
    borrowed[0] = CardType.Attack;
    Check(DrawnIsSkill(), "drawn types captured independently of borrowed array");
    Check(!new OperationResolutionState().LastDrawnCardIsSkill, "card plays do not share last draw");
    local.RecordDrawnTypes([CardType.Skill, CardType.Skill]);
    Check(!DrawnIsSkill(), "two skills do not satisfy exactly one drawn skill");
    local.RecordDrawnTypes([CardType.Attack]);
    Check(!DrawnIsSkill(), "one attack is not a skill");
    local.RecordDrawnTypes([]);
    Check(!DrawnIsSkill(), "failed or zero draw clears previous result");
    foreach (var kind in new[] { "fatal", "first_play_of_this_card_this_turn", "cards_played_this_turn_below",
                 "has_frost_orb", "energy_x_at_least", "cards_played_this_turn_at_least", "unknown_condition" })
    {
        var unsupportedCondition = conditionOp with { RuntimeSpec = conditionSpec with { Condition = new(kind, "self") } };
        Check(validateOperation.Invoke(null, [TestChaosCard.Create([unsupportedCondition]), 0, unsupportedCondition]) is not null,
            "unsupported condition rejected: " + kind);
        try
        {
            evaluate.Invoke(null, [null, unsupportedCondition, null, local]);
            throw new Exception("unsupported condition evaluated as a default: " + kind);
        }
        catch (TargetInvocationException e) when (e.InnerException is UnsupportedRuntimeSpecException) { checks++; }
    }

    var drawPolicy = assembly.GetType("AutoAnthonyCombatSolverBridge.CombatSolver.ChaosDrawEventPolicy")!
        .GetMethod("Events", BindingFlags.Static | BindingFlags.NonPublic)!;
    string[] DrawEvents(bool strike, bool ethereal, bool status, bool already, bool handDraw, bool ownerTurn)
        => (string[])drawPolicy.Invoke(null, [strike, ethereal, status, already, handDraw, ownerTurn])!;
    Check(DrawEvents(true, true, true, false, false, true).SequenceEqual(new[]
        { "strike_card_drawn", "card_drawn_during_turn", "card_drawn", "ethereal_card_drawn", "first_status_drawn_each_turn" }),
        "draw event filters and ordering match pinned AA hook");
    Check(DrawEvents(false, false, true, true, true, true).SequenceEqual(new[] { "card_drawn" }),
        "turn-start draws skip extra-draw and consumed first-status events");
    Check(DrawEvents(false, false, false, false, false, false).SequenceEqual(new[] { "card_drawn" }),
        "opponent-side draw skips owner-turn event");
    foreach (var kind in new[] { "strike_card_drawn", "card_drawn_during_turn", "ethereal_card_drawn", "first_status_drawn_each_turn" })
        Check(TriggerReason(Op(Spec("trigger", "event", "self", new(kind, "combat")))) is null,
            "wired draw trigger admitted: " + kind);
    foreach (var kind in new[] { "owner_hp_lost_during_turn", "energy_spent_this_turn_excluding_self", "card_generated",
                 "block_gained", "lightning_orb_evoked", "for_each_discarded_card", "self_exhausted", "attack_received" })
        Check(TriggerReason(Op(Spec("trigger", "event", "self", new(kind, "combat")))) is not null,
            "unwired trigger rejected: " + kind);
    foreach (var variant in new[] { "m_repeatperattackthisturn", "ncr_foreachexhaustedsoul", "d_foreachenemy",
                 "d_foreachorb", "ncr_foreachostyattackcard", "r_foreachstargainedthisturn", "r_foreachskillplayedthisturn" })
    {
        var modifier = Op(Spec("template_modifier", variant, "self"), "M:Fixture", OperationScope.Modifier);
        Check(validateOperation.Invoke(null, [TestChaosCard.Create([modifier]), 0, modifier]) is not null,
            "approximate or dependency modifier rejected: " + variant);
    }
    var gatedModifier = Op(Spec("modify_hits", "flat_extra", "self"), "M:Fixture", OperationScope.Modifier) with
    { Parameters = new Dictionary<string, int> { ["triggerIndex"] = 0 } };
    Check(validateOperation.Invoke(null, [TestChaosCard.Create([conditionOp, gatedModifier]), 1, gatedModifier]) is not null,
        "modifier cannot ignore condition gate");
    foreach (var (template, variant) in new[] { ("A:rule", "skills_cost_zero"), ("A:rule", "retain_block_between_turns"),
                 ("A:ruleRetainHand", "retain_hand_at_turn_end"), ("A:ProxyAtomic_ForbiddenGrimoire", "a_proxyatomic_forbiddengrimoire") })
    {
        var rule = Op(Spec("combat_rule", variant, "self"), template, OperationScope.AbilityRule);
        Check(validateOperation.Invoke(null, [TestChaosCard.Create([rule]), 0, rule]) is not null,
            "unmodeled rule rejected: " + variant);
    }
    foreach (var (template, variant) in new[] { ("A:ProxyAtomic_Buffer", "a_proxyatomic_buffer"),
                 ("A:ProxyAtomic_Calcify", "a_proxyatomic_calcify"), ("A:ProxyAtomic_Parry", "a_proxyatomic_parry"),
                 ("A:ProxyAtomic_Royalties", "a_proxyatomic_royalties"), ("A:ProxyAtomic_SwordSage", "a_proxyatomic_swordsage") })
    {
        var rule = Op(Spec("combat_rule", variant, "self"), template, OperationScope.AbilityRule);
        Check(validateOperation.Invoke(null, [TestChaosCard.Create([rule]), 0, rule]) is null,
            "standalone proxy rule admitted: " + variant);
    }

    foreach (var output in new[] { "lightning", "frost", "dark", "plasma", "glass", "random" })
        Check(OrbSlotCatalog.ResolveOutput(output, "I:ProxyAtomic_Tempest")?.Id == output,
            "installed Tempest output slot: " + output);
    Check(OrbSlotCatalog.ResolveOutput(null, "I:ProxyAtomic_Tempest")?.Id == "lightning",
        "legacy Tempest default output slot");
    var triggerAtoms = 0;
    var payloadAtoms = 0;
    var auditRows = new List<object>();
    var acceptedAtoms = 0;
    foreach (var atom in atoms)
    {
        var spec = OperationRuntimeSpecCompiler.GetOrCompile(atom);
        var op = new GeneratorOperation(atom.Template, atom.Scope, "ignored", new Dictionary<string, int>(), RuntimeSpec: spec);
        if (TriggerReason(op) is null) triggerAtoms++;
        if (PayloadReason(spec) is null) payloadAtoms++;
        var reason = (string?)validateOperation.Invoke(null, [TestChaosCard.Create([op]), 0, op]);
        if (reason is null) acceptedAtoms++;
        auditRows.Add(new { template = atom.Template, scope = atom.Scope.ToString(), opcode = spec.Opcode,
            variant = spec.Variant, acceptedAtomicShape = reason is null, rejectionReason = reason });
    }
    Check(triggerAtoms > 0 && payloadAtoms > 0, "installed catalog includes supported trigger and payload atoms");
    Console.WriteLine($"Catalog: {triggerAtoms} trigger atoms, {payloadAtoms} atomic payoff atoms (card-level validation still required)");
    Console.WriteLine($"Registry: {OperationHandlerRegistry.Instance.Count} keys; atomic validation: {acceptedAtoms}/{atoms.Length}");

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
    var statusFork = stateType.GetMethod("Fork")!.Invoke(state, [null])!;
    var statusSnapshot = (ChaosCompositePower)stateType.GetProperty("Snapshot")!.GetValue(statusFork)!;
    statusSnapshot.StatusDrawnThisTurn = true;
    stateType.GetMethod("Refresh")!.Invoke(statusFork, null);
    Check(!snapshot.StatusDrawnThisTurn, "first-status flag isolated across forks");
    Check(!rootVector.SequenceEqual((int[])stateType.GetProperty("FingerprintValues")!.GetValue(statusFork)!),
        "first-status flag affects fingerprint");
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
    power.StatusDrawnThisTurn = true;
    Check(firstStamp != Stamp(), "continuation detects first-status flag mismatch");
    power.StatusDrawnThisTurn = false;
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
    if (Environment.GetEnvironmentVariable("AA_BRIDGE_AUDIT_PATH") is { Length: > 0 } auditPath)
    {
        var audit = new { schema = 1, autoAnthonySha256 = Facts(aa).Sha256, combatSolverSha256 = Facts(solver).Sha256,
            registeredKeys = OperationHandlerRegistry.Instance.Count, catalogAtoms = atoms.Length, acceptedAtoms,
            note = "Singleton operation validation only. Whole-card dependencies, upgrades and live strict diff remain required.",
            rows = auditRows };
        File.WriteAllText(auditPath, System.Text.Json.JsonSerializer.Serialize(audit,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    Console.WriteLine($"PASS {checks} bridge contract / binary seam / hidden-state isolation checks");
}

sealed class TestChaosCard : ChaosCardModel
{
    private ChaosCardDefinition _fixture = null!;
    protected override int Slot => 0;
    protected override ChaosCardDefinition ResolveDefinition() => _fixture;

    public static TestChaosCard Create(GeneratorOperation[] operations)
    {
        var card = (TestChaosCard)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TestChaosCard));
        var generated = new GeneratedCard(1, GeneratedCardType.Skill, TargetMode.Other,
            GeneratedRarity.Common, "unused", [], operations);
        card._fixture = new(0, generated, "", "", "", "", "",
            operations.Select(operation => operation.RuntimeSpec!).ToArray());
        return card;
    }
}
