using System.Diagnostics.CodeAnalysis;
using AutoAnthony;
using ChaosCardGenerator;
using MegaCrit.Sts2.Core.Models;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.AutoAnthony;

/// <summary>桥对一张生成卡需要的全部信息，一次性解析完成。</summary>
public sealed record ResolvedChaosCard(
    ChaosCardModel Model,
    ChaosCardDefinition Definition,
    GeneratedCard Generated,
    IReadOnlyList<GeneratorOperation> Operations,
    IReadOnlyList<OperationRuntimeSpec?> RuntimeSpecs,
    IReadOnlyList<OperationRuntimeSpec>? PersistedSpecs)
{
    public int OperationCount => Operations.Count;
}

/// <summary>
/// 把活的 CardModel 解析成完整结构化定义。解析失败（例如定义尚未水合）时返回 false 而不是抛异常
/// ——转储补丁把它当作"跳过这张卡"，绝不当作"这张卡没有效果"。
/// </summary>
public static class ChaosCardResolver
{
    public static bool TryResolve(CardModel card, [NotNullWhen(true)] out ResolvedChaosCard? resolved)
    {
        resolved = null;
        if (card is not ChaosCardModel chaos)
            return false;

        try
        {
            var generated = chaos.Generated;
            var definition = chaos.Definition;
            var operations = generated.Operations;
            var runtimeSpecs = new OperationRuntimeSpec?[operations.Count];
            for (var index = 0; index < operations.Count; index++)
                runtimeSpecs[index] = operations[index].RuntimeSpec;

            resolved = new ResolvedChaosCard(chaos, definition, generated, operations, runtimeSpecs, definition.RuntimeSpecs);
            return true;
        }
        catch
        {
            // 定义在当前状态下无法解析（例如水合中途）。跳过转储是安全的；猜测一个空效果列表不是。
            return false;
        }
    }
}
