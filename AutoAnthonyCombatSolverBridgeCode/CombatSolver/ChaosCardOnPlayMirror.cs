using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using AutoAnthonyCombatSolverBridge.Diagnostics;
using AutoAnthonyCombatSolverBridge.Translation;
using AutoAnthonyCombatSolverBridge.Bootstrap;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.CombatSolver;

/// <summary>
/// ChaosCardModel.OnPlay 的通用镜像：一个 handler 解释全部 500+ 具体卡类（读"当前这个实例"
/// 的 RuntimeSpec，逐操作走翻译层），而不是逐卡写死。
///
/// 纪律（CombatSolver 第三方适配规则 + 本桥设计红线）：
///  1. 先整卡预校验，后执行——任何不支持的操作都让整场搜索中止（ForContent →
///     IncompatibleGameplayModException），绝不静默把未知效果当空操作；
///  2. 数值解析直接复用 AutoAnthony 自己的 internal 投影（publicized 编译期引用），
///     与真实执行逐位一致；
///  3. 只写模拟分支状态（receiver 是 MutablePreview 克隆）。
///
/// 预校验排除的 0.1.0 边界（全部 fail-closed，后续里程碑逐项解锁）：
///  - X 费卡（CostsX / HasStarCostX）与 X 值源槽（energy_x/star_x/special_x）→ 0.4.0；
///  - Modifier / AbilityTrigger / ConditionalTrigger / AbilityRule scope 的操作
///    （修饰符数学、触发器、复合 Power）→ 0.2.0/0.6.0；
///  - 玩家选牌（选择器模板 / CardTargetSlot / requires 结构升级）→ 0.5.0；
///  - 随机目标引用（random_enemy_reference）、事件目标、历史计数、阈值翻倍 → 0.4.0；
///  - Power 类型卡上的 deal_damage（Unpowered 逐 hit 路径）→ 0.2.0；
///  - 结构性升级（RepeatOperation / ExecuteOperationOnPlay / ChooseExhaust / 衍生卡升级）。
/// </summary>
internal static class ChaosCardOnPlayMirror
{
    public static void Execute(ChaosCardModel card, CardOnPlayMirrorContext context)
    {
        if (!BridgeBootstrap.IsReady)
            throw PredictionUnsupportedException.ForContent("桥未完成全部初始化，拒绝部分适配预测。", typeof(ChaosCardModel));
        // 硬失败兜底：正常情况下保守可打性已让矩阵外卡不进入候选；强制打出
        // （auto-play 类效果绕过 CanPlay）落到这里时仍按 fail-closed 中止整场搜索。
        var reason = ChaosCardSupport.GetUnsupportedReason(card, context.Card.Original);
        if (reason is not null)
            throw PredictionUnsupportedException.ForContent(reason, typeof(ChaosCardModel));

        // 复刻 ChaosCardModel.OnPlay 的 X 值解析（MODEL L751-761）：Hook.ModifyXValue 传
        // 分支战斗状态——与 CombatSolver 自带的 ResolveEnergyXValue 扩展同款分支安全路径。
        var resolvedEnergyX = card.EnergyCost.CostsX
            ? Hook.ModifyXValue(context.CombatState, card,
                context.CardPlay.Resources.EnergySpent > 0
                    ? context.CardPlay.Resources.EnergySpent
                    : card.EnergyCost.CapturedXValue)
            : 0;
        var resolvedStarX = card.HasStarCostX
            ? Hook.ModifyXValue(context.CombatState, card,
                context.CardPlay.Resources.StarsSpent > 0
                    ? context.CardPlay.Resources.StarsSpent
                    : card.LastStarsSpent)
            : 0;
        card.SetResolvedXValues(resolvedEnergyX, resolvedStarX);
        context.Simulator.AcknowledgeExecutionDispatch();
        Continue(card, context, 0, new Dictionary<int, bool>(), new OperationResolutionState
        {
            PriorAttackHitsOnTargetAtPlayStart = context.CardPlay.Target is { } target
                ? ((SimulatedCombatState)context.CombatState).GetPoweredAttackHitsThisTurn(card.Owner.Creature, target) : 0,
            TargetDebuffSnapshot = context.CardPlay.Target is { } debuffTarget && card.Generated.Operations.Any(op => op.Template == "NCR:CopyTargetDebuffsToOthers")
                ? TargetDebuffResolver.Capture(context.Simulator, debuffTarget) : null
        });
    }

