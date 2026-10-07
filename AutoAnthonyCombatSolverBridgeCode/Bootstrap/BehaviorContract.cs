using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoAnthonyCombatSolverBridge.Bootstrap;

// Compare executable contracts, not DLL packaging identity. Metadata tokens are resolved
// to names so adding unrelated code or changing assembly versions does not invalidate IL.
internal static class BehaviorContract
{
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields()
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    internal static SortedDictionary<string, string> Capture(Assembly assembly, IEnumerable<string> roots)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in roots.Distinct().Order())
            if (assembly.GetType(name) is { } type) Visit(type);
        return result;

        void Visit(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
                result[$"{type.FullName}::{method}"] = Fingerprint(method);
            foreach (var field in type.GetFields(flags))
                result[$"{type.FullName}::field {field.Name}"] = $"{field.FieldType}|{field.Attributes}|" +
                    (field.IsLiteral ? JsonSerializer.Serialize(field.GetRawConstantValue()) : "");
            foreach (var nested in type.GetNestedTypes(flags)) Visit(nested);
        }
    }

    internal static void Compare(IReadOnlyDictionary<string, string> baseline, IReadOnlyDictionary<string, string> current,
        string name, List<string> failures)
    {
        foreach (var (member, fingerprint) in baseline)
            if (!current.TryGetValue(member, out var actual) || actual != fingerprint)
                failures.Add($"{name}：已适配行为契约变化：{member}。需要复查对应结算逻辑。");
    }

    internal static void Verify(Assembly assembly, List<string> failures)
    {
        using var stream = typeof(BehaviorContract).Assembly.GetManifestResourceStream("BridgeBehaviorBaseline.json");
        if (stream is null) { failures.Add("桥缺少行为契约基线。"); return; }
        var baseline = JsonSerializer.Deserialize<Dictionary<string, SortedDictionary<string, string>>>(stream)!;
        var name = assembly.GetName().Name!;
        if (!baseline.TryGetValue(name, out var expected) || expected.Count == 0)
        { failures.Add($"{name}：行为契约基线为空。"); return; }
        // Recover top-level roots from baseline keys; additions outside these roots are irrelevant.
        var roots = expected.Keys.Select(k => k[..k.IndexOf("::", StringComparison.Ordinal)].Split('+')[0]).Distinct();
        Compare(expected, Capture(assembly, roots), name, failures);
    }

    private static string Identity(MemberInfo member) => member switch
    {
        Type type => type.ToString(),
        _ => $"{member.DeclaringType}::{member}"
    };

    internal static string Fingerprint(MethodBase method)
    {
        var body = method.GetMethodBody();
        var text = new StringBuilder().Append(method.Attributes).Append('|').Append(method.CallingConvention);
        if (body is not null)
        {
            text.Append('|').Append(body.InitLocals).Append('|').Append(body.MaxStackSize);
            foreach (var local in body.LocalVariables) text.Append('|').Append(local.LocalType).Append(':').Append(local.IsPinned);
            var il = body.GetILAsByteArray()!;
            var typeArgs = method.DeclaringType?.GetGenericArguments();
            var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
            for (var p = 0; p < il.Length;)
            {
                short code = il[p++];
                if (code == 0xfe) code = (short)(0xfe00 | il[p++]);
                var op = Opcodes[code];
                text.Append('|').Append(op.Name).Append(':');
                var size = op.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, p),
                    _ => 4
                };
                if (op.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType)
                    text.Append(Identity(method.Module.ResolveMember(BitConverter.ToInt32(il, p), typeArgs, methodArgs)!));
                else if (op.OperandType == OperandType.InlineString)
                    text.Append(JsonSerializer.Serialize(method.Module.ResolveString(BitConverter.ToInt32(il, p))));
                else if (op.OperandType == OperandType.InlineSig)
                    text.Append(Convert.ToHexString(method.Module.ResolveSignature(BitConverter.ToInt32(il, p))));
                else text.Append(Convert.ToHexString(il.AsSpan(p, size)));
                p += size;
            }
            foreach (var clause in body.ExceptionHandlingClauses)
            {
                text.Append('|').Append(clause.Flags).Append(':').Append(clause.TryOffset).Append(':').Append(clause.TryLength)
                    .Append(':').Append(clause.HandlerOffset).Append(':').Append(clause.HandlerLength);
                if (clause.Flags == ExceptionHandlingClauseOptions.Clause) text.Append(':').Append(clause.CatchType);
                if (clause.Flags == ExceptionHandlingClauseOptions.Filter) text.Append(':').Append(clause.FilterOffset);
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
