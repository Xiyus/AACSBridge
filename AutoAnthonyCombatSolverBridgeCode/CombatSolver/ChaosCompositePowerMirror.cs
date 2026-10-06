using System.Reflection;
using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.Common.Mirrors;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Block;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Damage;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.TurnEnd;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using AutoAnthonyCombatSolverBridge.Translation;
using AutoAnthonyCombatSolverBridge.Bootstrap;

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

internal static class ChaosCompositePowerMirror
{
    [ThreadStatic] private static int _triggerDepth;
    private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Register()
    {
        PowerDynamicVarWarmup.RegisterAdaptedCanonicalPower<ChaosCompositePower>();
        ModifyBlockMultiplicativeMirrors.Registry.Register<ChaosCompositePower>((_, _) => 1m);
        ModifyDamageMirrors.AdditiveRegistry.Register<ChaosCompositePower>((_, _) => 0m);
        ModifyDamageMirrors.MultiplicativeRegistry.Register<ChaosCompositePower>((_, _) => 1m);
        ModifyCardPlayCountMirrors.Registry.Register<ChaosCompositePower>((_, c) => c.PlayCount);
        ModifyCardPlayResultLocationMirrors.Registry.Register<ChaosCompositePower>((_, c) => c.Location);
        ModifyEnergyCostInCombatMirrors.LateRegistry.Register<ChaosCompositePower>((_, c) => c.Cost);
        PowerHiddenStateMirrors.RegisterRootCapture<ChaosCompositePower>((simulator, clone, live) =>
        {
            ValidatePower(live, simulator);
            simulator.StateStore.GetReadOnly(clone, () => new ChaosCompositePredictionState(live));
        });
        // Fixed part of AA's schema 5 checksum, plus full-width hashes of variable payloads.
        for (var index = 0; index < 34; index++)
        {
            var capturedIndex = index;
            PowerHiddenStateMirrors.Register<ChaosCompositePower>($"aa6.state.{index:D2}",
                (simulator, power) => Read(simulator, power).FingerprintValues[capturedIndex]);
        }
        PowerHiddenStateMirrors.Register<ChaosCompositePower>("aa6.values", (s, p) => Read(s, p).CapturedValuesHash);
        PowerHiddenStateMirrors.Register<ChaosCompositePower>("aa6.definition", (s, p) => Read(s, p).DefinitionHash);
        PowerHiddenStateMirrors.Register<ChaosCompositePower>("aa6.profile", (s, p) => Read(s, p).ProfileHash);
        PowerHiddenStateMirrors.Register<ChaosCompositePower>("aa6.budget", (s, p) =>
        {
            // Conservatively bounded between input/turn boundaries; include it in search equivalence.
            long hash = 0;
            foreach (var entry in Read(s, p).Activations)
                hash = unchecked(hash + (entry.Key * 397L ^ entry.Value));
            return hash;
        });
        AfterCardPlayedMirrors.Registry.Register<ChaosCompositePower>(AfterPlayed);
        AfterCardDrawnMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.PreviewCard.Owner != p.Owner.Player) return;
            Fire(p, c.Simulator, "card_drawn");
            if (c.PreviewCard.Type == CardType.Status)
            {
                var state = Read(c.Simulator, p);
                state.Snapshot.StatusDrawnThisTurn = true;
                state.Refresh();
            }
        });
        AfterCardExhaustedMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.PreviewCard.Owner == p.Owner.Player) Fire(p, c.Simulator, "card_exhausted");
        });
        AfterAutoPostPlayPhaseEnteredMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.Player.Creature == p.Owner) Fire(p, c.Simulator, "turn_end");
        });
        RegisterNeutralActionHooks();
    }

    // These overrides cannot execute an effect in our bounded contract. No AbilityRule,
    // resource/damage/orb/generation triggers or special play modifiers are admitted.
    // An unknown Power definition is rejected at root capture, before these registrations dispatch.
    private static readonly HashSet<string> NeutralActions = new(StringComparer.Ordinal)
    {
        "AfterApplied", "AfterCardEnteredCombat", "AfterPlayerTurnStart", "AfterAutoPrePlayPhaseEntered",
        "BeforeSideTurnEnd", "AfterBlockGained", "AfterCurrentHpChanged", "AfterPowerAmountChanged",
        "AfterCardGeneratedForCombat", "AfterOrbChanneled", "AfterOrbEvoked", "AfterStarsSpent",
        "AfterEnergySpent", "AfterStarsGained", "AfterDamageGiven", "AfterModifyingCardPlayCount",
        "BeforeCardPlayed", "AfterDamageReceived", "AfterShuffle"
    };

    private static void RegisterNeutralActionHooks()
    {
        foreach (var type in typeof(PowerHiddenStateMirrors).Assembly.GetTypes()
                     .Where(t => t.Namespace?.StartsWith("CombatSolver.Engine.InCombat.Mirrors", StringComparison.Ordinal) == true
                                 && t.IsAbstract && t.IsSealed && !t.ContainsGenericParameters))
        foreach (var field in type.GetFields(StaticFields))
        {
            if (!field.FieldType.IsGenericType
                || field.FieldType.GetGenericTypeDefinition() != typeof(MethodMirrorRegistry<,>)) continue;
            if (field.GetValue(null) is not IMethodMirrorRegistryDescriptorProvider provider) continue;
            var descriptor = provider.DescribeMirrorSupport();
            if (!NeutralActions.Contains(descriptor.BaseMethod.Name)
                || !descriptor.ReceiverType.IsAssignableFrom(typeof(ChaosCompositePower))) continue;
            var method = typeof(ChaosCompositePower).GetMethod(descriptor.BaseMethod.Name,
                descriptor.BaseMethod.GetParameters().Select(p => p.ParameterType).ToArray());
            if (method?.DeclaringType != typeof(ChaosCompositePower)) continue;
            field.FieldType.GetMethod("RegisterIgnored", [typeof(Type)])!
                .Invoke(provider, [typeof(ChaosCompositePower)]);
        }
    }

    internal static ChaosCompositePredictionState Read(CombatPredictionSimulator simulator, ChaosCompositePower power)
    {
        if (simulator.StateStore.TryGetReadOnly<ChaosCompositePredictionState>(power, out var existing))
            return existing!;
        ValidatePower(power, simulator);
        return simulator.StateStore.GetReadOnly(power, static p => new ChaosCompositePredictionState(p));
    }

    internal static void ResetBudget(CombatPredictionSimulator simulator)
    {
        if (simulator.State.CombatState is not SimulatedCombatState combat) return;
        foreach (var power in combat.EffectivePowers().OfType<ChaosCompositePower>())
        {
            var state = Read(simulator, power);
            if (state.ActiveTriggers.Count > 0) throw Unsupported("触发收益内出现未适配的输入边界");
            state.Activations.Clear();
        }
    }

    private static PredictedCard Source(ChaosCompositePower power)
    {
        if (power.ProfileId.Length != 0)
            throw Unsupported("外部角色 Profile 的复合 Power 尚未适配");
        // 模拟分支中的克隆 Creature 可能没有 Player 引用（例如根捕获时来自非玩家侧的
        // Power 克隆）——fail-closed 而不是 NullReference 崩溃
        if (power.Owner?.Player is null)
            throw Unsupported("复合 Power 的 Owner Creature 缺少 Player 引用（模拟克隆边界）");
        var predicted = PredictedCard.Create(ChaosCardRegistry.Canonical(power.Character, power.Slot), power.Owner.Player);
        var card = (ChaosCardModel)predicted.MutablePreview;
        if (power.SourceTinkeredDefinitionPayload.Length > 0)
            card.ApplyCapturedDefinition(CardTinkeringApi.DeserializeCard(power.SourceTinkeredDefinitionPayload));
        card.ResolvedSpecialXValue = power.SpecialXValue;
        card.SetResolvedXValues(power.ResolvedEnergyXValue, power.ResolvedStarXValue);
        if (power.SourceUpgraded && card.IsUpgradable) PredictionUtils.UpgradeCard(card);
        card.ApplyCapturedOperationValues(power.CapturedOperationValues);
        return predicted;
    }

    private static void ValidatePower(ChaosCompositePower power, CombatPredictionSimulator simulator)
    {
        if (!BridgeBootstrap.IsReady) throw Unsupported("桥未完成全部初始化，拒绝部分适配预测");
        if (power.Owner.Player is null) throw Unsupported("复合 Power Owner 不是玩家");
        var card = (ChaosCardModel)Source(power).MutablePreview;
        var reason = ChaosCardOnPlayMirror.ValidateCard(card);
        if (reason is not null) throw Unsupported(reason);
        if (!card.Generated.Operations.Any(ChaosOperationExecutor.RequiresCompositePower))
            throw Unsupported("复合 Power 定义没有受支持触发器");
        AssertStartOrder(simulator, power);
    }

    public static void Arm(ChaosCardModel card, CardOnPlayMirrorContext context)
    {
        if (!card.Generated.Operations.Any(ChaosOperationExecutor.RequiresCompositePower)) return;
        var combat = (SimulatedCombatState)context.CombatState;
        var power = (ChaosCompositePower)ModelDb.Power<ChaosCompositePower>().ToMutable();
        var deckIndex = card.DeckVersion is { } deck
            ? card.Owner.Deck.Cards.ToList().FindIndex(c => ReferenceEquals(c, deck)) : -1;
        power.ConfigureTinkered(card.Generated.Character, card.Definition.Slot, card.IsUpgraded,
            card.Type == CardType.Power, card.ResolvedSpecialXValue, card.ResolvedEnergyXValue,
            card.ResolvedStarXValue, card.CaptureOperationValuesForPower(), context.CardPlay.Target?.CombatId,
            card.RuntimeProfileId, card.EffectiveDefinitionPayload, deckIndex);
        AssertStartOrder(context.Simulator, power);
        combat.BeginCardPowerApplication(card);
        try { combat.ApplyClonedPower(power, card.Owner.Creature, 1, card.Owner.Creature); }
        finally { combat.CompleteCardPowerApplication(card); }
    }

    private static void AfterPlayed(ChaosCompositePower power, AfterCardPlayedMirrorContext context)
    {
        if (context.PreviewCard.Owner != power.Owner.Player) return;
        var state = Read(context.Simulator, power);
        if (state.Snapshot.IgnoreArmingCardPlay)
        {
            state.Snapshot.IgnoreArmingCardPlay = false;
            state.Refresh();
            return;
        }
        Fire(power, context.Simulator, "card_played", context.CardPlay);
        if (context.PreviewCard.Type == CardType.Attack)
        {
            state.Snapshot.AttacksPlayedThisTurn = ((SimulatedCombatState)context.CombatState)
                .GetAttacksPlayedThisTurn(power.Owner) + 1;
            state.Refresh();
        }
        var kind = context.PreviewCard.Type switch
        {
            CardType.Attack => "attack_played",
            CardType.Skill => "skill_played",
            CardType.Power => "power_played",
            _ => null
        };
        if (kind is not null) Fire(power, context.Simulator, kind, context.CardPlay);
    }

    internal static void Start(CombatPredictionSimulator simulator, SimulatedCombatState combat,
        CombatSide side, IReadOnlyList<Creature> participants)
    {
        foreach (var power in combat.EffectivePowers().OfType<ChaosCompositePower>().ToArray())
        {
            if (power.Owner.Side != side || !participants.Contains(power.Owner)) continue;
            AssertStartOrder(simulator, power);
            var state = Read(simulator, power);
            var snapshot = state.Snapshot;
            snapshot.AttacksPlayedThisTurn = 0;
            snapshot.StatusDrawnThisTurn = false;
            snapshot.CardsPlayedTowardTrigger = 0;
            snapshot.FirstAttackOrSkillAvailable = false;
            snapshot.FirstCardReplayAvailable = false;
            snapshot.ZeroCostAttackReturnAvailable = false;
            if (snapshot.RemainingTurnTriggers > 0)
            {
                Fire(power, simulator, "next_turns_start");
                snapshot.RemainingTurnTriggers--;
                RemoveIfFinished(power, simulator, snapshot);
            }
            if (snapshot.WaitForNextTurn)
            {
                snapshot.WaitForNextTurn = false;
                Fire(power, simulator, "next_turn_start");
                RemoveIfFinished(power, simulator, snapshot);
            }
            if (snapshot.Permanent) Fire(power, simulator, "turn_start");
            state.Refresh();
        }
    }

    internal static void End(CombatPredictionSimulator simulator, SimulatedCombatState combat,
        CombatSide side, IEnumerable<Creature> participants)
    {
        var owners = participants.ToHashSet();
        foreach (var power in combat.EffectivePowers().OfType<ChaosCompositePower>().ToArray())
        {
            var state = Read(simulator, power);
            if (power.Owner.Side == side)
            {
                if (!owners.Contains(power.Owner)) continue;
                state.Snapshot.OwnerTurnEffectsExpired = true;
            }
            else state.Snapshot.DefensiveTurnEffectsExpired = true;
            state.Refresh();
        }
    }

    private static void RemoveIfFinished(ChaosCompositePower power, CombatPredictionSimulator simulator, ChaosCompositePower snapshot)
    {
        if (!snapshot.HasLiveEffects()) ((SimulatedCombatState)simulator.State.CombatState).SetPowerAmount(power, 0);
    }

    // The pinned solver batches vanilla side-start effects; it has no ordered registry for this hook.
    // Only use our seam when no other same-phase listener exists. Never approximate listener order.
    private static void AssertStartOrder(CombatPredictionSimulator simulator, ChaosCompositePower power)
    {
        if (!power.Definition.Card.Operations.Any(op => op.RuntimeSpec?.Trigger?.Kind is
                "next_turn_start" or "next_turns_start" or "turn_start")) return;
        var combat = (SimulatedCombatState)simulator.State.CombatState;
        IEnumerable<AbstractModel> listeners = ((ICombatPredictionHookListenerSource)combat).MirroredHookListeners;
        foreach (var listener in listeners)
        {
            if (listener is ChaosCompositePower) continue;
            var method = listener.GetType().GetMethod(nameof(AbstractModel.AfterSideTurnStart));
            if (method?.DeclaringType != typeof(AbstractModel))
                throw Unsupported($"AfterSideTurnStart 顺序冲突：{listener.GetType().FullName}；当前 Solver 无有序第三方入口");
        }
    }

    private static void Fire(ChaosCompositePower power, CombatPredictionSimulator simulator, string kind, CardPlay? sourcePlay = null)
    {
        var state = Read(simulator, power);
        var snapshot = state.Snapshot;
        var predicted = Source(snapshot);
        var card = (ChaosCardModel)predicted.MutablePreview;
        var operations = card.Generated.Operations;
        for (var trigger = 0; trigger < operations.Count; trigger++)
        {
            if (operations[trigger].RuntimeSpec?.Trigger?.Kind != kind) continue;
            if (!ChaosCompositePower.TryEnterTrigger(state.ActiveTriggers, trigger, _triggerDepth)) continue;
            var previousDepth = _triggerDepth;
            try
            {
                // Do not approximate AA's per-monster/resumed-choice epochs: an ambiguous over-budget
                // chain is rejected. The supported atomic payloads do not resume player choices.
                var count = state.Activations.GetValueOrDefault(trigger);
                if (count >= 20) throw Unsupported("触发链超过已核验输入区间的 20 次预算");
                state.Activations[trigger] = count + 1;
                _triggerDepth++;
                for (var index = trigger + 1; index < operations.Count; index++)
                {
                    var op = operations[index];
                    if (op.Parameters.GetValueOrDefault("triggerIndex", -1) != trigger) continue;
                    var spec = ChaosOperationExecutor.EffectiveRuntimeSpec(card, index);
                    var handler = OperationHandlerRegistry.Instance.TryGet(OperationKey.FromSpec(spec))
                        ?? throw Unsupported($"触发收益无 handler：{spec.Opcode}/{spec.Variant}");
                    var play = sourcePlay ?? new CardPlay
                    {
                        Card = card, Player = card.Owner, Target = null, ResultPile = PileType.Discard,
                        Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
                        IsAutoPlay = true, PlayIndex = 0, PlayCount = 1
                    };
                    var mirror = new CardOnPlayMirrorContext { Simulator = simulator, Card = predicted, CardPlay = play };
                    Creature? target = spec.Flags.Contains("random_enemy_reference")
                        ? simulator.Rng.CombatTargets.NextItem(mirror.CombatState.HittableEnemies) : null;
                    handler.Execute(new OperationExecutionContext(mirror, card, new OperationShape(index, op.Scope, spec), target, IsTriggered: true));
                    if (simulator.HasPendingChoice) throw Unsupported("触发收益产生尚未适配的选择续接");
                }
            }
            finally
            {
                _triggerDepth = previousDepth;
                state.ActiveTriggers.Remove(trigger);
            }
        }
    }

    internal static Exception Unsupported(string reason)
        => PredictionUnsupportedException.ForContent($"AutoAnthony 复合 Power：{reason}", typeof(ChaosCompositePower));
}