    private static bool Continue(ChaosCardModel card, CardOnPlayMirrorContext context, int nextIndex,
        Dictionary<int, bool> conditionResults, OperationResolutionState resolution)
    {
        context.Simulator.AcknowledgeExecutionDispatch();
        var operations = card.Generated.Operations;
        var extraOperations = card.IsUpgraded && card.Generated.Upgrade is { } upgrade
            ? upgrade.Effects.Where(effect => effect.Kind == CardUpgradeKind.ExecuteOperationOnPlay)
                .Select(effect => effect.OperationIndex).OfType<int>().Where(index => (uint)index < (uint)operations.Count).ToArray() : [];
        // 0.9.0：条件结果缓存（EvaluateConditionOnce 语义——按索引一次评估）
        for (var pc = nextIndex; pc < operations.Count + extraOperations.Length; pc++)
        {
            var isExtra = pc >= operations.Count;
            var index = isExtra ? extraOperations[pc - operations.Count] : pc;
            var operation = operations[index];
            if (ChaosCardPassiveMirror.IsOperation(operation.Template)) continue;
            // 0.9.0：条件操作——评估并缓存，不执行（复刻源码 L178-190）
            if (!isExtra && operation.Scope == OperationScope.ConditionalTrigger)
            {
                var conditionSpec = TryEffectiveSpec(card, index);
                // Batch AQ：ForEach 触发器（immediate）——对本卡已消耗的每张卡执行收益
                // （复刻源码 L180-182 ExecuteForEach；ExhaustedByCard 由 ExhaustHandler 记录）
                if (conditionSpec?.Opcode == "trigger"
                    && conditionSpec.Trigger is { Lifetime: "immediate" } foreachTrigger && IsForEachKind(foreachTrigger.Kind))
                {
                    if (!ExecuteForEach(card, index, context, resolution)) return false;
                    continue;
                }
                if (conditionSpec?.Condition is { } condition
                    && ConditionEvaluator.IsSupportedCondition(condition.Kind))
                {
                    if (!conditionResults.TryGetValue(index, out var cached))
                        conditionResults[index] = ConditionEvaluator.Evaluate(card, operation, context, resolution);
                }
                continue;
            }
            // 校验层已保证只剩可执行操作；这里防御性地重复 Play 的元数据跳过规则
            if (operation.Scope is OperationScope.Modifier or OperationScope.AbilityTrigger)
                continue;
            if (operation.Scope == OperationScope.AbilityRule && ChaosOperationExecutor.RequiresCompositePower(operation))
                continue;
            if (operation.Template is "N_SELECT_HAND_CARD" or "N_SELECT_HAND_ATTACK")
                continue;
            // 0.9.0：payoff 的条件门控（复刻源码 L192-200）——triggerIndex 指向条件操作
            if (!isExtra && operation.Parameters.TryGetValue("triggerIndex", out var gateIndex)
                && gateIndex >= 0 && gateIndex < operations.Count)
            {
                var gate = operations[gateIndex];
                // 只门控普通条件（AbilityTrigger/持久触发器仍跳过——武装为 Power）
                if (gate.Scope == OperationScope.ConditionalTrigger)
                {
                    // Batch AQ：ForEach 触发器的收益已由 ForEach 循环执行——主循环跳过
                    var gateSpec = TryEffectiveSpec(card, gateIndex);
                    if (gateSpec?.Opcode == "trigger"
                        && gateSpec.Trigger is { } foreachGate && IsForEachKind(foreachGate.Kind))
                        continue;
                    if (!conditionResults.TryGetValue(gateIndex, out var gateResult) || !gateResult)
                        continue;    // 条件 false → 跳过 payoff
                }
                else if (gate.Scope is OperationScope.AbilityTrigger)
                    continue;    // 持久触发器的 payoff 不在本场执行
            }

            var spec = ChaosOperationExecutor.EffectiveRuntimeSpec(card, index);
            var operationContext = new OperationExecutionContext(context, card, new OperationShape(index, operation.Scope, spec), Resolution: resolution);
            if (!ChaosCardSlotMirror.Resolve(context, card, index, resolution))
            {
                Suspend(pc);
                return false;
            }
            if (!DependencyResolver.Matches(operationContext) || DependencyResolver.Multiplier(operationContext) <= 0) continue;
            if (SelectionEffectResolver.TryExecute(operationContext))
            {
                if (context.Simulator.HasPendingChoice)
                {
                    context.Simulator.RejectExecutionContinuation();
                    Suspend(pc + 1);
                    return false;
                }
                continue;
            }

            // 0.5.0：选择型操作跳过——由 CardChoiceMirrors 在 OwnChoice 阶段解析
            // （求解器展开分支 + 原生 Effect 施加，与源码 CardCmd/CardPileCmd 语义一致）
            if (ChaosCardChoiceMirror.IsSupportedSelection(spec) && operation.CardTargetSlot is null
                && !(spec.Opcode == "exhaust_card" && spec.Variant == "referenced" && operationContext.ReferencedCard is not null))
            {
                var selection = ChaosCardChoiceMirror.BuildOperationSpec(context.Simulator, context.Card, card, index, operationContext.ExecutableAmount);
                if (selection is not null)
                {
                    var binding = context.Simulator.StateStore.Get(context.Card.Original, static () => new ChaosCardSlotMirror.BindingState());
                    binding.Resolution = resolution;
                    binding.Slot = null;
                    var combat = (SimulatedCombatState)context.CombatState;
                    combat.ResolveActionCardChoice(context.Simulator, context.Card, string.Empty, selection,
                        combat._activeCardExecutionDeaths ?? new HashSet<uint>(), $"aa.operation.{index}");
                    if (context.Simulator.HasPendingChoice)
                    {
                        Suspend(pc + 1);
                        return false;
                    }
                }
                continue;
            }

            var handler = OperationHandlerRegistry.Instance.TryGet(OperationKey.FromSpec(spec));
            if (handler is null)
                throw PredictionUnsupportedException.ForContent(
                    Describe(card, index, $"opcode={spec.Opcode} variant={spec.Variant} 无注册 handler（校验层遗漏，属桥的 bug）"),
                    typeof(ChaosCardModel));

            // 复刻 ExecuteWithResolvedTarget 的显式随机目标解析（L474-481）：RNG 消耗必须与
            // 真实执行一致（即使结果只影响 state.Target）；分支的 CombatTargets 流。
            Creature? resolvedRandomTarget = null;
            if (spec.Flags.Contains("random_enemy_reference"))
                resolvedRandomTarget = context.Rng.CombatTargets.NextItem(context.CombatState.HittableEnemies);

            handler.Execute(new OperationExecutionContext(context, card,
                new OperationShape(index, operation.Scope, spec), resolvedRandomTarget, Resolution: resolution));
            if (context.Simulator.HasPendingChoice)
            {
                // Most simulator command families rely on full-action replay for deeper choices.
                // Only retain our tail when the inner command owns its complete continuation.
                if (handler is not Translation.Handlers.DrawHandler) context.Simulator.RejectExecutionContinuation();
                Suspend(pc + 1);
                return false;
            }
        }
        ChaosCompositePowerMirror.Arm(card, context);
        if (context.Simulator.StateStore.TryGetReadOnly<ChaosCardSlotMirror.BindingState>(context.Card.Original, out var completedBinding))
        { completedBinding!.Resolution = null; completedBinding.Slot = null; }
        if (resolution.EndTurnRequested && context.OwnerState.Phase == MegaCrit.Sts2.Core.Combat.PlayerTurnPhase.Play)
            ((SimulatedCombatState)context.CombatState).RequestPlayerTurnEnd();
        return true;

        void Suspend(int index) => context.Simulator.AppendExecutionContinuation(
            new OperationFrame(context.Card, context.CardPlay, index, conditionResults, resolution));
    }

