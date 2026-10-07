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
if (args.Contains("--write-compat-baseline"))
{
    var capture = typeof(CompatibilityGuard).GetMethod("CaptureBehaviorBaseline", BindingFlags.NonPublic | BindingFlags.Static)!;
    var solver = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("CombatSolver"));
    var baseline = capture.Invoke(null, [typeof(ChaosCompositePower).Assembly, solver]);
    File.WriteAllText(Environment.GetEnvironmentVariable("AA_BRIDGE_BASELINE_PATH") ?? throw new Exception("Baseline output path required"),
        System.Text.Json.JsonSerializer.Serialize(baseline, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Behavior baseline captured from installed original DLLs; not a compatibility test.");
}
else if (args.Contains("--audit-only")) AuditOnly();
else Run();

void AuditOnly()
{
    HandlerCatalog.RegisterAll(OperationHandlerRegistry.Instance);
    var assembly = typeof(CompatibilityGuard).Assembly;
    var validate = assembly.GetType("AutoAnthonyCombatSolverBridge.CombatSolver.ChaosCardOnPlayMirror")!
        .GetMethod("ValidateOperation", BindingFlags.NonPublic | BindingFlags.Static)!;
    var atoms = Enum.GetValues<GeneratedCharacter>().SelectMany(character => CharacterComponentCatalogs.Get(character).Atoms).ToArray();
    var rows = new List<object>();
    var accepted = 0;
    var completeShapes = 0;
    foreach (var atom in atoms)
    {
        var spec = OperationRuntimeSpecCompiler.GetOrCompile(atom);
        var operation = new GeneratorOperation(atom.Template, atom.Scope, "ignored", new Dictionary<string, int>(), RuntimeSpec: spec);
        string? reason;
        try { reason = (string?)validate.Invoke(null, [TestChaosCard.Create([operation]), 0, operation]); }
        catch (TargetInvocationException exception) { reason = "AUDIT_EXCEPTION: " + exception.InnerException; }
        if (reason is null) accepted++;
        var companionReason = reason;
        string? companionTemplate = null;
        if (reason is not null)
        {
            if (CardEffectRules.IsDependencyPrefix(operation))
            {
                foreach (var payoffAtom in atoms)
                {
                    var payoff = new GeneratorOperation(payoffAtom.Template, payoffAtom.Scope, "ignored", new Dictionary<string, int>(),
                        RuntimeSpec: OperationRuntimeSpecCompiler.GetOrCompile(payoffAtom));
                    if (!CardEffectRules.IsLegalDependencyPayoff(operation, payoff)) continue;
                    var fixture = TestChaosCard.Create([operation, payoff]);
                    var prefixReason = (string?)validate.Invoke(null, [fixture, 0, operation]);
                    var payoffReason = (string?)validate.Invoke(null, [fixture, 1, payoff]);
                    if (prefixReason is null && payoffReason is null)
                    { companionReason = null; companionTemplate = payoff.Template; break; }
                }
            }
            else if (spec.Opcode == "trigger" && spec.Trigger?.Lifetime == "immediate")
            {
                var trigger = operation with { CardTargetSlot = spec.Trigger.Kind == "self_exhausted" ? "thisCard" : operation.CardTargetSlot };
                var payoff = new GeneratorOperation("N:B", OperationScope.NonTargeted, "ignored",
                    new Dictionary<string, int> { ["triggerIndex"] = 0 },
                    RuntimeSpec: new OperationRuntimeSpec(1, "gain_block", "immediate", "self", "none", "none", "any", [], [new("block", 3)]));
                var fixture = TestChaosCard.Create([trigger, payoff]);
                var triggerReason = (string?)validate.Invoke(null, [fixture, 0, trigger]);
                var payoffReason = (string?)validate.Invoke(null, [fixture, 1, payoff]);
                if (triggerReason is null && payoffReason is null) { companionReason = null; companionTemplate = payoff.Template; }
            }
        }
        if (companionReason is null) completeShapes++;
        rows.Add(new { template = atom.Template, scope = atom.Scope.ToString(), opcode = spec.Opcode, variant = spec.Variant,
            acceptedAtomicShape = reason is null, rejectionReason = reason,
            acceptedWithRequiredCompanion = companionReason is null, companionTemplate, companionRejectionReason = companionReason });
    }
    var audit = new { schema = 1, registeredKeys = OperationHandlerRegistry.Instance.Count, catalogAtoms = atoms.Length,
        acceptedAtoms = accepted, acceptedCompleteShapes = completeShapes,
        note = "Implementation inventory only. A structural atom may require its legal companion; no contract checks or combat-equivalence tests executed.", rows };
    if (Environment.GetEnvironmentVariable("AA_BRIDGE_AUDIT_PATH") is { Length: > 0 } path)
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(audit, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"AUDIT ONLY {accepted}/{atoms.Length} singleton atoms; {completeShapes}/{atoms.Length} with required companions; {OperationHandlerRegistry.Instance.Count} registered keys; no tests executed");
}

void Run()
{
    HandlerCatalog.RegisterAll(OperationHandlerRegistry.Instance);
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
    var highCost = Op(Spec("trigger", "event", "self",
        new("energy_cost_at_least_card_played", "combat", ThresholdSlot: "threshold"),
        [new("threshold", 2)]), "A:whenEnergyCostAtLeast");
    Check(TriggerReason(highCost) is null, "resolved-cost trigger admitted");
    Check(TriggerReason(highCost with { RuntimeSpec = highCost.RuntimeSpec! with
        { Trigger = new("energy_cost_at_least_card_played", "this_turn", ThresholdSlot: "threshold") } }) is not null,
        "unwired high-cost lifetime rejected");
    Check(TriggerReason(highCost with { RuntimeSpec = highCost.RuntimeSpec! with
        { Trigger = new("energy_cost_at_least_card_played", "combat", ThresholdSlot: "unknown") } }) is not null,
        "unknown high-cost threshold slot rejected");
    Check(TriggerReason(highCost with { RuntimeSpec = highCost.RuntimeSpec! with
        { Values = [new("threshold", 0, "energy_x")] } }) is not null,
        "dynamic high-cost threshold rejected");
    Check(TriggerReason(delayed with { Parameters = new Dictionary<string, int> { ["triggerIndex"] = 0 } }) is not null,
        "nested trigger rejected");
    Check(TriggerReason(delayed with { RuntimeSpec = delayed.RuntimeSpec! with { Trigger = new("unknown", "next_turn") } }) is not null,
        "unknown trigger rejected");
    Check(TriggerReason(delayed with { RuntimeSpec = delayed.RuntimeSpec! with { Condition = new("unknown", "self") } }) is not null,
        "condition rejected");
    Check(TriggerReason(Op(Spec("trigger", "event", "self", new("card_played", "this_turn")))) is null,
        "temporary card-play lifetime admitted with expiry");
    foreach (var tuple in new[] { ("gain_block", "immediate", "self"), ("gain_energy", "immediate", "self"),
                 ("heal", "immediate", "self"), ("deal_damage", "all", "all_enemies"), ("deal_damage", "random", "random_enemy") })
        Check(PayloadReason(Spec(tuple.Item1, tuple.Item2, tuple.Item3)) is null, "accepted payload " + tuple.Item1);
    Check(PayloadReason(Spec("draw_cards", "immediate", "self")) is null, "triggered draw has an executable mirror");
    Check(PayloadReason(Spec("deal_damage", "selected", "selected_enemy")) is null, "selected triggered target has stored/fallback resolution");
    Check(PayloadReason(Spec("gain_block", "immediate", "self", values: [new("block", 0, "energy_x")])) is null,
        "triggered energy-X uses captured source values");
    Check(PayloadReason(Spec("gain_block", "immediate", "self", values: [new("block", 0, "unknown")])) is not null,
        "unknown triggered value source rejected");
    Check(PayloadReason(Spec("gain_block", "immediate", "self", flags: ["unknown"])) is not null, "unknown flag rejected");
    var atoms = Enum.GetValues<GeneratedCharacter>().SelectMany(character => CharacterComponentCatalogs.Get(character).Atoms).ToArray();
    var independent = new AutoAnthonyCombatSolverBridge.Translation.Handlers.TemplateIndependentActionHandler();
    var unsupportedIndependent = new[] { "unknown_independent" };
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
    var selfExhaust = Op(Spec("trigger", "event", "self", new("self_exhausted", "immediate")),
        "C:after", OperationScope.ConditionalTrigger) with { CardTargetSlot = "thisCard" };
    var exhaustMirror = assembly.GetType("AutoAnthonyCombatSolverBridge.CombatSolver.ChaosCardExhaustMirror")!;
    var isSelf = exhaustMirror.GetMethod("IsTrigger", BindingFlags.NonPublic | BindingFlags.Static)!;
    Check((bool)isSelf.Invoke(null, [selfExhaust])!, "self-exhaust event identity admitted");
    Check(!(bool)isSelf.Invoke(null, [selfExhaust with { CardTargetSlot = null }])!, "self-exhaust requires thisCard slot");
    Check(!(bool)isSelf.Invoke(null, [selfExhaust with { RuntimeSpec = selfExhaust.RuntimeSpec! with
        { Trigger = new("turn_end_if_self_in_exhaust", "immediate") } }])!, "exhaust-pile turn event is not self-exhaust");
    Check(validateOperation.Invoke(null, [TestChaosCard.Create([selfExhaust, supportedPayoff]), 0, selfExhaust]) is null,
        "self-exhaust accepts bounded linked payoff");
    Check(validateOperation.Invoke(null, [TestChaosCard.Create([selfExhaust, supportedPayoff]), 1, supportedPayoff]) is null,
        "self-exhaust linked payoff accepted by card validation");
    Check(validateOperation.Invoke(null, [TestChaosCard.Create([selfExhaust]), 0, selfExhaust]) is not null,
        "self-exhaust without payoff rejected");
    var drawPayoff = supportedPayoff with { RuntimeSpec = Spec("draw_cards", "immediate", "self") };
    Check(validateOperation.Invoke(null, [TestChaosCard.Create([selfExhaust, drawPayoff]), 0, selfExhaust]) is null,
        "self-exhaust draw payoff admitted with full-action choice replay");
    var badLifetime = selfExhaust with { RuntimeSpec = selfExhaust.RuntimeSpec! with { Trigger = new("self_exhausted", "combat") } };
    Check(validateOperation.Invoke(null, [TestChaosCard.Create([badLifetime, supportedPayoff]), 0, badLifetime]) is not null,
        "self-exhaust combat lifetime rejected");

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
    foreach (var kind in new[] { "fatal", "has_frost_orb", "cards_played_this_turn_at_least",
                 "first_play_of_this_card_this_turn", "cards_played_this_turn_below", "energy_x_at_least" })
    {
        var supportedCondition = conditionOp with { RuntimeSpec = conditionSpec with { Condition = new(kind, "self") } };
        Check(validateOperation.Invoke(null, [TestChaosCard.Create([supportedCondition]), 0, supportedCondition]) is null,
            "Batch AP condition admitted: " + kind);
    }
    foreach (var kind in new[] { "unknown_condition" })
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
    foreach (var kind in new[] { "owner_hp_lost_during_turn", "card_generated", "block_gained", "lightning_orb_evoked", "attack_received",
                 "vulnerable_applied", "doom_applied", "enemy_debuff_applied", "energy_spent_threshold", "stars_spent_threshold" })
        Check(TriggerReason(Op(Spec("trigger", "event", "self", new(kind, "combat")))) is null,
            "wired event trigger admitted: " + kind);
    foreach (var kind in new[] { "energy_spent_this_turn_excluding_self", "for_each_discarded_card", "self_exhausted" })
        Check(TriggerReason(Op(Spec("trigger", "event", "self", new(kind, "combat")))) is not null,
            "unwired trigger rejected: " + kind);
    foreach (var variant in new[] { "ncr_foreachexhaustedsoul", "d_foreachorb", "ncr_foreachostyattackcard",
                 "r_foreachskillplayedthisturn" })
    {
        var modifier = Op(Spec("template_modifier", variant, "self"), "M:Fixture", OperationScope.Modifier);
        Check(validateOperation.Invoke(null, [TestChaosCard.Create([modifier]), 0, modifier]) is null,
            "Batch AO modifier admitted: " + variant);
    }
    foreach (var variant in new[] { "d_foreachenemy", "r_foreachstargainedthisturn" })
    {
        var modifier = Op(Spec("template_modifier", variant, "self"), "M:Fixture", OperationScope.Modifier);
        Check(validateOperation.Invoke(null, [TestChaosCard.Create([modifier]), 0, modifier]) is not null,
            "approximate or dependency modifier rejected: " + variant);
    }
    var gatedModifier = Op(Spec("modify_hits", "flat_extra", "self"), "M:Fixture", OperationScope.Modifier) with
    { Parameters = new Dictionary<string, int> { ["triggerIndex"] = 0 } };
    Check(validateOperation.Invoke(null, [TestChaosCard.Create([conditionOp, gatedModifier]), 1, gatedModifier]) is null,
        "conditional modifier admitted with explicit gate evaluation");
    foreach (var (template, variant) in new[] { ("A:rule", "skills_cost_zero"), ("A:rule", "retain_block_between_turns"),
                 ("A:ruleRetainHand", "retain_hand_at_turn_end"), ("A:ProxyAtomic_ForbiddenGrimoire", "a_proxyatomic_forbiddengrimoire") })
    {
        var rule = Op(Spec("combat_rule", variant, "self"), template, OperationScope.AbilityRule);
        Check(validateOperation.Invoke(null, [TestChaosCard.Create([rule]), 0, rule]) is null,
            "implemented combat rule admitted: " + variant);
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
    var discardCount = typeof(ChaosCompositePower).Assembly.GetType("AutoAnthony.ChaosOperationExecutor")!
        .GetMethod("ExecutableDerivativeDiscardCount", BindingFlags.Static | BindingFlags.NonPublic)!;
    int StatusDiscardCount(string template, int amount, RuntimeValueSlot[]? values = null)
        => (int)discardCount.Invoke(null, [Op(Spec("template_self_action", "d_createburnindiscard", "self", values: values), template), amount])!;
    Check(StatusDiscardCount("D:CreateBurnInDiscard", 0) == 1, "unvalued discard derivative produces one card rather than zero");
    Check(StatusDiscardCount("D:CreateTwoWoundsInDiscard", 0) == 2, "legacy two-wound derivative has two-card default");
    Check(StatusDiscardCount("D:CreateBurnInDiscard", 0, [new("amount", 0, Source: "energy_x")]) == 0,
        "explicit zero X derivative does not use missing-value default");
    Check(StatusDiscardCount("D:CreateBurnInDiscard", 0, [new("amount", 3)]) == 3,
        "fixed derivative fallback preserves printed count");
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
    var predictionCardType = solver.GetType("CombatSolver.Engine.Common.PredictedCard")!;
    var sourceCard = TestChaosCard.Create([supportedPayoff]);
    var forkCard = TestChaosCard.Create([supportedPayoff]);
    var sourcePrediction = Activator.CreateInstance(predictionCardType, [sourceCard, null])!;
    var forkPrediction = Activator.CreateInstance(predictionCardType, [forkCard, null])!;
    var forkContextType = solver.GetType("CombatSolver.Engine.Common.PredictionForkContext")!;
    using (var forkContext = (IDisposable)Activator.CreateInstance(forkContextType)!)
    {
        forkContextType.GetMethod("Register")!.MakeGenericMethod(predictionCardType).Invoke(forkContext, [sourcePrediction, forkPrediction]);
        var resolution = new OperationResolutionState { LastAttackKilled = true, LastDamageDealt = 13, PriorAttackHitsOnTargetAtPlayStart = 4 };
        var selections = (System.Collections.IDictionary)typeof(OperationResolutionState).GetProperty("CardSelections")!.GetValue(resolution)!;
        var selectedList = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(predictionCardType))!;
        selectedList.Add(sourcePrediction);
        selections.Add("card0", selectedList);
        typeof(OperationResolutionState).GetProperty("LastMovedCard")!.SetValue(resolution, sourcePrediction);
        var forkLocal = (OperationResolutionState)typeof(OperationResolutionState).GetMethod("Fork", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(resolution, [forkContext])!;
        var forkSelections = (System.Collections.IDictionary)typeof(OperationResolutionState).GetProperty("CardSelections")!.GetValue(forkLocal)!;
        var remapped = (System.Collections.IList)forkSelections["card0"]!;
        Check(ReferenceEquals(remapped[0], forkPrediction), "operation slots remap to the sibling's predicted card");
        Check(ReferenceEquals(typeof(OperationResolutionState).GetProperty("LastMovedCard")!.GetValue(forkLocal), forkPrediction),
            "last-moved provenance remaps on fork");
        remapped.Clear();
        forkLocal.LastDamageDealt = 99;
        Check(selectedList.Count == 1 && resolution.LastDamageDealt == 13, "choice slots and local damage state are fork isolated");
        Check(forkLocal.PriorAttackHitsOnTargetAtPlayStart == 4 && forkLocal.LastAttackKilled, "fork preserves action-start hit provenance and fatal state");
    }
    var aa = typeof(ChaosCompositePower).Assembly;
    static BridgeModFacts Facts(Assembly target) => new(target.GetName().Name!, target.Location, null, null,
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(target.Location))).ToLowerInvariant(),
        null, null, [], []);
    var seams = typeof(CompatibilityGuard).GetMethod("VerifyCompositeSeams", BindingFlags.NonPublic | BindingFlags.Static)!;
    var failures = new List<string>();
    seams.Invoke(null, [Facts(aa), Facts(solver), solver, failures]);
    Check(failures.Count == 0, "installed binary seams: " + string.Join("; ", failures));
    seams.Invoke(null, [Facts(aa), Facts(solver) with { Sha256 = "wrong" }, solver, failures]);
    Check(failures.Count == 0, "changed solver package hash with unchanged behavior remains compatible: " + string.Join("; ", failures));
    seams.Invoke(null, [Facts(aa) with { Sha256 = "different" }, Facts(solver), solver, failures]);
    Check(failures.Count == 0, "changed AA package hash with unchanged behavior remains compatible");
    var behavior = assembly.GetType("AutoAnthonyCombatSolverBridge.Bootstrap.BehaviorContract")!;
    var compare = behavior.GetMethod("Compare", BindingFlags.NonPublic | BindingFlags.Static)!;
    var expected = new Dictionary<string, string> { ["Example::Method"] = "baseline" };
    compare.Invoke(null, [expected, new Dictionary<string, string> { ["Example::Method"] = "changed" }, "fixture", failures]);
    Check(failures.Count == 1, "changed adapted method behavior fails closed");
    failures.Clear();
    compare.Invoke(null, [expected, new Dictionary<string, string>(), "fixture", failures]);
    Check(failures.Count == 1, "missing adapted method fails closed");
    failures.Clear();
    compare.Invoke(null, [expected, new Dictionary<string, string> { ["Example::Method"] = "baseline", ["Unrelated::New"] = "extra" }, "fixture", failures]);
    Check(failures.Count == 0, "unrelated added methods do not invalidate behavior contract");
    var fingerprint = behavior.GetMethod("Fingerprint", BindingFlags.NonPublic | BindingFlags.Static)!;
    static MethodInfo EmitBehavior(bool extra, string value)
    {
        var module = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()),
            System.Reflection.Emit.AssemblyBuilderAccess.Run).DefineDynamicModule("test");
        var type = module.DefineType("Fixture", TypeAttributes.Public);
        if (extra)
        {
            var other = type.DefineMethod("Unrelated", MethodAttributes.Public | MethodAttributes.Static, typeof(string), Type.EmptyTypes).GetILGenerator();
            other.Emit(System.Reflection.Emit.OpCodes.Ldstr, "unrelated token");
            other.Emit(System.Reflection.Emit.OpCodes.Ret);
        }
        var il = type.DefineMethod("Adapted", MethodAttributes.Public | MethodAttributes.Static, typeof(string), Type.EmptyTypes).GetILGenerator();
        il.Emit(System.Reflection.Emit.OpCodes.Ldstr, value);
        il.Emit(System.Reflection.Emit.OpCodes.Ret);
        return type.CreateType()!.GetMethod("Adapted")!;
    }
    var originalBehavior = fingerprint.Invoke(null, [EmitBehavior(false, "same")]);
    Check(Equals(originalBehavior, fingerprint.Invoke(null, [EmitBehavior(true, "same")])), "metadata token shifts preserve semantic IL fingerprint");
    Check(!Equals(originalBehavior, fingerprint.Invoke(null, [EmitBehavior(true, "changed")])), "changed actual IL operand changes fingerprint");

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
    var effectiveAmount = typeof(ChaosCompositePower).GetMethod("EffectiveOperationAmount", BindingFlags.Instance | BindingFlags.NonPublic)!;
    Check((int)effectiveAmount.Invoke(snapshot, [1, 2])! == 7,
        "high-cost threshold uses captured operation value instead of fallback");
    Check((int)effectiveAmount.Invoke(forkSnapshot, [1, 2])! == 99,
        "high-cost threshold reads isolated branch snapshot");
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
    var choiceMirror = assembly.GetType("AutoAnthonyCombatSolverBridge.CombatSolver.ChaosCardChoiceMirror")!;
    var otherPools = choiceMirror.GetMethod("OtherCharacterPools", BindingFlags.NonPublic | BindingFlags.Static)!;
    var ownPool = (MegaCrit.Sts2.Core.Models.CardPoolModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
        typeof(MegaCrit.Sts2.Core.Models.CardPools.NecrobinderCardPool));
    var firstOtherPool = (MegaCrit.Sts2.Core.Models.CardPoolModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
        typeof(MegaCrit.Sts2.Core.Models.CardPools.IroncladCardPool));
    var secondOtherPool = (MegaCrit.Sts2.Core.Models.CardPoolModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
        typeof(MegaCrit.Sts2.Core.Models.CardPools.SilentCardPool));
    var unlockedPools = new[] { firstOtherPool, ownPool, secondOtherPool };
    var splashPools = (IReadOnlyList<MegaCrit.Sts2.Core.Models.CardPoolModel>)otherPools.Invoke(null, [unlockedPools, ownPool])!;
    Check(splashPools.Count == 2 && ReferenceEquals(splashPools[0], firstOtherPool) && ReferenceEquals(splashPools[1], secondOtherPool),
        "Splash excludes current character while preserving other unlocked pool order");
    Check(unlockedPools.Length == 3 && ReferenceEquals(unlockedPools[1], ownPool), "Splash does not mutate the shared unlock pool list");
    var fallbackPool = (IReadOnlyList<MegaCrit.Sts2.Core.Models.CardPoolModel>)otherPools.Invoke(null, [new[] { ownPool }, ownPool])!;
    Check(fallbackPool.Count == 1 && ReferenceEquals(fallbackPool[0], ownPool), "Splash matches AA single unlocked character fallback");
    var attackMultiplier = mirror.GetMethod("TriggeredAttackDamageMultiplier", BindingFlags.NonPublic | BindingFlags.Static)!;
    var effectiveOperationsCache = typeof(ChaosCompositePower).GetField("_cachedEffectivePowerOperations", BindingFlags.NonPublic | BindingFlags.Instance)!;
    Check(snapshot.Owner is null, "saved composite snapshot is detached from its owner");
    effectiveOperationsCache.SetValue(snapshot, new[] { Op(Spec("trigger", "event", "self", new("derivative_played", "combat")), "A:whenSoulPlayed") });
    Check((decimal)attackMultiplier.Invoke(null, [state, null])! == 1m,
        "detached unrelated composite does not query attack history or owner during damage");
    var percentTrigger = Op(Spec("trigger", "event", "self", new("attack_played", "combat")), "A:whenAttackPlayed");
    var percentModifier = new GeneratorOperation("M:TriggeredAttackDamagePercent", OperationScope.Modifier, "unused",
        new Dictionary<string, int> { ["triggerIndex"] = 0 }, RuntimeSpec: Spec("template_modifier", "triggered_attack_damage_percent", "self"));
    effectiveOperationsCache.SetValue(snapshot, new[] { percentTrigger, percentModifier });
    Check((decimal)attackMultiplier.Invoke(null, [state, null])! == 1.07m,
        "unconditional attack percent uses captured values without detached owner or unnecessary history");
    effectiveOperationsCache.SetValue(snapshot, null);
    mirror.GetMethod("Register")!.Invoke(null, null);
    var beforeRegistry = solver.GetType("CombatSolver.Engine.InCombat.Mirrors.Hooks.Card.BeforeCardPlayedMirrors")!
        .GetField("Registry", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    Check((bool)beforeRegistry.GetType().GetMethod("HasRegisteredHandler")!.Invoke(beforeRegistry, [power])!,
        "BeforeCardPlayed installs an executable handler instead of an ignored override");
    var registrar = assembly.GetType("AutoAnthonyCombatSolverBridge.CombatSolver.CombatSolverRegistrar")!;
    var concreteCard = aa.GetTypes().First(type => !type.IsAbstract && typeof(ChaosCardModel).IsAssignableFrom(type));
    registrar.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(method => method.Name == "RegisterMirrorsForType").Invoke(null, [concreteCard]);
    var locationRegistry = solver.GetType("CombatSolver.Engine.InCombat.Mirrors.Cards.CardResultLocationMirrors")!
        .GetField("Registry", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    var locationReceiver = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(concreteCard);
    Check(((System.Collections.IDictionary)locationRegistry.GetType().GetField("_registrations", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(locationRegistry)!).Contains(concreteCard),
        "concrete Chaos card installs its actual post-play result-location override");
    var passiveType = assembly.GetType("AutoAnthonyCombatSolverBridge.CombatSolver.ChaosCardPassiveMirror")!;
    var resultLocation = passiveType.GetMethod("ResultLocation", BindingFlags.Static | BindingFlags.NonPublic)!;
    TestChaosCard MovementCard(params string[] templates)
    {
        var fixture = TestChaosCard.Create(templates.Select(template => Op(Spec("template_independent_action", "unused", "self"), template)).ToArray());
        typeof(MegaCrit.Sts2.Core.Models.AbstractModel).GetProperty("IsMutable")!.SetValue(fixture, true);
        return fixture;
    }
    var drawMovement = MovementCard("R:PutThisOnDraw");
    var returnMovement = MovementCard("R:PutThisOnDraw", "R:ReturnThisToHand");
    MegaCrit.Sts2.Core.Entities.Cards.CardLocation ResolveMovement(TestChaosCard fixture, MegaCrit.Sts2.Core.Entities.Cards.PileType pile)
        => (MegaCrit.Sts2.Core.Entities.Cards.CardLocation)resultLocation.Invoke(null,
            [fixture, new MegaCrit.Sts2.Core.Entities.Cards.CardLocation(null!, pile, MegaCrit.Sts2.Core.Entities.Cards.CardPilePosition.Bottom)])!;
    var drawResult = ResolveMovement(drawMovement, MegaCrit.Sts2.Core.Entities.Cards.PileType.Discard);
    Check(drawResult.pileType == MegaCrit.Sts2.Core.Entities.Cards.PileType.Draw && drawResult.position == MegaCrit.Sts2.Core.Entities.Cards.CardPilePosition.Top,
        "PutThisOnDraw returns played card to draw top before next-turn draw");
    Check(ResolveMovement(returnMovement, MegaCrit.Sts2.Core.Entities.Cards.PileType.Discard).pileType == MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand,
        "return to hand takes precedence over draw movement exactly as AA");
    var moveAfterExhaust = typeof(ChaosCardModel).GetField("_postPlayExhaustMovePile", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    Check(ResolveMovement(drawMovement, MegaCrit.Sts2.Core.Entities.Cards.PileType.Exhaust).pileType == MegaCrit.Sts2.Core.Entities.Cards.PileType.Exhaust
        && Equals(moveAfterExhaust.GetValue(drawMovement), MegaCrit.Sts2.Core.Entities.Cards.PileType.Draw), "exhaust event preserved before draw-top movement");
    Check(ResolveMovement(drawMovement, MegaCrit.Sts2.Core.Entities.Cards.PileType.None).pileType == MegaCrit.Sts2.Core.Entities.Cards.PileType.None
        && moveAfterExhaust.GetValue(drawMovement) is null, "none result stays removed and clears stale post-exhaust movement");
    var deathRegistry = solver.GetType("CombatSolver.Engine.InCombat.Mirrors.Hooks.Death.AfterDeathMirrors")!
        .GetField("Registry", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    var cardReceiver = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(concreteCard);
    var exhaustRegistry = solver.GetType("CombatSolver.Engine.InCombat.Mirrors.Hooks.Card.AfterCardExhaustedMirrors")!
        .GetField("Registry", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    Check((bool)exhaustRegistry.GetType().GetMethod("HasRegisteredHandler")!.Invoke(exhaustRegistry, [cardReceiver])!,
        "concrete Chaos card has registered exhaust movement rather than an unmirrored override");
    Check((bool)deathRegistry.GetType().GetMethod("HasRegisteredHandler")!.Invoke(deathRegistry, [cardReceiver])!,
        "concrete Chaos card has actual AfterDeath cost handler");
    var deathContextType = solver.GetType("CombatSolver.Engine.InCombat.Mirrors.Hooks.Death.AfterDeathMirrorContext")!;
    var deathContext = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(deathContextType);
    deathContextType.GetProperty("WasRemovalPrevented")!.SetValue(deathContext, true);
    registrar.GetMethod("AfterDeath", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [cardReceiver, deathContext]);
    Check(true, "prevented removal does not touch card or branch state");
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
            method.DeclaringType?.FullName == "CombatSolver.Engine.InCombat.Mirrors.HookMirrors" && method.Name == "AfterCardExhausted"),
            "self-exhaust seam patches solver global event");
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
