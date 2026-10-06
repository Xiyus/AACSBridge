using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using HarmonyLib;

namespace AutoAnthonyCombatSolverBridge.Bootstrap;

/// <summary>守卫对其中一个被适配 Mod 观察到的全部事实。</summary>
public sealed record BridgeModFacts(
    string DisplayName,
    string? AssemblyPath,
    Version? ManifestVersion,
    Guid? Mvid,
    string? Sha256,
    int? SpecSchemaVersion,
    int? ComponentApiVersion,
    IReadOnlyList<string> MissingTypes,
    IReadOnlyList<string> MissingMembers);

/// <summary>启动自检结果。Fail closed：全部检查通过 IsCompatible 才为 true。</summary>
public sealed record CompatibilityReport(
    bool IsCompatible,
    BridgeModFacts AutoAnthony,
    BridgeModFacts CombatSolver,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Notes)
{
    public const string MinimumAutoAnthonyVersion = "0.3.137";
    public const string TestedAutoAnthonyVersion = "0.3.137";
    public const string MinimumCombatSolverVersion = "0.50.0";
    public const string TestedCombatSolverVersion = "0.50.0";
}

/// <summary>
/// 启动期兼容性自检。在任何 Harmony 补丁应用之前、任何 CombatSolver 注册表被触碰之前运行，
/// 且全程纯反射（对两个 Mod 零编译期引用）——这样在 Mod 缺失时能安全报告"未加载"，
/// 而不是抛 TypeLoadException 崩掉。
///
/// 纪律（来自 CombatSolver 第三方适配文档与 AutoAnthony 的 COMPONENT_API）：
///  - 锁定依赖版本，并在运行期核对方法/字段签名；
///  - 自检不通过时一个镜像都不注册——部分适配比完全不适配更危险；
///  - 记录程序集 MVID + SHA256，让玩家和开发者能核对桥实际运行的二进制。
/// </summary>
public static class CompatibilityGuard
{
    // --- AutoAnthony 契约：桥依赖的类型与成员 -----------------------------------------------
    private static readonly (string Type, string[] Members)[] AutoAnthonyContract =
    [
        ("AutoAnthony.ChaosCardModel", ["Generated", "Definition", "AfterCardEnteredCombat", "OnPlay", "IsClone"]),
        ("AutoAnthony.ChaosCardDefinition", ["Slot", "Card", "RuntimeSpecs", "UpgradeValueSlots"]),
        ("AutoAnthony.ChaosCardRegistry", ["IsGeneratedCardId", "TryGetGeneratedCardSlot", "Types", "TypesFor"]),
        ("AutoAnthony.ChaosRunDefinitions", ["ForSlot", "GetCards", "GetAllCards"]),
        ("AutoAnthony.ChaosOperationExecutor", ["Play", "EffectiveRuntimeSpec", "RequiresCompositePower"]),
        ("AutoAnthony.ChaosCompositePower", ["Definition", "Configure", "FireTriggers"]),
        ("AutoAnthony.ComponentRuntimeApi", ["Register", "RegisterPackage", "ApiVersion"]),
        ("ChaosCardGenerator.GeneratedCard", ["Operations", "Cost", "Type", "Target", "Rarity", "Character", "StarCost", "HasStarCostX", "Tags"]),
        ("ChaosCardGenerator.GeneratorOperation", ["Template", "Scope", "Parameters", "RuntimeSpec", "CardTargetSlot", "RequiresSingleTarget"]),
        ("ChaosCardGenerator.OperationRuntimeSpec",
            ["SchemaVersion", "Opcode", "Variant", "Target", "SourceZone", "DestinationZone", "CardFilter", "Flags", "Values", "Condition", "Trigger", "CurrentSchemaVersion", "Validate", "StableSignature"]),
        ("ChaosCardGenerator.RuntimeValueSlot", ["Id", "BaseValue", "Source", "Offset", "Upgradable", "Explicit"]),
        ("ChaosCardGenerator.RuntimeConditionSpec", ["Kind", "Subject", "ValueSlot"]),
        ("ChaosCardGenerator.RuntimeTriggerSpec", ["Kind", "Lifetime", "ThresholdSlot", "DurationSlot"]),
        ("ChaosCardGenerator.ComponentApi", ["ApiVersion"]),
        ("ChaosCardGenerator.OperationRuntimeSpecCompiler", ["RequireStructured", "GetOrCompile"]),
        ("ChaosCardGenerator.GeneratedCharacter", []),
    ];

