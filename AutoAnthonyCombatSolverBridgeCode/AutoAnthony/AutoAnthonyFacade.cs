using AutoAnthony;
using ChaosCardGenerator;
using MegaCrit.Sts2.Core.Models;

// 命名空间说明：本文件位于 AutoAnthonyCombatSolverBridge.AutoAnthony 之下，因此在桥根命名空间
// 内部，裸标识符 "AutoAnthony" 会绑定到我们自己的子命名空间，遮蔽外部 Mod 的根命名空间。
// 外部 Mod 一律通过文件级 using + 非限定名引用（如下所示）；本代码库内永远不要写限定名
// AutoAnthony.Xxx。

namespace AutoAnthonyCombatSolverBridge.AutoAnthony;

/// <summary>
/// 对 AutoAnthony 公共 API 的编译期类型化访问——AutoAnthony API 变化时唯一需要改动的接缝。
/// 每个成员只在 CompatibilityGuard 证明 AutoAnthony 程序集已加载且契约完好之后才会被调用
/// （否则 MainFile 会 fail closed）。
///
/// 桥绝不解析卡牌本地化文本：AutoAnthony 的 OperationRuntimeSpec 是官方声明的执行来源
/// （见 AutoAnthony 的 COMPONENT_API.md），本 facade 只暴露结构化数据。
/// </summary>
public static class AutoAnthonyFacade
{
    /// <summary>里程碑 ③：判定一张 AutoAnthony 生成牌（外部角色外壳同样覆盖）。</summary>
    public static bool IsChaosCard(CardModel card) => card is ChaosCardModel;

    public static ChaosCardModel? AsChaosCard(CardModel card) => card as ChaosCardModel;

    /// <summary>按卡牌 id 做注册表层判定——手头只有 ModelId 时有用。</summary>
    public static bool IsGeneratedCardId(ModelId id) => ChaosCardRegistry.IsGeneratedCardId(id);

    /// <summary>
    /// 生效中的生成定义：编辑器覆盖 > 自由形态 > 手工调整 > 按局共享的池定义
    /// （AutoAnthony 自己的优先级顺序）。
    /// </summary>
    public static GeneratedCard? TryGetGenerated(CardModel card)
        => card is ChaosCardModel chaos ? chaos.Generated : null;

    /// <summary>按槽位的共享定义（与之平行的持久化 RuntimeSpecs 存在这里）。</summary>
    public static ChaosCardDefinition? TryGetDefinition(CardModel card)
        => card is ChaosCardModel chaos ? chaos.Definition : null;

    public static IReadOnlyList<GeneratorOperation>? TryGetOperations(CardModel card)
        => TryGetGenerated(card)?.Operations;

    /// <summary>单个操作自带的结构化 spec（新生成的卡必然有）。</summary>
    public static OperationRuntimeSpec? TryGetRuntimeSpec(GeneratorOperation operation)
        => operation.RuntimeSpec;

    /// <summary>与 <see cref="TryGetOperations"/> 平行的持久化 spec 列表（快照身份）。</summary>
    public static IReadOnlyList<OperationRuntimeSpec>? TryGetPersistedRuntimeSpecs(CardModel card)
        => TryGetDefinition(card)?.RuntimeSpecs;

    /// <summary>解析生成牌背后的 (角色, 槽位) 组合（仅池内卡可解析）。</summary>
    public static bool TryGetSlot(CardModel card, out GeneratedCharacter character, out int slot)
        => ChaosCardRegistry.TryGetGeneratedCardSlot(card.Id, out character, out slot);
}
