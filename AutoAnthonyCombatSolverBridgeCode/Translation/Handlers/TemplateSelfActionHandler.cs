using AutoAnthony;
using AutoAnthonyCombatSolverBridge.Translation;
using ChaosCardGenerator;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;

// 命名空间说明见 AutoAnthonyFacade.cs：外部类型一律通过文件级 using + 非限定名引用。

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

/// <summary>
/// template_self_action 的球引导/聚焦/球位/Shiv 创建精确镜像（0.8.x：37 条目录形状）。
/// 逐字复刻源码的模板路由（遗留路径）：
///  - d_channelfrost/dark/lightning/glass/plasma（L2162-2165 → ChaosOrbResolver.Channel L26-54）：
///    count 次 OrbCmd.Channel&lt;T&gt; → simulator.OrbChannel&lt;T&gt;(owner, count)
///  - d_channelrandom：CombatOrbGeneration 随机选球 → 分支 RNG 同流
///  - d_gainfocus（L2184-2185）：PowerCmd.Apply&lt;FocusPower&gt; → effects.ApplyPowerFromSource
///  - d_gaintemporaryfocus（L2187-2188）：同上（临时聚焦在本回合结束自然过期）
///  - d_gainorbslots（L2191）：OrbCmd.AddSlots → simulator.AddOrbSlots
///  - n_createshiv（SimpleHandDerivativeProducerTemplates → CreateDerivatives）：
///    simulator.CreateAndAddGeneratedCardsToCombat&lt;Shiv&gt;(owner, Hand, count, owner)
///    ——与原生镜像 BespokeCardMirrors L138 同款
/// </summary>
public sealed class TemplateSelfActionHandler : IOperationHandler
{
    public string Describe => "template_self_action(球引导/聚焦/球位/Shiv)：模拟器精确镜像";

    public string? ValidateSupport(OperationShape shape)
    {
        var spec = shape.Spec;
        return spec.Variant switch
        {
            "d_channelfrost" or "d_channeldark" or "d_channellightning"
                or "d_channelglass" or "d_channelplasma" or "d_channelrandom" => null,
            "d_gainfocus" or "d_gaintemporaryfocus" => null,
            "d_gainorbslots" => null,
            "n_createshiv" => null,
            _ => $"template_self_action 的 variant={spec.Variant} 不在支持矩阵",
        };
    }

    public void Execute(OperationExecutionContext context)
    {
        var mirror = context.Mirror;
        var owner = context.Card.Owner;
        var amount = context.Card.OperationAmount(context.Shape.OperationIndex);

        switch (context.Shape.Spec.Variant)
        {
            case "d_channelfrost":
                mirror.Simulator.OrbChannel<FrostOrb>(owner, Math.Max(1, amount));
                return;
            case "d_channeldark":
                mirror.Simulator.OrbChannel<DarkOrb>(owner, Math.Max(1, amount));
                return;
            case "d_channellightning":
                mirror.Simulator.OrbChannel<LightningOrb>(owner, Math.Max(1, amount));
                return;
            case "d_channelglass":
                mirror.Simulator.OrbChannel<GlassOrb>(owner, Math.Max(1, amount));
                return;
            case "d_channelplasma":
                mirror.Simulator.OrbChannel<PlasmaOrb>(owner, Math.Max(1, amount));
                return;
            case "d_channelrandom":
            {
                // 源码：OrbModel.GetRandomOrb(Rng.CombatOrbGeneration)——分支 RNG 同流
                var count = Math.Max(1, amount);
                for (var index = 0; index < count; index++)
                {
                    var orb = OrbModel.GetRandomOrb(mirror.Rng.CombatOrbGeneration).ToMutable();
                    if (!mirror.Simulator.OrbChannel(owner, orb))
                        return;
                    if (mirror.Simulator.HasPendingChoice)
                        return;
                }
                return;
            }
            case "d_gainfocus":
            case "d_gaintemporaryfocus":
            {
                // 源码：PowerCmd.Apply<FocusPower>(ctx, owner, amount, owner, card)
                if (mirror.CombatState is not ICombatPredictionEffectSink effects)
                    throw new InvalidOperationException("聚焦需要分支战斗状态效果汇。");
                effects.ApplyPowerFromSource(typeof(FocusPower), owner.Creature, amount, owner.Creature, context.Card);
                return;
            }
            case "d_gainorbslots":
                mirror.Simulator.AddOrbSlots(owner, Math.Max(1, amount));
                return;
            case "n_createshiv":
            {
                // 源码：CreateDerivatives(card, index, op, Hand, ExecutableOperationCount(op, amount))
                // 镜像：与原生 BespokeCardMirrors L138 同款
                var operation = context.Card.Generated.Operations[context.Shape.OperationIndex];
                var count = ChaosOperationExecutor.ExecutableOperationCount(operation, amount);
                if (count == 0)
                    return;
                mirror.Simulator.CreateAndAddGeneratedCardsToCombat<Shiv>(
                    owner, PileType.Hand, count, owner);
                return;
            }
            default:
                throw new UnsupportedRuntimeSpecException(context.Shape.Spec.Opcode, context.Shape.Spec.Variant);
        }
    }
}
