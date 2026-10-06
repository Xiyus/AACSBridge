using AutoAnthony;
using ChaosCardGenerator;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation;

/// <summary>
/// handler 的执行上下文：CombatSolver 的 OnPlay 镜像上下文 + 接收者（MutablePreview 克隆）+
/// 操作形状 + 该操作的显式随机目标（若有）。数值解析直接复用 AutoAnthony 自己的 internal
/// 投影（publicized 编译期引用），保证与真实执行逐位一致：
///  - <see cref="ExecutableAmount"/> 复刻 ChaosOperationExecutor.Execute 的主数值链
///    （OperationAmount → 非能量加成 → 依赖乘数恒等）；
///  - <see cref="RuntimeValue"/> 即 ChaosOperationExecutor.RuntimeSpecValue（live DynamicVar
///    优先，其次升级后 spec 槽值；X 源槽读解析后的 X 值——镜像层已在执行前复刻 OnPlay 的
///    X 解析并写入卡实例）。
/// </summary>
public sealed record OperationExecutionContext(
    CardOnPlayMirrorContext Mirror,
    ChaosCardModel Card,
    OperationShape Shape,
    Creature? ResolvedTarget = null)
{
    /// <summary>
    /// 主数值：OperationAmount（live DynamicVar 优先，含升级/成长）+ 非卡牌的外部伤害加成。
    /// 与 Execute L595-L615 的链一致（简单操作依赖乘数恒为 1，校验层已排除依赖操作）。
    /// </summary>
    public int ExecutableAmount
    {
        get
        {
            var amount = Card.OperationAmount(Shape.OperationIndex);
            if (Card.Type != CardType.Power)
                amount += Card.CapturedExternalDamageBonus(Shape.OperationIndex);
            return amount;
        }
    }

    /// <summary>按槽 ID 解析值（live DynamicVar 优先，其次升级后 spec 槽值）。</summary>
    public int RuntimeValue(string slotId, int fallback)
        => ChaosOperationExecutor.RuntimeSpecValue(Card, Shape.OperationIndex, slotId, fallback);

    /// <summary>伤害命令的 props（与 DamagePropsForCardEffect 一致：Power 卡 Unpowered，否则 Move）。</summary>
    public ValueProp DamageProps => ChaosOperationExecutor.DamagePropsForCardEffect(Card.Type);

    /// <summary>格挡命令的 props（与 BlockPropsForCardEffect 一致）。</summary>
    public ValueProp BlockProps => ChaosOperationExecutor.BlockPropsForCardEffect(Card.Type);
}
