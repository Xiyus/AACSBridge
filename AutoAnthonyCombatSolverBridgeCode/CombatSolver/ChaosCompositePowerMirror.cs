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
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Resources;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.TurnStart;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Orb;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
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
        ModifyBlockMultiplicativeMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
            c.Target == p.Owner && c.CardSource is not null && Read(c.Simulator, p).Snapshot.HasRule("first_card_block_doubled_each_turn")
                && ((SimulatedCombatState)c.CombatState).GetBlockCardsPlayedThisTurn(p.Owner) - c.Simulator.GetPoweredBlockEvents(c.CardPlay) <= 0 ? 2m : 1m);
        ModifyDamageMirrors.AdditiveRegistry.Register<ChaosCompositePower>(ModifyDamageAdditive);
        ModifyDamageMirrors.MultiplicativeRegistry.Register<ChaosCompositePower>(ModifyDamage);
        ModifyCardPlayCountMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            var snapshot = Read(c.Simulator, p).Snapshot;
            if (c.Card.Preview.Owner?.Creature != p.Owner) return c.PlayCount;
            if (snapshot.FirstCardReplayAvailable)
            {
                var count = ChaosCompositePower.LinkedEffectAmount(snapshot.EffectivePowerOperations(), "D:ReplayEventCard",
                    op => op.RuntimeSpec?.Trigger?.Kind == "first_card_played_each_turn", index => snapshot.EffectiveDefinitionOperationAmount(index, 1));
                if (count > 0) return c.PlayCount + count;
            }
            if (c.Card.Preview.Type == CardType.Attack && snapshot.NextAttackReplayAvailable)
                return c.PlayCount + ChaosCompositePower.LinkedEffectAmount(snapshot.EffectivePowerOperations(), "I:ReplayAttack",
                    CardEffectRules.IsNextAttackGrantTrigger, index => snapshot.EffectiveOperationAmount(index, 1));
            return c.PlayCount;
        });
        ModifyCardPlayCountMirrors.AfterRegistry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.Card.Preview.Owner?.Creature != p.Owner) return;
            var state = Read(c.Simulator, p);
            state.Snapshot.FirstCardReplayAvailable = false;
            state.Refresh();
        });
        ModifyCardPlayResultLocationMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            var location = c.Location;
            if (c.Card.Preview.Owner?.Creature == p.Owner && c.Card.Preview.Type == CardType.Skill
                && Read(c.Simulator, p).Snapshot.EffectiveDescriptionOperations().Any(op => op.Template == "N:Exhaust" && op.RuntimeSpec?.Variant == "referenced"))
                location.pileType = PileType.Exhaust;
            return location;
        });
        ModifyEnergyCostInCombatMirrors.LateRegistry.Register<ChaosCompositePower>((p, c) =>
            c.Card.Preview.Owner?.Creature == p.Owner && c.Card.Preview.Type == CardType.Skill
                && Read(c.Simulator, p).Snapshot.HasRule("skills_cost_zero")
                || c.Card.Preview.Owner?.Creature == p.Owner && c.Card.Preview.Type == CardType.Attack
                    && Read(c.Simulator, p).Snapshot.NextAttackFreeAvailable ? 0m : c.Cost);
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
        AfterBlockGainedMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.Creature == p.Owner) Fire(p, c.Simulator, "block_gained");
        });
        AfterCurrentHpChangedMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.Creature == c.Simulator.State.GetOsty(Read(c.Simulator, p).OwnerPlayer!) && c.Delta < 0)
                Fire(p, c.Simulator, "osty_hp_lost", eventAmount: -c.Delta);
            if (c.Creature == p.Owner && c.Delta < 0 && c.CombatState.CurrentSide == p.Owner.Side)
                Fire(p, c.Simulator, "owner_hp_lost_during_turn");
        });
        AfterCardGeneratedForCombatMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.Creator?.Creature != p.Owner) return;
            Fire(p, c.Simulator, "card_generated", eventCard: c.Card);
            if (c.PreviewCard.Type == CardType.Status) Fire(p, c.Simulator, "status_generated", eventCard: c.Card);
        });
        AfterOrbChanneledMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.Player.Creature == p.Owner) Fire(p, c.Simulator, "orb_channeled");
        });
        AfterOrbEvokedMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.Orb.Owner != Read(c.Simulator, p).OwnerPlayer) return;
            var operations = Read(c.Simulator, p).Snapshot.Definition.Card.Operations;
            for (var index = 0; index < operations.Count; index++)
            {
                var op = operations[index];
                if (op.Template != "A:whenLightningEvoked" || !ChaosOrbResolver.MatchesSource(c.Orb, op)) continue;
                foreach (var target in c.Targets.Where(target => target.IsAlive))
                    Fire(p, c.Simulator, "lightning_orb_evoked", eventCreature: target, onlyTrigger: index);
            }
        });
        AfterStarsGainedMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.Amount > 0 && c.Gainer.Creature == p.Owner) Fire(p, c.Simulator, "stars_spent_or_gained");
        });
        AfterShuffleMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.Player == Read(c.Simulator, p).OwnerPlayer) Fire(p, c.Simulator, "draw_pile_shuffled");
        });
        AfterDamageGivenMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if ((c.Dealer != p.Owner && c.Dealer?.PetOwner?.Creature != p.Owner) || !c.Target.IsEnemy || !c.Props.IsPoweredAttack()) return;
            Fire(p, c.Simulator, "attack_damaged_enemy", eventCreature: c.Target);
            if (c.Result.TotalDamage > 0) Fire(p, c.Simulator, "attack_dealt_damage", eventCreature: c.Target, eventAmount: c.Result.TotalDamage);
        });
        AfterDamageReceivedMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.Target == p.Owner && c.Props.IsPoweredAttack() && c.Result.UnblockedDamage > 0
                && Read(c.Simulator, p).Snapshot.EffectiveDescriptionOperations().Any(op => op.Template == "CL:DieOnUnblockedAttack"))
            {
                ((SimulatedCombatState)c.CombatState).SetPowerAmount(p, 0);
                c.Simulator.Kill(p.Owner);
                return;
            }
            if (c.Target == p.Owner && c.Dealer is not null && c.Props.IsPoweredAttack())
                Fire(p, c.Simulator, "attack_received", eventCreature: c.Dealer);
        });
        BeforeCardPlayedMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.PreviewCard.Owner != Read(c.Simulator, p).OwnerPlayer) return;
            Fire(p, c.Simulator, "energy_cost_at_least_card_played", c.CardPlay);
        });
        AfterCardDrawnMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.PreviewCard.Owner != Read(c.Simulator, p).OwnerPlayer) return;
            var state = Read(c.Simulator, p);
            var countIndex = state.Snapshot.Definition.Card.Operations.ToList().FindIndex(op => op.Template == "CL:EveryCardsDrawn");
            if (countIndex >= 0)
            {
                var threshold = Math.Max(1, state.Snapshot.EffectiveOperationAmount(countIndex, 10));
                state.Snapshot.CardsDrawnTowardTrigger++;
                while (state.Snapshot.CardsDrawnTowardTrigger >= threshold)
                {
                    state.Snapshot.CardsDrawnTowardTrigger -= threshold;
                    state.Refresh();
                    Fire(p, c.Simulator, "cards_drawn_threshold", onlyTrigger: countIndex, eventCard: c.Card);
                }
            }
            var events = ChaosDrawEventPolicy.Events(
                c.PreviewCard.Tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.Strike),
                c.PreviewCard.Keywords.Contains(CardKeyword.Ethereal),
                c.PreviewCard.Type == CardType.Status, state.Snapshot.StatusDrawnThisTurn,
                c.FromHandDraw, c.CombatState.CurrentSide == p.Owner.Side);
            foreach (var kind in events)
            {
                if (kind == "first_status_drawn_each_turn")
                {
                    if (state.Snapshot.StatusDrawnThisTurn) continue;
                    // Commit before firing: recursive draws must see that the first status was consumed.
                    state.Snapshot.StatusDrawnThisTurn = true;
                    state.Refresh();
                }
                Fire(p, c.Simulator, kind, eventCard: c.Card);
            }
        });
        AfterCardExhaustedMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.PreviewCard.Owner == Read(c.Simulator, p).OwnerPlayer) Fire(p, c.Simulator, "card_exhausted", eventCard: c.Card);
        });
        AfterAutoPostPlayPhaseEnteredMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (c.Player.Creature == p.Owner) Fire(p, c.Simulator, "turn_end");
        });
        BeforeSideTurnEndMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            var state = Read(c.Simulator, p);
            if (state.Snapshot.DelayedTurns <= 0 || c.Side != p.Owner.Side || !c.Participants.Contains(p.Owner)) return;
            state.Snapshot.DelayedTurns--;
            state.Refresh();
            if (state.Snapshot.DelayedTurns > 0) return;
            Fire(p, c.Simulator, "turns_elapsed");
            RemoveIfFinished(p, c.Simulator, state.Snapshot);
        });
        AfterPlayerTurnStartMirrors.Registry.Register<ChaosCompositePower>((p, c) =>
        {
            if (Read(c.Simulator, p).OwnerPlayer == c.Player) ResolveStarts(p, c.Simulator, choices: true);
        });
        RegisterNeutralActionHooks();
    }

    internal static bool IsSupportedRule(string variant) => variant is "retain_block_between_turns"
        or "retain_hand_at_turn_end" or "skills_cost_zero" or "vulnerable_enemy_damage_bonus"
        or "weak_enemy_attack_damage_bonus" or "first_card_block_doubled_each_turn" or "first_derivative_bonus_damage"
        or "derivative_retain" or "die_on_unblocked_attack";

    // These overrides cannot execute an effect in our bounded contract. No AbilityRule,
    // resource/damage/orb/generation triggers or special play modifiers are admitted.
    // An unknown Power definition is rejected at root capture, before these registrations dispatch.
    private static readonly HashSet<string> NeutralActions = new(StringComparer.Ordinal)
    {
        "AfterApplied", "AfterCardEnteredCombat", "AfterAutoPrePlayPhaseEntered",
        "AfterPowerAmountChanged", "AfterStarsSpent",
        "AfterEnergySpent"
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

    internal static void ResetBudget(CombatPredictionSimulator simulator, bool choiceResume = false)
    {
        if (simulator.State.CombatState is not SimulatedCombatState combat) return;
        foreach (var power in combat.EffectivePowers().OfType<ChaosCompositePower>())
        {
            var state = Read(simulator, power);
            if (!choiceResume && state.ActiveTriggers.Count > 0) throw Unsupported("触发收益内出现未适配的输入边界");
            state.Activations.Clear();
        }
    }

    private static PredictedCard Source(ChaosCompositePower power, Player? ownerPlayer)
    {
        if (power.ProfileId.Length != 0)
            throw Unsupported("外部角色 Profile 的复合 Power 尚未适配");
        // 预测状态创建时捕获的 Player 引用（克隆 Creature 的 Player 属性在模拟分支中可能丢失）
        if (ownerPlayer is null)
            throw Unsupported("复合 Power 的 Owner Player 无法解析（模拟克隆边界）");
        var predicted = PredictedCard.Create(ChaosCardRegistry.Canonical(power.Character, power.Slot), ownerPlayer);
        var card = (ChaosCardModel)predicted.MutablePreview;
        if (power.SourceTinkeredDefinitionPayload.Length > 0)
            card.ApplyCapturedDefinition(CardTinkeringApi.DeserializeCard(power.SourceTinkeredDefinitionPayload));
        bool MatchesSource(ChaosCardModel candidate) => candidate.Definition.Slot == power.Slot
            && candidate.RuntimeProfileId == power.ProfileId && candidate.EffectiveDefinitionPayload == power.SourceTinkeredDefinitionPayload;
        var deckVersion = (uint)power.SourceDeckIndex < (uint)ownerPlayer.Deck.Cards.Count
            && ownerPlayer.Deck.Cards[power.SourceDeckIndex] is ChaosCardModel indexed && MatchesSource(indexed)
                ? indexed : ownerPlayer.Deck.Cards.OfType<ChaosCardModel>().FirstOrDefault(MatchesSource);
        if (deckVersion is not null) card.DeckVersion = deckVersion;
        card.ResolvedSpecialXValue = power.SpecialXValue;
        card.SetResolvedXValues(power.ResolvedEnergyXValue, power.ResolvedStarXValue);
        if (power.SourceUpgraded && card.IsUpgradable) PredictionUtils.UpgradeCard(card);
        card.ApplyCapturedOperationValues(power.CapturedOperationValues);
        return predicted;
    }

    private static void ValidatePower(ChaosCompositePower power, CombatPredictionSimulator simulator)
    {
        if (!BridgeBootstrap.IsReady) throw Unsupported("桥未完成全部初始化，拒绝部分适配预测");
        if (power.Owner?.Player is null) throw Unsupported("复合 Power Owner 不是玩家");
        var card = (ChaosCardModel)Source(power, power.Owner.Player).MutablePreview;
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
            card.Type == CardType.Power || card.Generated.Operations.Any(op => op.Template is "CL:AfterTurns" or "CL:DieOnUnblockedAttack"), card.ResolvedSpecialXValue, card.ResolvedEnergyXValue,
            card.ResolvedStarXValue, card.CaptureOperationValuesForPower(), context.CardPlay.Target?.CombatId,
            card.RuntimeProfileId, card.EffectiveDefinitionPayload, deckIndex);
        AssertStartOrder(context.Simulator, power);
        combat.BeginCardPowerApplication(card);
        try
        {
            combat.ApplyClonedPower(power, card.Owner.Creature, 1, card.Owner.Creature);
            if (power.HasRule("derivative_retain"))
                foreach (var derivative in context.OwnerState.AllCards.Where(candidate =>
                             candidate.Preview.Tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.Shiv)))
                    derivative.MutablePreview.AddKeyword(CardKeyword.Retain);
        }
        finally { combat.CompleteCardPowerApplication(card); }
    }

    private static void AfterPlayed(ChaosCompositePower power, AfterCardPlayedMirrorContext context)
    {
        var state = Read(context.Simulator, power);
        if (context.PreviewCard.Owner != state.OwnerPlayer) return;
        if (state.Snapshot.IgnoreArmingCardPlay)
        {
            state.Snapshot.IgnoreArmingCardPlay = false;
            state.Refresh();
            return;
        }
        var isFirst = ChaosHistory.Finished(context.Simulator, state.OwnerPlayer!).Count() == 1;
        var countIndex = state.Snapshot.Definition.Card.Operations.ToList().FindIndex(op => op.Template == "CL:EveryCardsPlayedThisTurn");
        if (countIndex >= 0 && context.PreviewCard.Id != ChaosCardRegistry.Canonical(state.Snapshot.Character, state.Snapshot.Slot).Id)
        {
            var threshold = Math.Max(1, state.Snapshot.EffectiveOperationAmount(countIndex, 5));
            state.Snapshot.CardsPlayedTowardTrigger++;
            while (state.Snapshot.CardsPlayedTowardTrigger >= threshold)
            {
                state.Snapshot.CardsPlayedTowardTrigger -= threshold;
                state.Refresh();
                Fire(power, context.Simulator, "cards_played_this_turn_threshold", context.CardPlay, onlyTrigger: countIndex);
            }
        }
        Fire(power, context.Simulator, "card_played", context.CardPlay);
        if (isFirst) Fire(power, context.Simulator, "first_card_played_each_turn", context.CardPlay);
        if (context.PreviewCard.Type == CardType.Power) Fire(power, context.Simulator, "power_played", context.CardPlay);
        var derivativeTriggers = state.Snapshot.Definition.Card.Operations;
        for (var index = 0; index < derivativeTriggers.Count; index++)
            if (derivativeTriggers[index].Template == "A:whenSoulPlayed" && ChaosDerivativeResolver.Matches(context.PreviewCard, derivativeTriggers[index]))
                Fire(power, context.Simulator, derivativeTriggers[index].RuntimeSpec!.Trigger!.Kind, context.CardPlay, onlyTrigger: index);
        if (context.PreviewCard.Keywords.Contains(CardKeyword.Ethereal)) Fire(power, context.Simulator, "ethereal_card_played", context.CardPlay);
        if (context.CardPlay.Resources.StarsSpent > 0) Fire(power, context.Simulator, "stars_spent_or_gained", context.CardPlay);
        if (context.PreviewCard.Type == CardType.Attack)
        {
            state.Snapshot.AttacksPlayedThisTurn = ChaosHistory.Finished(context.Simulator, state.OwnerPlayer!).Count(play => play.Card.Type == CardType.Attack);
            state.Refresh();
        }
        if (context.PreviewCard.Type == CardType.Attack)
        {
            Fire(power, context.Simulator, "attack_played", context.CardPlay);
            if (state.Snapshot.AttacksPlayedThisTurn == 1) Fire(power, context.Simulator, "first_attack_played_each_turn", context.CardPlay);
            var operations = state.Snapshot.Definition.Card.Operations;
            for (var index = 0; index < operations.Count; index++)
                if (ChaosCompositePower.IsNthAttackPlayedThisTurnTrigger(operations[index])
                    && state.Snapshot.AttacksPlayedThisTurn == ChaosCompositePower.NthAttackPlayedThisTurnThreshold(operations[index]))
                    Fire(power, context.Simulator, "nth_attack_played_this_turn", context.CardPlay, onlyTrigger: index);
            if (state.Snapshot.NextAttackTriggerAvailable && state.Snapshot.NextAttackTriggersRemaining > 0)
            {
                var index = operations.ToList().FindIndex(CardEffectRules.IsNextAttackGrantTrigger);
                if (index >= 0) Fire(power, context.Simulator, operations[index].RuntimeSpec!.Trigger!.Kind, context.CardPlay, onlyTrigger: index);
                state.Snapshot.NextAttackTriggersRemaining = Math.Max(0, state.Snapshot.NextAttackTriggersRemaining - 1);
                state.Snapshot.NextAttackTriggerAvailable = state.Snapshot.NextAttackTriggersRemaining > 0;
                if (!state.Snapshot.NextAttackTriggerAvailable)
                {
                    state.Snapshot.NextAttackReplayAvailable = false;
                    state.Snapshot.NextAttackFreeAvailable = false;
                    RemoveIfFinished(power, context.Simulator, state.Snapshot);
                }
            }
        }
        if (context.PreviewCard.Type == CardType.Skill) Fire(power, context.Simulator, "skill_played", context.CardPlay);
        if (state.Snapshot.FirstAttackOrSkillAvailable && context.PreviewCard.Type is CardType.Attack or CardType.Skill)
        {
            state.Snapshot.FirstAttackOrSkillAvailable = false;
            Fire(power, context.Simulator, "first_attack_or_skill_each_turn", context.CardPlay);
        }
        if (state.Snapshot.ZeroCostAttackReturnAvailable && context.PreviewCard.Type == CardType.Attack && context.PreviewCard.EnergyCost.GetResolved() == 0)
        {
            state.Snapshot.ZeroCostAttackReturnAvailable = false;
            Fire(power, context.Simulator, "first_zero_cost_attack_played_each_turn", context.CardPlay);
        }
        state.Refresh();
    }

    internal static void Start(CombatPredictionSimulator simulator, SimulatedCombatState combat,
        CombatSide side, IReadOnlyList<Creature> participants, ChaosCompositePower? onlyPower = null)
    {
        foreach (var power in combat.EffectivePowers().OfType<ChaosCompositePower>().ToArray())
        {
            if (power.Owner.Side != side || !participants.Contains(power.Owner)) continue;
            if (onlyPower is not null && !ReferenceEquals(onlyPower, power)) continue;
            AssertStartOrder(simulator, power);
            var state = Read(simulator, power);
            var snapshot = state.Snapshot;
            snapshot.AttacksPlayedThisTurn = 0;
            snapshot.StatusDrawnThisTurn = false;
            snapshot.CardsPlayedTowardTrigger = 0;
            snapshot.FirstAttackOrSkillAvailable = snapshot.EffectiveDescriptionOperations().Any(op => op.Template == "CL:FirstAttackOrSkillEachTurn");
            snapshot.FirstCardReplayAvailable = ChaosCompositePower.HasTriggerWithLinkedEffect(snapshot.EffectivePowerOperations(), "first_card_played_each_turn", "D:ReplayEventCard");
            snapshot.ZeroCostAttackReturnAvailable = snapshot.EffectivePowerOperations().Any(op => op.RuntimeSpec?.Trigger?.Kind == "first_zero_cost_attack_played_each_turn");
            state.Refresh();
            ResolveStarts(power, simulator, choices: false);
        }
    }

    private static void ResolveStarts(ChaosCompositePower power, CombatPredictionSimulator simulator, bool choices)
    {
        var state = Read(simulator, power);
        var snapshot = state.Snapshot;
        if (snapshot.RemainingTurnTriggers > 0 && snapshot.StartTriggerNeedsPlayerChoice("next_turns_start") == choices)
        {
            Fire(power, simulator, "next_turns_start");
            if (simulator.HasPendingChoice) return;
            snapshot.RemainingTurnTriggers--;
            RemoveIfFinished(power, simulator, snapshot);
        }
        if (snapshot.WaitForNextTurn && snapshot.StartTriggerNeedsPlayerChoice("next_turn_start") == choices)
        {
            snapshot.WaitForNextTurn = false;
            Fire(power, simulator, "next_turn_start");
            if (simulator.HasPendingChoice) return;
            RemoveIfFinished(power, simulator, snapshot);
        }
        if (snapshot.Permanent && snapshot.StartTriggerNeedsPlayerChoice("turn_start", "turn_start_if_self_in_exhaust") == choices)
            FireAny(power, simulator, ["turn_start", "turn_start_if_self_in_exhaust"]);
        state.Refresh();
    }

    internal static void FireAny(ChaosCompositePower power, CombatPredictionSimulator simulator, IReadOnlyCollection<string> kinds, bool autoPrePlay = false)
    {
        var operations = Read(simulator, power).Snapshot.Definition.Card.Operations;
        for (var index = 0; index < operations.Count; index++)
        {
            var kind = operations[index].RuntimeSpec?.Trigger?.Kind;
            if (kind is null || !kinds.Contains(kind)) continue;
            var mayhem = kind is "turn_start" or "turn_start_if_self_in_exhaust" && operations.Any(op => op.Template == "CL:PlayTopDrawCard"
                && op.Parameters.GetValueOrDefault("triggerIndex", -1) == index);
            if (mayhem != autoPrePlay) continue;
            Fire(power, simulator, kind, onlyTrigger: index);
            if (simulator.HasPendingChoice) return;
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
            // Batch AQ-2：this_turn 触发器回合结束过期（源码 TurnLimitedTriggerExpired → RemoveIfNoLiveEffects）
            RemoveIfFinished(power, simulator, state.Snapshot);
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
            if (method?.DeclaringType == typeof(AbstractModel)) continue;
            // 游戏原生模型（MegaCrit.* 命名空间）由 CombatSolver 自己的镜像处理——不是顺序冲突
            var listenerNamespace = listener.GetType().Namespace ?? "";
            if (listenerNamespace.StartsWith("MegaCrit", StringComparison.Ordinal)) continue;
            // AutoAnthony 自身的模型也由桥处理
            if (listenerNamespace.StartsWith("AutoAnthony", StringComparison.Ordinal)) continue;
            throw Unsupported($"AfterSideTurnStart 顺序冲突：{listener.GetType().FullName}；当前 Solver 无有序第三方入口");
        }
    }

    internal static void Fire(ChaosCompositePower power, CombatPredictionSimulator simulator, string kind, CardPlay? sourcePlay = null,
        Creature? eventCreature = null, decimal eventAmount = 0, int? onlyTrigger = null, PredictedCard? eventCard = null)
    {
        if (simulator.HasPendingChoice) { simulator.RejectExecutionContinuation(); return; }
        var state = Read(simulator, power);
        var snapshot = state.Snapshot;
        var predicted = Source(snapshot, state.OwnerPlayer);
        var card = (ChaosCardModel)predicted.MutablePreview;
        var operations = card.Generated.Operations;
        for (var trigger = 0; trigger < operations.Count; trigger++)
        {
            if (operations[trigger].RuntimeSpec?.Trigger?.Kind != kind) continue;
            if (onlyTrigger is not null && onlyTrigger != trigger) continue;
            if (ChaosCompositePower.TurnLimitedTriggerExpired(operations[trigger], snapshot.OwnerTurnEffectsExpired,
                snapshot.DefensiveTurnEffectsExpired)) continue;
            // AA evaluates each threshold immediately before its payoff. A previous payoff
            // can change the resolved cost, so do not snapshot it once for the whole event.
            if (kind == "energy_cost_at_least_card_played"
                && (sourcePlay is null || sourcePlay.Card.EnergyCost.GetResolved()
                    < snapshot.EffectiveOperationAmount(trigger, 2))) continue;
            if (!ChaosCompositePower.TryEnterTrigger(state.ActiveTriggers, trigger, _triggerDepth)) continue;
            var previousDepth = _triggerDepth;
            try
            {
                // Do not approximate AA's per-monster/resumed-choice epochs: an ambiguous over-budget
                // chain is rejected. The supported atomic payloads do not resume player choices.
                var count = state.Activations.GetValueOrDefault(trigger);
                if (count >= ChaosAbilityTriggerLimiter.MaximumActivationsPerEffect) continue;
                state.Activations[trigger] = count + 1;
                _triggerDepth++;
                var increase = Enumerable.Range(0, operations.Count)
                    .FirstOrDefault(index => ChaosOperationExecutor.RollingGrowthOwner(operations, index) == trigger, -1);
                var rolling = operations.Select((op, index) => (op, index)).FirstOrDefault(item =>
                    item.op.Template is "N:AllD" or "CL:RollingAllDamage"
                    && item.op.Parameters.GetValueOrDefault("triggerIndex", -1) == trigger && increase >= 0);
                var amount = eventAmount;
                if (rolling.op is not null && amount == 0)
                {
                    if (snapshot.RollingDamage <= 0) snapshot.RollingDamage = Math.Max(0, snapshot.EffectiveOperationAmount(rolling.index, 0));
                    amount = snapshot.RollingDamage;
                }
                var target = eventCreature;
                var sourceTarget = snapshot.SourceTargetCombatId < 0 ? null
                    : simulator.State.CombatState.Creatures.FirstOrDefault(creature => creature.CombatId == snapshot.SourceTargetCombatId);
                if (ChaosClauseMirror.ResolveTarget(simulator, card, trigger, sourceTarget, ref target))
                {
                    var play = sourcePlay ?? new CardPlay
                    {
                        Card = card, Player = card.Owner, Target = target, ResultPile = PileType.Discard,
                        Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
                        IsAutoPlay = true, PlayIndex = 0, PlayCount = 1
                    };
                    if (!ChaosClauseMirror.Execute(simulator, predicted, card, play, trigger, new OperationResolutionState(),
                        eventCard ?? (sourcePlay is null ? null : simulator.State.FindCard(sourcePlay.Card)), amount, target, powered: false)) return;
                }
                if (rolling.op is not null && increase >= 0)
                    snapshot.RollingDamage = ChaosCompositePower.AdvanceRollingDamage(snapshot.RollingDamage, snapshot.EffectiveOperationAmount(increase, 5));
                state.Refresh();
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

    private static decimal ModifyDamage(ChaosCompositePower power, ModifyDamageMirrorContext context)
    {
        var state = Read(context.Simulator, power);
        var snapshot = state.Snapshot;
        var combat = (SimulatedCombatState)context.CombatState;
        decimal multiplier = 1m;
        if (context.Dealer == power.Owner && context.Props.IsPoweredAttack() && context.CardSource?.Preview.Type == CardType.Attack)
            multiplier *= TriggeredAttackDamageMultiplier(state, context);
        if (snapshot.HasRule("vulnerable_enemy_damage_bonus") && context.Dealer == power.Owner
            && context.Target is { } vulnerableTarget && combat.GetAmount<VulnerablePower>(vulnerableTarget) > 0)
            multiplier *= 1m + snapshot.RuleAmount("vulnerable_enemy_damage_bonus") / 100m;
        if (snapshot.HasRule("weak_enemy_attack_damage_bonus") && context.Props.IsPoweredAttack()
            && context.CardSource is not null && (context.Dealer == power.Owner || context.Dealer?.PetOwner?.Creature == power.Owner)
            && context.Target is { } weakTarget && combat.GetAmount<WeakPower>(weakTarget) > 0)
            multiplier *= 1m + snapshot.RuleAmount("weak_enemy_attack_damage_bonus") / 100m;
        var reductionIndex = snapshot.Definition.Card.Operations.ToList()
            .FindIndex(op => op.RuntimeSpec?.Trigger?.Kind == "vulnerable_enemy_damage_reduction");
        if (reductionIndex >= 0 && context.Target == power.Owner && context.Props.IsPoweredAttack()
            && !ChaosCompositePower.TurnLimitedTriggerExpired(snapshot.Definition.Card.Operations[reductionIndex],
                snapshot.OwnerTurnEffectsExpired, snapshot.DefensiveTurnEffectsExpired)
            && context.Dealer is { } dealer && combat.GetAmount<VulnerablePower>(dealer) > 0)
            multiplier *= Math.Max(0m, 1m - snapshot.EffectiveOperationAmount(reductionIndex, 50) / 100m);
        return multiplier;
    }

    private static decimal TriggeredAttackDamageMultiplier(ChaosCompositePredictionState state, ModifyDamageMirrorContext context)
    {
        var snapshot = state.Snapshot;
        var operations = snapshot.EffectivePowerOperations();
        CardPlay[]? prior = null;
        CardPlay[] Prior() => prior ??= ChaosHistory.Finished(context.Simulator,
            state.OwnerPlayer ?? context.CardSource?.Preview.Owner ?? throw Unsupported("攻击增伤缺少玩家来源"))
            .Where(play => play.Card.Type == CardType.Attack).ToArray();
        decimal multiplier = 1m;
        for (var index = 0; index < operations.Count; index++)
        {
            var modifier = operations[index];
            if (modifier.Template != "M:TriggeredAttackDamagePercent" || !modifier.Parameters.TryGetValue("triggerIndex", out var owner)
                || owner < 0 || owner >= index || !CardEffectRules.SuppliesEventAttackForDamageModifier(operations[owner])) continue;
            var applies = operations[owner].RuntimeSpec?.Trigger?.Kind switch
            {
                "attack_played" => true,
                "first_attack_played_each_turn" => Prior().Length == 0,
                "first_zero_cost_attack_played_each_turn" => context.CardSource!.Preview.EnergyCost.GetResolved() == 0
                    && Prior().Count(play => ChaosHistory.CurrentCard(context.Simulator, play).EnergyCost.GetResolved() == 0) == 0,
                "nth_attack_played_this_turn" => Prior().Length + 1 == snapshot.EffectiveOperationAmount(owner, 3),
                "next_attack" or "next_attacks_this_turn" => !snapshot.IgnoreArmingCardPlay && snapshot.NextAttackTriggerAvailable && snapshot.NextAttackTriggersRemaining > 0,
                _ => false
            };
            if (applies) multiplier *= 1m + Math.Max(0, snapshot.EffectiveOperationAmount(index, 50)) / 100m;
        }
        return multiplier;
    }

    private static decimal ModifyDamageAdditive(ChaosCompositePower power, ModifyDamageMirrorContext context)
    {
        var state = Read(context.Simulator, power);
        var snapshot = state.Snapshot;
        var combat = (SimulatedCombatState)context.CombatState;
        decimal bonus = 0m;
        if (context.Dealer == power.Owner && context.Props.IsPoweredAttack() && context.CardSource?.Preview.Owner?.Creature == power.Owner
            && context.CardSource.Preview.Tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.Shiv) && snapshot.HasRule("first_derivative_bonus_damage")
            && !ChaosHistory.Finished(context.Simulator, state.OwnerPlayer!).Any(play => play.Card.Tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.Shiv)))
            bonus += snapshot.RuleAmount("first_derivative_bonus_damage");
        if (snapshot.IgnoreArmingCardPlay || !snapshot.NextAttackTriggerAvailable || snapshot.NextAttackTriggersRemaining <= 0
            || context.Dealer != power.Owner || !context.Props.IsPoweredAttack() || context.CardSource?.Preview.Type != CardType.Attack
            || context.CardSource.Preview.Owner?.Creature != power.Owner) return bonus;
        var operations = snapshot.EffectivePowerOperations();
        for (var index = 0; index < operations.Count; index++)
        {
            var op = operations[index];
            if (op.Scope != OperationScope.Modifier || !op.Parameters.TryGetValue("triggerIndex", out var owner)
                || owner < 0 || owner >= snapshot.Definition.Card.Operations.Count || !CardEffectRules.IsNextAttackGrantTrigger(snapshot.Definition.Card.Operations[owner])) continue;
            var value = Math.Max(1, snapshot.EffectiveOperationAmount(index, 1));
            var variant = op.Template == "M:base" ? op.RuntimeSpec?.Variant : null;
            if (op.Template == "M:DamagePerExhaustCard" || variant == "exhaust_pile_scaled")
                bonus += value * context.Simulator.State.GetPlayerCombatState(state.OwnerPlayer!).ExhaustPile.Cards.Count;
            else if (variant == "vulnerable_scaled") bonus += value * (context.Target is { } target ? combat.GetAmount<VulnerablePower>(target) : 0);
            else if (variant == "strike_count_scaled") bonus += value * context.Simulator.State.GetPlayerCombatState(state.OwnerPlayer!).AllCards
                .Count(card => card.Preview.Tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.Strike));
        }
        return bonus;
    }
}
