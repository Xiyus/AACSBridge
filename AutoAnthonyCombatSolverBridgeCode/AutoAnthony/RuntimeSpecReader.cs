using System.Text;
using ChaosCardGenerator;

namespace AutoAnthonyCombatSolverBridge.AutoAnthony;

/// <summary>
/// 把解析后的生成卡及其 OperationRuntimeSpec 格式化为稳定、可 grep 的日志行。
/// 字段顺序与 RuntimeSpec record 一致，便于跨局 diff：这里打印的每个字段都是结构化执行数据
/// ——刻意不含本地化文本。
/// </summary>
public static class RuntimeSpecReader
{
    public static string DescribeCard(ResolvedChaosCard card)
    {
        var model = card.Model;
        var generated = card.Generated;
        var specCount = 0;
        foreach (var spec in card.RuntimeSpecs)
            if (spec is not null) specCount++;

        var sb = new StringBuilder("CHAOS_CARD ");
        sb.Append($"id={model.Id} title=\"{model.Title}\" type={model.Type} ");
        sb.Append($"cost={generated.Cost} star_cost={generated.StarCost} star_x={generated.HasStarCostX} ");
        sb.Append($"target={generated.Target} rarity={generated.Rarity} character={generated.Character} ");
        sb.Append($"slot={card.Definition.Slot} operations={card.OperationCount} specs_present={specCount} persisted_specs={card.PersistedSpecs?.Count ?? 0}");
        if (card.Generated.Tags is { Count: > 0 } tags)
            sb.Append($" tags=[{string.Join(",", tags)}]");
        return sb.ToString();
    }

    public static string DescribeOperation(int index, GeneratorOperation operation)
    {
        var sb = new StringBuilder($"  op[{index}] ");
        sb.Append($"template=\"{operation.Template}\" scope={operation.Scope} ");
        sb.Append($"spec={(operation.RuntimeSpec is null ? "MISSING" : "present")}");
        if (operation.Parameters is { Count: > 0 } parameters)
        {
            var pairs = string.Join(",", parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
            sb.Append($" params=[{pairs}]");
        }
        if (operation.RequiresSingleTarget)
            sb.Append(" requires_single_target");
        if (operation.CardTargetSlot is { } targetSlot)
            sb.Append($" card_target_slot={targetSlot}");
        return sb.ToString();
    }

    public static string DescribeSpec(OperationRuntimeSpec spec)
    {
        var sb = new StringBuilder("    spec ");
        sb.Append($"schema={spec.SchemaVersion} opcode={spec.Opcode} variant={VariantOrNone(spec.Variant)} target={spec.Target} ");
        sb.Append($"zones={spec.SourceZone}->{spec.DestinationZone} filter={spec.CardFilter} ");
        sb.Append($"flags=[{string.Join(",", spec.Flags)}] ");
        sb.Append($"values=[{string.Join(",", spec.Values.Select(DescribeValue))}] ");
        sb.Append($"condition={DescribeCondition(spec.Condition)} ");
        sb.Append($"trigger={DescribeTrigger(spec.Trigger)}");
        return sb.ToString();
    }

    public static string DescribeValue(RuntimeValueSlot slot)
        => $"{slot.Id}={slot.BaseValue} src={slot.Source} off={slot.Offset}{(slot.Upgradable ? " up" : "")}";

    public static string DescribeCondition(RuntimeConditionSpec? condition)
        => condition is null
            ? "-"
            : $"{condition.Kind}(subject={condition.Subject}{(condition.ValueSlot is { } valueSlot ? $", slot={valueSlot}" : "")})";

    public static string DescribeTrigger(RuntimeTriggerSpec? trigger)
        => trigger is null
            ? "-"
            : $"{trigger.Kind}/lifetime={trigger.Lifetime}" +
               (trigger.ThresholdSlot is { } threshold ? $" threshold={threshold}" : "") +
               (trigger.DurationSlot is { } duration ? $" duration={duration}" : "");

    private static string VariantOrNone(string? variant)
        => string.IsNullOrEmpty(variant) ? "-" : variant;
}