    // --- CombatSolver 契约：0.1.0 注册将要写入的适配面 ---------------------------------------
    private static readonly (string Type, string[] Members)[] CombatSolverContract =
    [
        ("CombatSolver.Entry", ["ModId", "Initialize"]),
        ("CombatSolver.AdaptedCardOnPlayMirrors", ["Register", "Seal", "DescribeRegisteredCompositions"]),
        ("CombatSolver.ModelPredictionStateMirrors", ["RegisterRelic", "RegisterModifier", "Get"]),
        ("CombatSolver.PredictionModHookSubscriberCapture", ["Capture", "KnownPreRootSubscriberTypeNames"]),
        ("CombatSolver.PredictionModPatchAudit", ["CaptureCardOnPlay", "RegisterAdaptedMonsterMachine"]),
        ("CombatSolver.SimulatedCombatState", ["Apply", "ApplyPower"]),
        ("CombatSolver.StrategicEffectMirrors", ["Register"]),
        ("CombatSolver.PowerHiddenStateMirrors", ["Register", "RegisterRootCapture"]),
        ("CombatSolver.CardChoiceMirrors", ["Register"]),
        ("CombatSolver.PowerDynamicVarWarmup", ["RegisterAdaptedCanonicalPower"]),
        ("CombatSolver.IncompatibleGameplayModException", []),
        ("CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay.CardOnPlayMirrors", ["Registry"]),
        ("CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay.CardOnPlayMirrorContext", ["CardPlay", "Target", "OwnerState"]),
        ("CombatSolver.Engine.InCombat.Mirrors.Cards.CardIsPlayableMirrors", ["Registry"]),
        ("CombatSolver.Engine.InCombat.Simulation.CombatPredictionSimulator",
            ["Damage", "GainBlock", "GainEnergy", "LoseEnergy", "GainStars", "Draw", "Shuffle", "Discard", "Exhaust", "Heal", "Kill", "Upgrade"]),
        ("CombatSolver.Engine.InCombat.Simulation.CombatPredictionRngSet", []),
        ("CombatSolver.Engine.Common.Mirrors.MethodMirrorRegistry`2", ["Register", "RegisterIgnored", "RegisterInferrer", "Invoke"]),
        ("CombatSolver.Engine.Common.Mirrors.MethodMirrorRegistry`3", ["Register", "RegisterIgnored"]),
        ("CombatSolver.Engine.Common.Mirrors.ThirdPartyMirrorRegistration", ["Register", "RegisterResult"]),
        ("CombatSolver.Engine.InCombat.Mirrors.CombatMirrorContext`1", ["Simulator", "State", "Rng", "StateStore", "History", "CombatState"]),
        ("CombatSolver.Engine.Common.PredictionUnsupportedException", ["ForContent"]),
    ];

    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

    public static CompatibilityReport Run()
    {
        var failures = new List<string>();
        var notes = new List<string>();

        var aaAssembly = FindLoadedAssembly("AutoAnthony");
        var csAssembly = FindLoadedAssembly("CombatSolver");

        var aa = InspectMod("AutoAnthony", aaAssembly, "AutoAnthony.json", AutoAnthonyContract, failures, notes);
        var cs = InspectMod("CombatSolver", csAssembly, "CombatSolver.json", CombatSolverContract, failures, notes);

        aa = VerifyAutoAnthonyVersions(aaAssembly, aa, failures, notes);
        VerifyCombatSolverVersions(csAssembly, cs, failures, notes);
        AuditChaosCardOnPlayPatches(aaAssembly, notes);

        return new CompatibilityReport(failures.Count == 0, aa, cs, failures, notes);
    }

