using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
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

        var operations = card.Generated.Operations;
        // 0.9.0：条件结果缓存（EvaluateConditionOnce 语义——按索引一次评估）
        var conditionResults = new Dictionary<int, bool>();
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            // 0.9.0：条件操作——评估并缓存，不执行（复刻源码 L178-190）
            if (operation.Scope == OperationScope.ConditionalTrigger)
            {
                var conditionSpec = TryEffectiveSpec(card, index);
                if (conditionSpec?.Condition is { } condition
                    && ConditionEvaluator.IsSupportedCondition(condition.Kind))
                {
                    if (!conditionResults.TryGetValue(index, out var cached))
                        conditionResults[index] = ConditionEvaluator.Evaluate(card, operation, context, conditionResults);
                }
                continue;
            }
            // 校验层已保证只剩可执行操作；这里防御性地重复 Play 的元数据跳过规则
            if (operation.Scope is OperationScope.Modifier or OperationScope.AbilityTrigger
                or OperationScope.AbilityRule)
                continue;
            if (operation.Template is "N_SELECT_HAND_CARD" or "N_SELECT_HAND_ATTACK")
                continue;
            // 0.9.0：payoff 的条件门控（复刻源码 L192-200）——triggerIndex 指向条件操作
            if (operation.Parameters.TryGetValue("triggerIndex", out var gateIndex)
                && gateIndex >= 0 && gateIndex < operations.Count)
            {
                var gate = operations[gateIndex];
                // 只门控普通条件（AbilityTrigger/持久触发器仍跳过——武装为 Power）
                if (gate.Scope == OperationScope.ConditionalTrigger)
                {
                    if (!conditionResults.TryGetValue(gateIndex, out var gateResult) || !gateResult)
                        continue;    // 条件 false → 跳过 payoff
                }
                else if (gate.Scope is OperationScope.AbilityTrigger)
                    continue;    // 持久触发器的 payoff 不在本场执行
            }

            var spec = ChaosOperationExecutor.EffectiveRuntimeSpec(card, index);

            // 0.5.0：选择型操作跳过——由 CardChoiceMirrors 在 OwnChoice 阶段解析
            // （求解器展开分支 + 原生 Effect 施加，与源码 CardCmd/CardPileCmd 语义一致）
            if (ChaosCardChoiceMirror.IsSupportedSelection(spec))
                continue;

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
                new OperationShape(index, operation.Scope, spec), resolvedRandomTarget));
        }
        ChaosCompositePowerMirror.Arm(card, context);
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
        if (selectionCount > 1)
            return Describe(card, null, "含多个选择型操作（0.5.0 只支持单选卡）");
        if (selectionIndex >= 0 && operations.Skip(selectionIndex + 1).Any(op => op.Scope != OperationScope.Modifier))
            return Describe(card, selectionIndex, "选牌后还有操作；OwnChoice 的末尾结算会改变触发事件顺序");

        if (operations.Any(ChaosOperationExecutor.RequiresCompositePower))
        {
            if (card.RuntimeProfileId.Length != 0)
                return Describe(card, null, "外部角色 Profile 的触发器尚未适配");
            if (operations.Any(op => op.Scope == OperationScope.Modifier))
                return Describe(card, null, "带触发器的卡暂不支持 Modifier");
            if (selectionCount > 0)
                return Describe(card, null, "带触发器的卡暂不支持选牌，避免 OwnChoice 重排武装时机");
        }

        if (card.IsUpgraded && card.Generated.Upgrade is { } upgrade)
        {
            foreach (var effect in upgrade.Effects)
            {
                if (effect.Kind is CardUpgradeKind.RepeatOperation or CardUpgradeKind.ExecuteOperationOnPlay
                    or CardUpgradeKind.ChooseExhaust or CardUpgradeKind.UpgradeDerivative
                    or CardUpgradeKind.UpgradeGeneratedCards or CardUpgradeKind.SelectAllCards
                    or CardUpgradeKind.IncreaseAllX)
                    return Describe(card, null, $"结构性升级 {effect.Kind} 不在支持矩阵");
            }
        }

        return null;
    }

    private static string? ValidateOperation(ChaosCardModel card, int index, GeneratorOperation operation)
    {
        if (operation.Template is "N_SELECT_HAND_CARD" or "N_SELECT_HAND_ATTACK")
            return Describe(card, index, "含玩家选牌选择器（0.5.0 解锁）");

        if (operation.Scope is OperationScope.Modifier)
        {
            // 0.2.0 放行：M:base/strength_scaled（BlockHandler 消费）
            // 0.9.0 放行：DamageModifierResolver 支持的伤害/命中修饰符（DamageHandler 消费）
            if (operation.Template == "M:base")
            {
                var modifierSpec = TryEffectiveSpec(card, index);
                if (modifierSpec is null)
                    return Describe(card, index, "修饰符无法解析执行视角 spec");
                if (modifierSpec.Opcode != "modify_block" || modifierSpec.Variant != "strength_scaled")
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
            // 0.9.0：条件操作（opcode=condition）走 ConditionEvaluator 校验
            if (triggerSpec.Opcode == "condition" && triggerSpec.Condition is { } condition)
            {
                if (AutoAnthonyCombatSolverBridge.Translation.ConditionEvaluator.IsSupportedCondition(condition.Kind))
                    return null;    // 放行（执行循环中评估并门控 payoff）
                return Describe(card, index, $"条件 kind={condition.Kind} 不在支持矩阵");
            }
            var triggerReason = ChaosTriggerPolicy.ValidateTrigger(operation, triggerSpec);
            return triggerReason is null ? null : Describe(card, index, triggerReason);
        }
        if (operation.Scope is OperationScope.AbilityRule)
            return Describe(card, index, "AbilityRule 操作不在支持矩阵");

        if (operation.Parameters.ContainsKey("triggerIndex"))
        {
            var owner = operation.Parameters["triggerIndex"];
            if (owner < 0 || owner >= index || owner >= card.Generated.Operations.Count)
                return Describe(card, index, "触发器引用不是前序有效操作");
            var trigger = card.Generated.Operations[owner];
            var triggerSpec = TryEffectiveSpec(card, owner);
            if (triggerSpec is null) return Describe(card, index, "触发器缺少 spec");
            // 0.9.0：条件门控的 payoff——条件已校验通过，payoff 走正常 handler 校验
            if (trigger.Scope == OperationScope.ConditionalTrigger
                && triggerSpec.Opcode == "condition"
                && triggerSpec.Condition is { } condition
                && AutoAnthonyCombatSolverBridge.Translation.ConditionEvaluator.IsSupportedCondition(condition.Kind))
                return null;    // 条件门控 payoff 放行（执行时按条件结果跳过）
            // 触发器门控的 payoff——走触发器校验
            if (ChaosTriggerPolicy.ValidateTrigger(trigger, triggerSpec) is not null)
                return Describe(card, index, "收益引用了未适配触发器");
            var payloadSpec = TryEffectiveSpec(card, index);
            if (payloadSpec is null) return Describe(card, index, "触发收益缺少 spec");
            var payloadReason = ChaosTriggerPolicy.ValidatePayload(payloadSpec);
            if (payloadReason is not null) return Describe(card, index, payloadReason);
        }

        if (operation.CardTargetSlot is not null)
            return Describe(card, index, "含玩家选牌槽位（0.5.0 解锁）");

        if (operation.Template == "R:EndTurn")
            return Describe(card, index, "结束回合操作不在支持矩阵");

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
