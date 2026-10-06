using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using MegaCrit.Sts2.Core.Entities.Cards;
using AutoAnthonyCombatSolverBridge.Diagnostics;
using AutoAnthonyCombatSolverBridge.Translation;

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
        var reason = ValidateCard(card);
        if (reason is not null)
            throw PredictionUnsupportedException.ForContent(reason, typeof(ChaosCardModel));

        var operations = card.Generated.Operations;
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            // 校验层已保证只剩可执行操作；这里防御性地重复 Play 的元数据跳过规则
            if (operation.Scope is OperationScope.Modifier or OperationScope.AbilityTrigger
                or OperationScope.ConditionalTrigger or OperationScope.AbilityRule)
                continue;
            if (operation.Template is "N_SELECT_HAND_CARD" or "N_SELECT_HAND_ATTACK")
                continue;

            var spec = ChaosOperationExecutor.EffectiveRuntimeSpec(card, index);
            var handler = OperationHandlerRegistry.Instance.TryGet(OperationKey.FromSpec(spec));
            if (handler is null)
                throw PredictionUnsupportedException.ForContent(
                    Describe(card, index, $"opcode={spec.Opcode} variant={spec.Variant} 无注册 handler（校验层遗漏，属桥的 bug）"),
                    typeof(ChaosCardModel));

            handler.Execute(new OperationExecutionContext(context, card, new OperationShape(index, operation.Scope, spec)));
        }
    }

    // --- 整卡预校验（fail-closed）--------------------------------------------------------------

    private static string? ValidateCard(ChaosCardModel card)
    {
        if (card.EnergyCost.CostsX || card.HasStarCostX)
            return Describe(card, null, "X 费卡不在支持矩阵（0.4.0 解锁）");

        var operations = card.Generated.Operations;
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            var reason = ValidateOperation(card, index, operation);
            if (reason is not null)
                return reason;
        }

        if (operations.Any(ChaosOperationExecutor.RequiresCompositePower))
            return Describe(card, null, "需要武装 ChaosCompositePower（触发器），0.6.0 解锁");

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
            // 0.2.0 唯一放行的修饰符：M:base/strength_scaled（由 BlockHandler 消费其数学，
            // 执行循环中与其他 Modifier 一样被跳过——与源码 Play 的跳过规则一致）
            if (operation.Template != "M:base")
                return Describe(card, index, $"Modifier 操作（{operation.Template}）的修饰符数学未建模");
            var modifierSpec = TryEffectiveSpec(card, index);
            if (modifierSpec is null)
                return Describe(card, index, "修饰符无法解析执行视角 spec");
            if (modifierSpec.Opcode != "modify_block" || modifierSpec.Variant != "strength_scaled")
                return Describe(card, index, $"修饰符（{modifierSpec.Opcode}/{modifierSpec.Variant}）的数学未建模");
            return ValidateSpecShape(card, index, modifierSpec);
        }

        if (operation.Scope is OperationScope.AbilityTrigger or OperationScope.ConditionalTrigger)
            return Describe(card, index, $"触发类操作（scope={operation.Scope}，0.6.0 解锁）");
        if (operation.Scope is OperationScope.AbilityRule)
            return Describe(card, index, "AbilityRule 操作不在支持矩阵");

        if (operation.Parameters.ContainsKey("triggerIndex"))
            return Describe(card, index, "挂靠触发器条件的操作（0.6.0 解锁）");

        if (operation.CardTargetSlot is not null)
            return Describe(card, index, "含玩家选牌槽位（0.5.0 解锁）");

        if (operation.Template == "R:EndTurn")
            return Describe(card, index, "结束回合操作不在支持矩阵");

        var spec = TryEffectiveSpec(card, index);
        if (spec is null)
            return Describe(card, index, "无法解析执行视角 spec");

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

    /// <summary>通用形状检查：Condition/Trigger、随机引用、X 值源。返回 null 表示通过。</summary>
    private static string? ValidateSpecShape(ChaosCardModel card, int index, OperationRuntimeSpec spec)
    {
        if (spec.Condition is not null || spec.Trigger is not null)
            return Describe(card, index, "spec 携带 Condition/Trigger（0.6.0 解锁）");

        if (spec.Flags.Contains("random_enemy_reference"))
            return Describe(card, index, "随机目标引用（0.4.0 解锁）");

        foreach (var slot in spec.Values)
        {
            if (slot.Source != "fixed")
                return Describe(card, index, $"值槽 {slot.Id} 的 Source={slot.Source}（X 值，0.4.0 解锁）");
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