    // --- 程序集发现 --------------------------------------------------------------------------

    private static Assembly? FindLoadedAssembly(string simpleName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(assembly.GetName().Name, simpleName, StringComparison.Ordinal))
                return assembly;
        }
        return null;
    }

    // --- 单 Mod 检查 -------------------------------------------------------------------------

    private static BridgeModFacts InspectMod(
        string displayName,
        Assembly? assembly,
        string manifestFileName,
        (string Type, string[] Members)[] contract,
        List<string> failures,
        List<string> notes)
    {
        if (assembly is null)
        {
            failures.Add($"{displayName} 程序集未加载。桥要求该 Mod 已安装并先行加载（manifest 依赖应保证这一点）。");
            return new BridgeModFacts(displayName, null, null, null, null, null, null, [], []);
        }

        var missingTypes = new List<string>();
        var missingMembers = new List<string>();
        foreach (var (typeName, members) in contract)
        {
            var type = assembly.GetType(typeName, throwOnError: false);
            if (type is null)
            {
                missingTypes.Add(typeName);
                continue;
            }
            foreach (var member in members)
            {
                if (type.GetMember(member, AllMembers).Length == 0)
                    missingMembers.Add($"{typeName}.{member}");
            }
        }

        foreach (var missing in missingTypes)
            failures.Add($"{displayName}：类型缺失：{missing}");
        foreach (var missing in missingMembers)
            failures.Add($"{displayName}：成员缺失：{missing}");

        var path = SafeLocation(assembly);
        return new BridgeModFacts(
            displayName,
            path,
            ReadManifestVersion(path, manifestFileName),
            SafeMvid(assembly),
            path is null ? null : ComputeSha256(path),
            null,
            null,
            missingTypes,
            missingMembers);
    }

    // --- 版本锁定 -----------------------------------------------------------------------------

    private static BridgeModFacts VerifyAutoAnthonyVersions(Assembly? assembly, BridgeModFacts facts, List<string> failures, List<string> notes)
    {
        if (assembly is null) return facts;

        int? schemaVersion = null;
        int? componentApiVersion = null;

        // RuntimeSpec schema：转储和后续所有翻译步骤都假设 schema 1。
        var specType = assembly.GetType("ChaosCardGenerator.OperationRuntimeSpec", throwOnError: false);
        var schemaField = specType?.GetField("CurrentSchemaVersion", BindingFlags.Public | BindingFlags.Static);
        if (schemaField?.GetValue(null) is int schema)
        {
            schemaVersion = schema;
            if (schema != 1)
                failures.Add($"AutoAnthony：OperationRuntimeSpec.CurrentSchemaVersion 为 {schema}，预期为 1。RuntimeSpec 契约已变化，桥需要复查后才能安全读取 spec。");
            notes.Add($"AutoAnthony RuntimeSpec schema = {schema}。");
        }

        // 组件创作 API：仅供参考——桥读取 spec，不创作组件。
        var apiType = assembly.GetType("ChaosCardGenerator.ComponentApi", throwOnError: false);
        var apiField = apiType?.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static);
        if (apiField?.GetValue(null) is int api)
        {
            componentApiVersion = api;
            if (api != 3)
                notes.Add($"AutoAnthony ComponentApi.ApiVersion = {api}（桥基于版本 3 开发）：请复查组件目录契约。");
            else
                notes.Add($"AutoAnthony ComponentApi.ApiVersion = {api}。");
        }

        CompareVersion(facts, CompatibilityReport.MinimumAutoAnthonyVersion, CompatibilityReport.TestedAutoAnthonyVersion, "AutoAnthony", failures, notes);
        return facts with { SpecSchemaVersion = schemaVersion, ComponentApiVersion = componentApiVersion };
    }

    private static void VerifyCombatSolverVersions(Assembly? assembly, BridgeModFacts facts, List<string> failures, List<string> notes)
    {
        if (assembly is null) return;
        CompareVersion(facts, CompatibilityReport.MinimumCombatSolverVersion, CompatibilityReport.TestedCombatSolverVersion, "CombatSolver", failures, notes);
    }

    private static void CompareVersion(BridgeModFacts facts, string minimum, string tested, string displayName, List<string> failures, List<string> notes)
    {
        if (facts.ManifestVersion is null)
        {
            failures.Add($"{displayName}：无法读取 manifest 版本（预期至少 {minimum}）。桥有意锁定版本——拒绝在无法识别的构建上运行。");
            return;
        }

        if (facts.ManifestVersion < Version.Parse(minimum))
        {
            failures.Add($"{displayName}：版本 {facts.ManifestVersion} 低于要求的最低版本 {minimum}。");
            return;
        }

        if (facts.ManifestVersion != Version.Parse(tested))
            notes.Add($"{displayName}：版本 {facts.ManifestVersion} 新于已测试的 {tested}——契约检查已通过，但信任预测前应重跑 strict-diff 夹具。");
        else
            notes.Add($"{displayName}：版本 {facts.ManifestVersion} 与锁定的测试版本一致。");
    }

    // --- Harmony 补丁审计 ---------------------------------------------------------------------

    /// <summary>
    /// CombatSolver 会审计根可达卡牌 OnPlay 上的 Harmony 补丁。AutoAnthony 本体不打
    /// ChaosCardModel.OnPlay 补丁（它直接重写虚方法），但伴侣 Mod（如 AutoAnthonyCardTinkering）
    /// 会补丁相关方法。一旦 OnPlay 上出现补丁，0.1.0 的注册就必须改走 AdaptedCardOnPlayMirrors
    /// 并声明完整补丁组合，而不是普通 CardOnPlayMirrors 注册表——所以现在先记录状态。
    /// </summary>
    private static void AuditChaosCardOnPlayPatches(Assembly? aaAssembly, List<string> notes)
    {
        if (aaAssembly is null) return;
        try
        {
            var chaosType = aaAssembly.GetType("AutoAnthony.ChaosCardModel", throwOnError: false);
            var onPlay = chaosType is null ? null : AccessTools.Method(chaosType, "OnPlay");
            if (onPlay is null)
            {
                notes.Add("未找到 ChaosCardModel.OnPlay，无法进行 Harmony 补丁审计。");
                return;
            }

            var patches = Harmony.GetPatchInfo(onPlay);
            if (patches is null || patches.Owners.Count == 0)
            {
                notes.Add("ChaosCardModel.OnPlay 无任何 Harmony 补丁（纯虚方法重写）——0.1.0 仍可走普通 CardOnPlayMirrors 注册。");
                return;
            }

            var owners = string.Join(", ", patches.Owners);
            notes.Add($"ChaosCardModel.OnPlay 被以下 owner 补丁：{owners}。0.1.0 必须改走 AdaptedCardOnPlayMirrors 并声明这个精确组合。");
        }
        catch (Exception exception)
        {
            notes.Add($"ChaosCardModel.OnPlay 补丁审计失败：{exception.GetType().Name}: {exception.Message}");
        }
    }

    // --- 辅助方法 ------------------------------------------------------------------------------

    private static string? SafeLocation(Assembly assembly)
    {
        try { return assembly.Location; }
        catch { return null; }
    }

    private static Guid? SafeMvid(Assembly assembly)
    {
        try { return assembly.ManifestModule.ModuleVersionId; }
        catch { return null; }
    }

    private static Version? ReadManifestVersion(string? assemblyPath, string manifestFileName)
    {
        if (assemblyPath is null) return null;
        try
        {
            var directory = Path.GetDirectoryName(assemblyPath);
            if (string.IsNullOrEmpty(directory)) return null;
            var manifestPath = Path.Combine(directory, manifestFileName);
            if (!File.Exists(manifestPath)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (document.RootElement.TryGetProperty("version", out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is { } raw)
            {
                var trimmed = raw.Trim().TrimStart('v', 'V');
                return Version.TryParse(trimmed, out var version) ? version : null;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ComputeSha256(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }
}