    private sealed record OperationFrame(PredictedCard Card, CardPlay Play, int NextIndex,
        Dictionary<int, bool> Conditions, OperationResolutionState Resolution) : ICombatPredictionExecutionFrame
    {
        public IEnumerable<CardPlay> ActiveCardPlays => [Play];
        public void PrepareFork(PredictionForkContext context)
        {
            CombatPredictionSimulator.PrepareExecutionCardPlay(Card, Play, context);
            Resolution.PrepareFork(context);
        }
        public ICombatPredictionExecutionFrame Fork(PredictionForkContext context) => this with
        {
            Card = context.RequireRemap(Card), Play = context.RequireRemap(Play),
            Conditions = new Dictionary<int, bool>(Conditions), Resolution = Resolution.Fork(context)
        };
        public bool Resume(CombatPredictionSimulator simulator) => Continue((ChaosCardModel)Card.MutablePreview,
            new CardOnPlayMirrorContext { Simulator = simulator, Card = Card, CardPlay = Play }, NextIndex, Conditions, Resolution);
    }

    // --- 整卡预校验（fail-closed；结果由 ChaosCardSupport 按 (根实例, 升级态) 缓存）----------------

    internal static string? ValidateCard(ChaosCardModel card)
    {
        var operations = card.Generated.Operations;
        // 0.5.0：选择型操作（exhaust/discard/move 的 selected）由 CardChoiceMirrors 在
        // OwnChoice 阶段解析（求解器展开分支 + 原生 Effect 施加）——校验放行、执行循环跳过。
        // 只支持恰好一个选择型操作（多个时第二个会被静默丢弃，故拒绝）。
        var selectionCount = 0;
        var selectionIndex = -1;
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            var reason = ValidateOperation(card, index, operation);
            if (reason is not null)
                return reason;
            var spec = TryEffectiveSpec(card, index);
            if (spec is not null && ChaosCardChoiceMirror.IsSupportedSelection(spec))
            {
                selectionCount++;
                selectionIndex = index;
            }
        }

        if (operations.Any(ChaosOperationExecutor.RequiresCompositePower))
        {
            if (card.RuntimeProfileId.Length != 0)
                return Describe(card, null, "外部角色 Profile 的触发器尚未适配");
        }

        if (card.IsUpgraded && card.Generated.Upgrade is { } upgrade)
        {
            foreach (var effect in upgrade.Effects)
            {
                if (effect.Kind == CardUpgradeKind.SelectAllCards && effect.OperationIndex is { } selectedIndex
                    && (uint)selectedIndex < (uint)operations.Count && operations[selectedIndex].Template != "I:Upgrade")
                    return Describe(card, selectedIndex, "SelectAllCards 的选择变体尚未实现");
            }
        }

        return null;
    }

    private static string? ValidateOperation(ChaosCardModel card, int index, GeneratorOperation operation)
    {
        if (ChaosCardPassiveMirror.IsOperation(operation.Template) && operation.CardTargetSlot is null)
            return null;
        if (operation.Template is "N_SELECT_HAND_CARD" or "N_SELECT_HAND_ATTACK")
            return null;

        if (operation.Scope is OperationScope.Modifier)
        {
            if (CardEffectRules.IsDependencyPrefix(operation))
            {
                var operations = card.Generated.Operations;
                return index + 1 < operations.Count && CardEffectRules.IsLegalDependencyPayoff(operation, operations[index + 1])
                    ? null : Describe(card, index, "依赖前缀缺少合法的相邻收益");
            }
            // 0.2.0 放行：M:base/strength_scaled（BlockHandler 消费）
            // 0.9.0 放行：DamageModifierResolver 支持的伤害/命中修饰符（DamageHandler 消费）
            if (operation.Template == "M:base")
            {
                var modifierSpec = TryEffectiveSpec(card, index);
                if (modifierSpec is null)
                    return Describe(card, index, "修饰符无法解析执行视角 spec");
                if ((modifierSpec.Opcode != "modify_block" || modifierSpec.Variant != "strength_scaled")
                    && !DamageModifierResolver.IsSupportedModifier(modifierSpec))
                    return Describe(card, index, $"修饰符（{modifierSpec.Opcode}/{modifierSpec.Variant}）的数学未建模");
                return ValidateSpecShape(card, index, modifierSpec);
            }
            // 0.9.0：伤害/命中修饰符（由 DamageModifierResolver 在 DamageHandler 内结算）
            var dmgModSpec = TryEffectiveSpec(card, index);
            if (dmgModSpec is null)
                return Describe(card, index, "修饰符无法解析执行视角 spec");
            if (AutoAnthonyCombatSolverBridge.Translation.DamageModifierResolver.IsSupportedModifier(dmgModSpec))
                return ValidateSpecShape(card, index, dmgModSpec);
            return Describe(card, index, $"Modifier 操作（{operation.Template}）的修饰符数学未建模");
        }

        if (operation.Scope is OperationScope.AbilityTrigger or OperationScope.ConditionalTrigger)
        {
            var triggerSpec = TryEffectiveSpec(card, index);
            if (triggerSpec is null) return Describe(card, index, "触发器缺少结构化 spec");
            if (ChaosCardExhaustMirror.IsLifecycleTrigger(operation))
                return ChaosCardExhaustMirror.Validate(card, index);
            // 0.9.0：条件操作（opcode=condition）走 ConditionEvaluator 校验
            if (triggerSpec.Opcode == "condition" && triggerSpec.Condition is { } condition)
            {
                if (AutoAnthonyCombatSolverBridge.Translation.ConditionEvaluator.IsSupportedCondition(condition.Kind))
                    return null;    // 放行（执行循环中评估并门控 payoff）
                return Describe(card, index, $"条件 kind={condition.Kind} 不在支持矩阵");
            }
            // Batch AQ：ForEach 触发器（immediate——打出时对已消耗的卡执行收益）
            if (operation.Scope == OperationScope.ConditionalTrigger
                && triggerSpec.Opcode == "trigger"
                && triggerSpec.Trigger is { } foreachTrigger && IsForEachKind(foreachTrigger.Kind))
            {
                if (triggerSpec.Trigger.Lifetime != "immediate")
                    return Describe(card, index, $"ForEach 触发器 lifetime={triggerSpec.Trigger.Lifetime} 不在支持矩阵");
                return ValidateForEachPayload(card, index);
            }
            var triggerReason = ChaosTriggerPolicy.ValidateTrigger(operation, triggerSpec);
            return triggerReason is null ? null : Describe(card, index, triggerReason);
        }
        if (operation.Scope is OperationScope.AbilityRule)
        {
            var compositeRule = TryEffectiveSpec(card, index);
            if (compositeRule is { Opcode: "combat_rule" } && ChaosCompositePowerMirror.IsSupportedRule(compositeRule.Variant)
                && !operation.Parameters.ContainsKey("triggerIndex") && operation.CardTargetSlot is null)
                return ValidateSpecShape(card, index, compositeRule);
            if (ChaosOperationExecutor.RequiresCompositePower(operation)
                || operation.Parameters.ContainsKey("triggerIndex"))
                return Describe(card, index, "复合规则 Power 或条件规则尚未接入精确 hook");
            var ruleSpec = TryEffectiveSpec(card, index);
            if (ruleSpec is not null && ruleSpec.Opcode == "combat_rule"
                && ruleSpec.Variant is "a_proxyatomic_buffer" or "a_proxyatomic_parry"
                    or "a_proxyatomic_royalties" or "a_proxyatomic_calcify" or "a_proxyatomic_swordsage"
                    or "a_proxyatomic_forbiddengrimoire" or "kings_sword_hits_all" or "poison_extra_triggers"
                    or "derivative_bonus_damage" or "derivative_hits_all" or "played_skills_gain_sly" or "first_cards_free_each_turn")
                return ValidateSpecShape(card, index, ruleSpec);
            return Describe(card, index, "AbilityRule 操作不在支持矩阵");
        }

        if (operation.Parameters.ContainsKey("triggerIndex"))
        {
            var owner = operation.Parameters["triggerIndex"];
            if (owner < 0 || owner >= index || owner >= card.Generated.Operations.Count)
                return Describe(card, index, "触发器引用不是前序有效操作");
            var trigger = card.Generated.Operations[owner];
            var triggerSpec = TryEffectiveSpec(card, owner);
            if (triggerSpec is null) return Describe(card, index, "触发器缺少 spec");
            // 0.9.0：条件门控的 payoff——条件已校验通过，payoff 走正常 handler 校验
            var immediateCondition = trigger.Scope == OperationScope.ConditionalTrigger
                && triggerSpec.Opcode == "condition"
                && triggerSpec.Condition is { } condition
                && AutoAnthonyCombatSolverBridge.Translation.ConditionEvaluator.IsSupportedCondition(condition.Kind);
            // Batch AQ：ForEach 触发器的 payoff——由 ForEach 循环执行，走正常 handler 校验
            var forEachTrigger = trigger.Scope == OperationScope.ConditionalTrigger
                && triggerSpec.Opcode == "trigger"
                && triggerSpec.Trigger is { } foreachGate && IsForEachKind(foreachGate.Kind);
            // 即时条件只门控执行，不能绕过后面的 handler 支持检查。
            if (!immediateCondition && !forEachTrigger)
            {
                if (!ChaosCardExhaustMirror.IsLifecycleTrigger(trigger)
                    && ChaosTriggerPolicy.ValidateTrigger(trigger, triggerSpec) is not null)
                    return Describe(card, index, "收益引用了未适配触发器");
                var payloadSpec = TryEffectiveSpec(card, index);
                if (payloadSpec is null) return Describe(card, index, "触发收益缺少 spec");
                var payloadReason = ChaosTriggerPolicy.ValidatePayload(payloadSpec);
                if (payloadReason is not null) return Describe(card, index, payloadReason);
            }
        }

        if (operation.CardTargetSlot is not null && operation.Scope is OperationScope.AbilityRule)
            return Describe(card, index, "含玩家选牌槽位（0.5.0 解锁）");


        var spec = TryEffectiveSpec(card, index);
        if (spec is null)
            return Describe(card, index, "无法解析执行视角 spec");

        // 0.5.0：选择型操作放行（CardChoiceMirrors 在 OwnChoice 阶段解析；执行循环跳过）
        if (ChaosCardChoiceMirror.IsSupportedSelection(spec))
            return ValidateSpecShape(card, index, spec);

        var shapeReason = ValidateSpecShape(card, index, spec);
        if (shapeReason is not null)
            return shapeReason;

        var handler = OperationHandlerRegistry.Instance.TryGet(OperationKey.FromSpec(spec));
        if (handler is null)
            return Describe(card, index, $"opcode={spec.Opcode} variant={spec.Variant} 不在支持矩阵");

        var handlerReason = handler.ValidateSupport(new OperationShape(index, operation.Scope, spec));
        if (handlerReason is not null)
            return Describe(card, index, handlerReason);

        return null;
    }

    /// <summary>通用形状检查：Condition/Trigger、X 值源。返回 null 表示通过。</summary>
    private static string? ValidateSpecShape(ChaosCardModel card, int index, OperationRuntimeSpec spec)
    {
        if (spec.Condition is not null || spec.Trigger is not null)
            return Describe(card, index, "spec 携带 Condition/Trigger（0.6.0 解锁）");

        foreach (var slot in spec.Values)
        {
            // 0.4.0：X 源槽（energy_x/star_x/special_x）已支持——镜像层在执行前复刻
            // OnPlay 的 X 解析（Hook.ModifyXValue + SetResolvedXValues），值槽经
            // RuntimeSpecValue 自动读到解析后的 X 值。
            if (slot.Source is not ("fixed" or "energy_x" or "star_x" or "special_x"))
                return Describe(card, index, $"值槽 {slot.Id} 的 Source={slot.Source} 不在支持矩阵");
        }

        return null;
    }

    /// <summary>Batch AQ：ForEach 触发器的收益校验（复刻源码 ExecuteForEach 的消费面）。</summary>
    private static string? ValidateForEachPayload(ChaosCardModel card, int triggerIndex)
    {
        var operations = card.Generated.Operations;
        var hasPayload = false;
        for (var index = triggerIndex + 1; index < operations.Count; index++)
        {
            var effect = operations[index];
            if (!effect.Parameters.TryGetValue("triggerIndex", out var linked) || linked != triggerIndex) continue;
            hasPayload = true;
            if (effect.Scope is OperationScope.AbilityTrigger or OperationScope.ConditionalTrigger or OperationScope.AbilityRule)
                return Describe(card, index, "ForEach 收益是触发器/修饰符（嵌套不支持）");
            var spec = TryEffectiveSpec(card, index);
            if (spec is null) return Describe(card, index, "ForEach 收益缺少 spec");
            if (effect.Scope == OperationScope.Modifier || ChaosCardChoiceMirror.IsSupportedSelection(spec)) continue;
            var handler = OperationHandlerRegistry.Instance.TryGet(OperationKey.FromSpec(spec));
            if (handler is null) return Describe(card, index, $"ForEach 收益 {spec.Opcode}/{spec.Variant} 无 handler");
            var reason = handler.ValidateSupport(new OperationShape(index, effect.Scope, spec));
            if (reason is not null) return Describe(card, index, reason);
            var shapeReason = ValidateSpecShape(card, index, spec);
            if (shapeReason is not null) return shapeReason;
        }
        if (!hasPayload) return Describe(card, triggerIndex, "ForEach 触发器没有收益操作");
        return null;
    }

    /// <summary>Batch AQ：ForEach 触发器执行（复刻源码 L383-401 ExecuteForEach）。</summary>
    private static bool ExecuteForEach(ChaosCardModel card, int triggerIndex,
        CardOnPlayMirrorContext context, OperationResolutionState resolution)
    {
        var triggerKind = TryEffectiveSpec(card, triggerIndex)?.Trigger?.Kind;
        // 源码 L387-388：state.ExhaustedByCard 按触发器 kind 过滤类型
        var items = triggerKind == "for_each_discarded_card" ? resolution.DiscardedByCard.ToList()
            : resolution.ExhaustedByCard.Where(candidate => ExhaustedCardMatchesTrigger(triggerKind, candidate.Preview.Type)).ToList();
        var energyCount = triggerKind == "energy_spent_this_turn_excluding_self"
            ? Math.Max(0, ((SimulatedCombatState)context.CombatState).GetEnergySpentThisTurn(card.Owner) - Math.Max(0, context.CardPlay.Resources.EnergySpent))
                / Math.Max(1, ChaosOperationExecutor.RuntimeSpecValue(card, triggerIndex, "threshold", 1)) : -1;
        var count = energyCount >= 0 ? energyCount : items.Count;
        for (var iteration = 0; iteration < count; iteration++)
        {
            if (energyCount < 0) resolution.IterationCard = items[iteration];
            if (!ChaosClauseMirror.Execute(context.Simulator, context.Card, card, context.CardPlay, triggerIndex, resolution,
                null, 0, context.CardPlay.Target, powered: false, corruptionExhaust: false, triggered: false)) return false;
        }
        if (energyCount < 0) resolution.IterationCard = null;
        return true;
    }

    private static bool IsForEachKind(string kind) => kind is "for_each_exhausted_card" or "for_each_exhausted_non_attack"
        or "for_each_exhausted_status" or "for_each_discarded_card" or "energy_spent_this_turn_excluding_self";

    /// <summary>源码 L403-408：按触发器 kind 过滤消耗卡的类型。</summary>
    private static bool ExhaustedCardMatchesTrigger(string? triggerKind, CardType cardType) => triggerKind switch
    {
        "for_each_exhausted_non_attack" => cardType != CardType.Attack,
        "for_each_exhausted_status" => cardType == CardType.Status,
        _ => true
    };

    /// <summary>解析执行视角 spec；旧存档无 spec 时返回 null（由调用方转成不支持原因）。</summary>
    private static OperationRuntimeSpec? TryEffectiveSpec(ChaosCardModel card, int index)
    {
        try
        {
            return ChaosOperationExecutor.EffectiveRuntimeSpec(card, index);
        }
        catch (Exception exception)
        {
            // RequireStructured 对无 spec 的旧存档操作抛 InvalidDataException——转成干净的不支持原因
            BridgeLog.Warn($"EffectiveRuntimeSpec 解析失败（{card.Id} 操作[{index}]）：{exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    private static string Describe(ChaosCardModel card, int? index, string reason)
    {
        // 异常消息路径：不再触发 Definition 解析（避免掩盖原始原因），只用卡牌 Id。
        var location = index is { } i
            ? $"操作[{i}] template={card.Generated.Operations[i].Template}"
            : "整卡";
        return $"AutoAnthony 生成卡 {card.Id}：{location}：{reason}。" +
               "该卡不在桥接支持矩阵内，为避免错误预测已中止本次搜索。";
    }
}
