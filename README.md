# AutoAnthony × CombatSolver 兼容桥

Slay the Spire 2 的 **纯代码 Bridge Mod**（id：`AutoAnthonyCombatSolverBridge`）：让 [CombatSolver](https://github.com/Torch1230/CombatSolver)
能够预测 [AutoAnthony](https://github.com/mewcodex/AutoAnthony) 生成的随机卡牌。

它不生成新内容，也不修改两个原 Mod。它做的事只有一条链：

```
AutoAnthony 生成牌 → 读取结构化 OperationRuntimeSpec → 翻译成 CombatSolver 的预测行为 → CombatSolver 正常搜索
```

**绝不解析卡牌中文/英文描述**——AutoAnthony 官方契约（`COMPONENT_API.md`）明确 `OperationRuntimeSpec`
（Opcode / Variant / Target / Zones / Flags / ValueSlots / Condition / Trigger）才是执行来源，本地化文本只是输出投影。

基于 [Alchyr/ModTemplate-StS2](https://github.com/Alchyr/ModTemplate-StS2) 的 **Empty Slay the Spire 2 Mod** 模板
（`alchyrsts2mod`）手工复刻，BaseLib 依赖已按模板 README 指引移除（纯代码 Mod，无卡牌/本地化/资源）。

---

## 当前开发状态（2026-10-07，全量实现候选）

1.2.3 修复心神化生成选牌使用错误角色池导致自动选择暂停；同步修正候选过滤、数量、可跳过
与选中后免费。140 项离线检查通过，实机复测待验证；见[选牌失败证据](docs/live-choice-failure-2026-10-07.json)。

1.2.2 修复灵魂牌进入抽牌堆时漏用 Random 插入的位置及随机数状态漂移；两条数量/X 路径
均复用衍生卡解析器。137 项离线检查通过，实机复测待验证；见[重算证据](docs/live-replan-2026-10-07.json)。

1.2.1 修复实机搜索空引用：复合 Power 的保存快照不保留 Owner，攻击增伤历史改用预测状态捕获的
玩家引用，并仅在规则需要历史时查询。137 项离线检查通过；实机复测待验证，证据见
[搜索失败记录](docs/live-failure-2026-10-07.json)。

当前源码已超过历史 1.0.0：**241 个注册键，134 项统一离线检查通过**。本机已安装 DLL 枚举到
467 个目录原子，440 个通过单操作校验；其余结构项在必要的合法配对上下文中准入，
实现清单为 **467/467**。这是目录准入统计，**不是整卡覆盖率或实机等价率**，
不能与历史 587/931 直接比较。逐条结果及二进制 hash 见 [目录审计](docs/catalog-audit.json)，
本轮结论与剩余工作见 [验收与后续清单](docs/acceptance-and-backlog.md)。

按“全部实现后再统一测试”的要求完成目录与执行链补齐，包括触发器、战斗规则、命名卡槽、
多次选择、ForEach、嵌套自动出牌、衍生槽/附魔、结构升级及已有近似路径纠偏。
完整范围、配对口径和统一实机测试清单见 [全量实现候选](docs/implementation-completion-2026-10-07.md)，
配对准入逐项证据见 [实现审计](docs/catalog-audit-work.json)。

以下为本轮以前的开发记录。

本轮新增唯一抽到技能牌条件和四种有实际派发的抽牌触发；修复规则代理被跳过、左球位置、
全体球重复激发、状态牌重复消耗、回洗手牌误触发弃牌，以及修饰符历史计数近似。
未接入的触发事件、依赖前缀、条件修饰符、临时聚焦和墨色小刀替代路径明确拒绝。
整卡执行遇到 pending choice 而没有续接时硬失败，不再继续执行后续操作。

**本轮新增行为尚未进行实机严格 diff，不据此宣布发布验收完成。** 以下为上一批和历史记录。

### Batch AS：实机费用差异修复与本卡消耗事件

2026-10-07 对 `014a6a5` 的实机日志核对：4 个 ROUTE_REPLAY、18 条 ROUTE_ACTION，
SEARCH_FAILURE 为 0，4 次 `firstScalarDifference` 均为 null。但发现一条完整续接状态差异：
`CHAOS_NECROBINDER_CARD007`（捕捉郁）在生物死亡后真实费用降至 0，预测仍为 1。
因此本场只记为逐动作标量回放一致，不能宣布完整状态严格 diff 通过。
原日志路径、SHA256 和差异文本保存于 [实机证据](docs/live-evidence-2026-10-07.json)。
高费用触发器只出现在目录转储，未证明本场实际触发；死灵法师火焰 VFX 空引用堆栈未指向桥。

| 行为 | 派发 | 支持边界 |
|---|---|---|
| 生物死亡后本卡降费 | 逐具体 Chaos 卡登记 `AfterDeath`；战斗牌堆中的分支卡按原版 `OperationAmount` 求和并 `AddThisCombat` | 排除阻止移除；每项至少 1；即使 OnPlay 不支持也镜像被动降费 |
| `self_exhausted/immediate` | Solver 全局 `AfterCardExhausted` 完成后派发本卡收益 | `ConditionalTrigger` + `thisCard`；fixed 自身格挡/能量/治疗、全体/随机伤害 |

本卡消耗事件覆盖统一消耗入口，按顺序执行 `triggerIndex` 链接收益；不在 OnPlay 执行，
不与消耗堆回合事件混淆。无收益、嵌套触发/修饰符、事件卡槽、动态收益及选择续接均拒绝。
134 项离线检查通过，包括事件身份/收益准入与拒绝、死亡 hook 登记、阻止移除过滤及实际 Harmony 接缝。
费用修复和新消耗事件均待新一轮实机严格 diff 验证；单操作目录准入仍为 331/467。

### Batch AR：高费用出牌触发器

| 触发器 | lifetime | 派发与阈值 | 收益边界 |
|---|---|---|---|
| `energy_cost_at_least_card_played` | `combat` | `BeforeCardPlayed`；逐操作比较当前 `EnergyCost.GetResolved()` 与复合 Power 的 `EffectiveOperationAmount(index, 2)` | 自身格挡/能量/治疗、全体或随机伤害；fixed 数值 |

阈值读取模拟分支捕获的操作数值，沿用 AutoAnthony 的升级/槽位回退逻辑，并纳入成员守卫。
该事件不使用实际消耗能量，也不套用 `AfterCardPlayed` 的武装牌跳过标记；多条阈值分别判断。
其他 lifetime、未知阈值槽、动态阈值和未支持收益仍拒绝。
本轮修正 Batch AO/AP 后过时的拒绝断言，验证真实 `BeforeCardPlayed` handler 登记和阈值 Fork 隔离。
Release 构建及 117 项离线检查通过；高费用触发器的实机严格 diff 尚待验证。

### Batch M 与独立模板纠偏

本批接续已有 Batch A–L 的模板扩展和三个未提交代理模板，完成：

- `I:ProxyAtomic_ForegoneConclusion`：按原始 OperationAmount（最少 1）施加对应 Power。
- `I:ProxyAtomic_MultiCast`：读取解析后的 X 槽值，每次重新读取队首球，仅最后一次移除。
- `I:ProxyAtomic_Tempest`：读取生成操作的 OrbOutputId，支持五种固定球与随机球；随机球使用分支 CombatOrbGeneration 流，保留原版循环的 RNG 消耗。
- 修正 `I:DrawWithRetain` 遗漏单回合保留、`I:TriggerPoisonNow` 跳过即时毒结算、`I:NextSkillCostsZero` 错用 FreePowerPower 的问题。
- `I:Upgrade`、`I:PlayTopCardAndExhaust`、`I:PlayThisCard`、`CL:ExhaustUpToHandCards` 尚缺选牌或嵌套出牌续接，明确拒绝，移除升级本卡或空操作的近似执行。
- 条件门控收益继续执行完整 handler 校验，不能因条件受支持而放行未知或未实现的收益。

抽牌保留、即时毒结算与代理球操作若出现 pending choice，本批显式失败，避免遗漏后续效果。AutoAnthony 守卫增加 OrbOutputId / OrbSlotCatalog.ResolveOutput 契约。

**验证：58 项已安装 DLL 的离线契约检查通过，编译通过。** 新增操作和上述纠偏尚未进行实机逐动作严格 diff；这些结果不代表全目录覆盖或整桥发布验收完成。历史覆盖率与实机记录如下，仅对应当时版本。

## v1.0.0 历史记录（64 翻译键 / 63.1% 目录覆盖）

| 里程碑 | 内容 | 状态 |
|---|---|---|
| ①–④ | 模板 / 双 DLL 引用 / 守卫 / 识别 / 转储 | ✅ 实机验证 |
| 0.1.0 | Damage/Block/Draw/Energy 翻译 + 镜像注册 | ✅ 实机验证（镜像调用/fail-closed 全链路实证） |
| 0.2.0 | apply_power(20 variant) + Power 卡伤害 + strength_scaled 修饰符 | ✅ 实机验证（**严格 diff 零差异**，见下） |
| 0.2.0+ | 保守可打性模式（矩阵外卡模拟中不可打） | ✅ 实机验证（搜索从必然失败变为完整完成） |
| 0.3.0 | 牌堆移动：exhaust/discard(all) + create_copy + draw_and_discard | ✅ 实机验证（严格 diff 零差异 + **生成牌递归**） |
| 0.4.0 | X 费卡（OnPlay X 解析复刻）+ 随机目标（分支 RNG 消耗对齐） | ✅ 编译+注册就绪 |
| 0.5.0 | 玩家选牌：exhaust/discard/move(selected) | ✅ 实机验证（**选牌分支 5551 展开 + 计划选择部署 + 严格 diff 零差异**） |
| 0.6.0 | 受限 Trigger / ChaosCompositePower + 根捕获 / Fork / 指纹 / 续接核对 | ✅ 编译与离线契约验证 + 缺陷局实机 |
| 0.7.0 | 随机生成（create_card 四变体，分支 RNG + 递归镜像） | ✅ 实机验证（CARD027 进路线 + 零差异） |
| 0.8.0 | 全目录审计（931 条逐条核对）+ gain_stars 补遗 | ✅ 离线 35 项检查 |
| 0.8.x | 球引导/聚焦/Shiv/锻造/毒/力量/敏捷/失焦/下回合能量/球激发/最大生命/禁抽/全体易伤 | ✅ 缺陷局实机验证 |
| 1.0.0 | 64 翻译键 / 63.1% 目录覆盖 / RNG 对齐修复 / 发布 | ✅ **发布就绪** |

### 0.6.0 实现范围与验证（2026-10-06）

初始化时登记复合 Power 的事件镜像和 `PowerHiddenStateMirrors`，并预热规范 Power 的
DynamicVars。OnPlay 先校验整卡，只执行即时操作；挂靠 `triggerIndex` 的收益被跳过，
最后按 AutoAnthony 的配置路径武装一个独立的、可实例化的模拟 Power。

| 触发器 Kind | Lifetime | 行为 |
|---|---|---|
| `next_turn_start` | `next_turn` | 下一次己方回合开始触发一次，随后清理无存活效果的容器 |
| `next_turns_start` | `next_n_turns` | 每次己方回合开始触发，递减捕获的次数 |
| `turn_start` | `combat` | 永久 Power 每次己方回合开始触发 |
| `turn_end` | `combat` | 己方 AutoPostPlay 阶段触发 |
| `card_played` / `attack_played` / `skill_played` / `power_played` | `combat` | 按事件和卡类型触发；跳过武装该 Power 的第一次出牌回调 |
| `card_drawn` / `card_exhausted` | `combat` | 仅响应 Owner 的抽牌 / 消耗事件 |

**触发收益**只放行 fixed 数值的 `gain_block/immediate/self`、`gain_energy/immediate/self`、
`heal/immediate/self`、`deal_damage/all/all_enemies` 和 `deal_damage/random/random_enemy`。
复刻脱离牌堆的来源代理：保存原定义、升级态、捕获值与 X 值；格挡使用 Unpowered，
伤害按来源类型使用 Unpowered 或 Unpowered|Move，随机目标消费模拟分支 RNG。
触发收益中的抽牌、选牌、牌堆移动、Power、事件目标、X 值源、Modifier、AbilityRule、
结构升级、外部角色 Profile 及其余触发器继续拒绝。

隐藏状态由独立的 `ChaosCompositePredictionState` 保存。根捕获从实机 Power 复制，Fork
深复制 Power、捕获值数组与触发计数；AA checksum schema 5 的固定字段和变长定义 / 捕获值
进入搜索指纹。由于 Solver 的 `PowerHiddenStateMirrors` 尚未提供续接追加入口，本桥还对
**Solver 的 `ContinuationStamp.AppendPowers`** 增补精确状态文本，两侧均包含完整捕获值和
定义 payload，隐藏次数失配会使续用失效；不只比较 Power.Amount。

**生命周期接缝限制**：当前 Solver 未提供有序 `AfterSideTurnStart` 第三方入口。本桥只补丁
Solver 的模拟生命周期，不修改真实游戏或 AutoAnthony；发现其他同阶段覆写监听者时，
回合开始收益明确拒绝，避免近似监听顺序。触发链采用直接重入抑制和 64 层深度保护；
模拟输入 / 回合边界间每个触发器最多验证 20 次，无法精确区分的怪物 / 选择续接时间点
若超过预算则硬失败，收益出现 pending choice 同样拒绝，绝不丢弃后续收益。

选牌仍只支持单个、位于最后的选择型操作。选牌后还有操作的卡被保守排除，避免 OwnChoice
把选牌移到末尾后改变 `card_exhausted` 等新接入事件的执行顺序。

离线检查使用**实际安装的原版 DLL**验证支持矩阵、目录原子形状、错误 hash 拒绝、
私有接缝签名、Power 隐藏状态 / hook 注册、Harmony 补丁目标、隐藏计数续接差异、兄弟分支
隔离及触发结算中的 Fork 拒绝，见 `tests/BridgeChecks`。这些检查不代表实机严格 diff 已通过。
本机结果：**35 项检查通过**，目录中 21 个触发原子、27 个收益原子落在受限形状内；
原子数不等于可打卡数，仍须逐卡校验全部操作与挂靠关系。

实机验收仍需：单次 / 多次延迟、永久回合开始 / 结束、出牌 / 抽牌 / 消耗事件各提供 fixture，
逐动作严格 diff 零差异且 PredictionGaps 非补偿项为空；增加从已有 Power 中途捕获根、两个
同槽但不同定义的实例、升级态、RNG 消耗及移除登记后的反向对照。

### 0.5.0 实机验证记录（2026-10-06 铁甲局）

- 铁甲局牌组 **10/14 张矩阵内**；搜索 SEARCH_FAILURE = 0，战斗获胜；
- **选牌分支展开**：`choice_branches=5551`——CARD001（格挡+选牌消耗）的选择被
  按手牌候选展开成搜索分支；
- **计划选择**：路线包含 `PlanCardChoice`（turn 2 计划消耗"余烬拳"、turn 6 计划
  消耗"打击祭品"）——求解器自主决定消耗哪张，部署时照计划应答原生选牌页面；
- **严格 diff 零差异**：2 条路线 28/28 与 25/25 步全部完成——含选牌部署；
- 路线 38 次出牌覆盖 8 种矩阵内卡（含 create_copy 卡 ×12——生成牌递归持续工作）。

### 0.3.0 实机验证记录（2026-10-06）

- 桥启用（31 形状 / 514 镜像），SEARCH_FAILURE = 0，战斗获胜；
- 路线 13 次出牌全部为矩阵内卡（CARD006 ×5、005/008 ×2、001/003/025/002 ×1）；
- **严格 diff 零差异**：18/18 步全部完成；
- **生成牌递归**（原计划 0.7.0 里程碑的基础形态）自然发生并被精确预测：CARD006
  （复制自身进弃牌堆）打出 → 克隆进弃牌 → 洗回抽牌堆 → 再抽到再打出（turn=6
  occurrence=1 即克隆实例）→ 再复制——同一镜像递归覆盖，预测与实际零差异；
- 本次测试前一轮还意外获得**无桥基线对照**：守卫因 CreateClone 扩展方法契约错误
  （已修复）禁用桥后，搜索把混沌卡全部按未适配处理，路线退化为纯 EndTurn——
  桥的价值直观可见。守卫的 fail-closed 第二次抓住契约错误并干净禁用（精确日志、
  零崩溃）。

### 0.2.0 实机验证记录（2026-10-06）

- **搜索完整完成并部署**：CombatSolver 对混沌牌组完成整场搜索（expanded=4452），路线被
  自动部署执行，战斗获胜；
- **路线构成**（26 次出牌，全部为矩阵内卡）：CARD003（易伤+伤害）×8、CARD008（伤害）×6、
  CARD001/005（格挡）×8、CARD002（格挡+力量缩放修饰符）×4——0.2.0 的 apply_power 与
  strength_scaled 特性均在被验证路线内；
- **严格 diff 零差异**（CombatSolver 官方验收标准第一条）：两条选定路线的 ROUTE_REPLAY
  回放比对（HP/格挡/能量/星能/手牌数逐动作）`firstScalarDifference = null`，20/21 步全部
  完成——**预测状态与真实执行完全一致**；
- 保守可打性生效：矩阵外的卡（move_card/create_copy/i_upgrade 等）不再触发搜索中止。

实测样例（spec-dump.log）：

```
CHAOS_CARD id=CARD.CHAOS_CARD000 title="岿然防御" type=Skill cost=1 ... operations=2 specs_present=2 persisted_specs=2
  op[0] template="N:B" scope=NonTargeted spec=present
    spec schema=1 opcode=gain_block variant=immediate target=self zones=none->none filter=any flags=[block_reference,...] values=[block=4 src=fixed off=0 up] condition=- trigger=-
```

### 支持矩阵（与 AutoAnthony 组件目录逐形状核对）

| Opcode | 支持形状 | 目录条目数 |
|---|---|---|
| `deal_damage` | (selected, selected_enemy) / (all, all_enemies) / **(random, random_enemy)**（0.4.0），fixed 值 + 可选 hits（**含 energy_x/star_x X 值源**）；含 Power 卡 Unpowered 逐 hit 路径 | 174 |
| `gain_block` | (immediate, self)，fixed 值；含 M:base/strength_scaled 修饰符数学 | 78 |
| `draw_cards` | (immediate, self)，fixed 值 | 50 |
| `gain_energy` | (immediate, self)，fixed 值 | 30 |
| `lose_hp` | (immediate, self) / (immediate, selected_enemy) / **(immediate, random_enemy)**（0.4.0），fixed 值 | 11 |
| `heal` | (immediate, self)，fixed 值 | 1 |
| `apply_power` | 20 个 variant（0.2.0）：vulnerable/weak/strength_loss(_this_turn)/strength_gain（selected_enemy+all_enemies）、vulnerable_double、strength/dexterity_gain(_loss/_this_turn)/doom/focus_loss/thorns/intangible/blur/plating/strength_this_turn/vigor/strength_loss(_this_turn)/retain_hand_this_turn/strength_per_target_vulnerable（self） | 76 |
| `exhaust_card` | (all, all_cards, hand→none)，filter=any/non_attack；**(selected, selected_card, hand→none)（0.5.0 选牌分支）** | 3 + 4 |
| `discard_card` | (all, all_cards, hand→none)，filter=any；**(selected, selected_card, hand→none)（0.5.0）** | 2 + 6 |
| `create_copy` | (this_card, self_card, none→discard)——Anger 式克隆进弃牌堆 | 2 |
| `draw_and_discard` | (nonzero_cost, self)——Scrape 式抽后弃非零费 | 1 |
| `move_card` | **(selected, discard→hand) / (selected, discard→draw)（0.5.0 选牌分支）** | 2 + 1 |

**卡级生效条件**：卡上全部操作都在矩阵内，且不触发以下任一排除项（fail-closed，逐项对应后续里程碑）：

- ~~X 费卡与 X 值源槽~~ → **0.4.0 已支持**（镜像层复刻 OnPlay 的 X 解析：`Hook.ModifyXValue` 分支状态 + `SetResolvedXValues`；值槽经 RuntimeSpecValue 自动读到解析后 X 值，含 ChaosXValueMultiplier 翻倍）；
- ~~随机目标引用~~ → **0.4.0 已支持**（显式抽取消耗分支 CombatTargets 流——RNG 消耗与真实一致；deal_damage/random 走命令随机目标，lose_hp/random 走抽取结果）；
- ~~玩家选牌（exhaust/discard/move 的 selected）~~ → **0.5.0 已支持**（`CardChoiceMirrors` 登记原生 Effect，求解器在 OwnChoice 阶段展开分支并施加效果；单选卡限制——多选卡仍拒绝）；
- Modifier scope 操作，**唯一例外**：`M:base/strength_scaled`（0.2.0 已建模，整数除法后乘）；
  其余修饰符（modify_damage/modify_hits 家族）→ 后续版本；
- AbilityTrigger / ConditionalTrigger 的受限复合 Power 形状 → **0.6.0 已实现**（范围与限制见上）；AbilityRule 与其余触发组合继续排除；
- 玩家选牌（选择器模板 / `CardTargetSlot`）→ 0.5.0；
- 事件目标（event_enemy）、历史计数（cards_played_combat）、阈值翻倍（selected_energy_x_threshold）→ 后续版本；
- 结构性升级（RepeatOperation / ExecuteOperationOnPlay / ChooseExhaust / 衍生卡升级等）。

**支持矩阵外的卡的处理——保守可打性模式（默认开启）**：

- 矩阵外的卡在**模拟中按"不可打"处理**（IsPlayable 镜像返回 false）——搜索只探索可精确
  预测的路线，部署的每个动作都被精确预测；这些卡留在手里不被求解器主动打出，**真实游戏
  完全不受影响**（玩家仍可手动打出）。
- 强制打出（Havoc/Cascade 类 auto-play 效果绕过 CanPlay）仍由 OnPlay 镜像**硬失败兜底**：
  `IncompatibleGameplayModException` → 整场搜索中止。
- 代价：路线可能次优（避开了一张本可制胜的矩阵外卡）——但预测永远精确，这是
  "宁可次优、不可错误预测"红线的保守实现。
- 设环境变量 `AA_BRIDGE_STRICT=1` 关闭本模式，回到纯硬失败语义（任何矩阵外卡进入
  候选即中止整场搜索——实测表明该语义下搜索几乎总是失败，因为搜索会探索抽牌堆里
  未来回合的全部候选）。
- 判定结果按（根卡实例, 升级态）缓存，避免搜索每个节点重复做升级投影。

### 0.1.0 架构要点

- **一个通用镜像解释全部 500+ 具体卡类**：镜像派发按精确运行时类型匹配（receiver 是保持运行时类型的 MutablePreview 克隆），注册器反射枚举 AutoAnthony 六个家族 + 其他已加载程序集的全部具体 `ChaosCardModel` 子类逐个登记 `CardOnPlayMirrors`/`CardIsPlayableMirrors`；
- **数值解析复用 AutoAnthony 自己的 internal 投影**（publicized 编译期引用）：`OperationAmount`（live DynamicVar 优先）、`EffectiveRuntimeSpec`（升级 delta）、`RuntimeSpecValue`（按槽解析）、`DamagePropsForCardEffect`/`BlockPropsForCardEffect`（ValueProp 语义）——与真实执行逐位一致；
- **预校验-后执行**：`ChaosCardOnPlayMirror` 先整卡校验（上面全部排除项），任何不支持 → `PredictionUnsupportedException.ForContent` → 整场搜索中止；
- **命令级精确镜像**：伤害走 `DamageCmd.Attack(...).WithHitCount(hits).FromCard(card, cardPlay).Targeting(...).Simulate(simulator)`（与 CombatSolver 内置镜像同款），格挡/抽牌/能量/失血/治疗走 simulator 对应入口，props 与 dealer 语义逐一对齐源码（含 lose_hp 6 参重载内部 `dealer = cardSource?.Owner.Creature` 的反编译核实）；
- **IsPlayable 镜像**复刻混沌卡唯一的重写子句（`C:playableIfDrawPileEmpty` → 模拟分支抽牌堆为空），读分支状态。

### 目录结构

```
AutoAnthonyCombatSolverBridge/
├── AutoAnthonyCombatSolverBridge.csproj      # Godot.NET.Sdk 4.5.1 / net9.0 / 双 DLL 引用 / Publicizer
├── AutoAnthonyCombatSolverBridge.json        # manifest（依赖 AutoAnthony + CombatSolver）
├── AutoAnthonyCombatSolverBridge.sln
├── Directory.Build.props                     # 机器本地（gitignored）：Sts2Path / GodotPath
├── LocalDependencies.props(.example)         # 机器本地（gitignored）：两个 Mod 的 DLL 路径
├── Sts2PathDiscovery.props                   # 模板自带的 Steam 路径自动探测
└── AutoAnthonyCombatSolverBridgeCode/
    ├── MainFile.cs                           # [ModInitializer] 入口：守卫 → PatchAll → Bootstrap
    ├── Bootstrap/
    │   ├── CompatibilityGuard.cs             # 版本/类型/成员/MVID/SHA256 自检（fail-closed）
    │   └── BridgeBootstrap.cs                # 启用桥并输出锁定信息
    ├── AutoAnthony/
    │   ├── AutoAnthonyFacade.cs              # AutoAnthony 公共 API 的唯一接缝（编译期类型引用）
    │   ├── ChaosCardResolver.cs              # CardModel → 完整结构化定义（一次解析）
    │   └── RuntimeSpecReader.cs              # RuntimeSpec → 稳定格式的日志行
    ├── CombatSolver/
    │   ├── CombatSolverRegistrar.cs          # 镜像注册器：反射枚举全部具体卡类逐个登记（精确类型派发）
    │   ├── ChaosCardOnPlayMirror.cs          # 通用 OnPlay 镜像：整卡预校验 → 逐操作翻译执行
    │   ├── ChaosCardIsPlayableMirror.cs      # IsPlayable 镜像：抽牌堆为空子句
    │   ├── ChaosTriggerPolicy.cs             # 0.6.0 受限触发器/收益支持矩阵
    │   ├── ChaosCompositePowerMirror.cs      # 武装、事件派发、延迟次数、移除、根校验
    │   └── ChaosCompositePredictionState.cs  # 脱离实机的隐藏状态、深 Fork、指纹
    ├── Translation/
    │   ├── OperationKey.cs                   # (Opcode, Variant) 路由键
    │   ├── OperationShape.cs                 # 操作形状（索引/scope/生效 spec）
    │   ├── OperationExecutionContext.cs      # 执行上下文（镜像上下文 + 数值解析复用 AA internal 投影）
    │   ├── IOperationHandler.cs              # 翻译器接口（ValidateSupport + Execute）
    │   ├── OperationHandlerRegistry.cs       # 处理器注册表（显式键）
    │   ├── HandlerCatalog.cs                 # 0.1.0 支持矩阵登记
    │   ├── RuntimeSpecTranslator.cs          # spec → handler（无 handler 即抛异常）
    │   ├── UnsupportedRuntimeSpecException.cs
    │   └── Handlers/
    │       ├── DamageHandler.cs              # deal_damage(selected/all)
    │       ├── BlockHandler.cs               # gain_block(immediate)
    │       ├── DrawHandler.cs                # draw_cards(immediate)
    │       ├── EnergyHandler.cs              # gain_energy(immediate)
    │       ├── LoseHpHandler.cs              # lose_hp(immediate, self/selected_enemy)
    │       └── HealHandler.cs                # heal(immediate)
    ├── Patches/
    │   └── RuntimeSpecDumpPatch.cs           # 里程碑③④：CombatState.AddCard 只读转储（开局牌组+战斗中生成牌全覆盖）
    └── Diagnostics/
        └── BridgeLog.cs                      # 游戏 Logger + 独立 spec-dump.log 文件
```

---

## 兼容性检查

| 组件 | 已审计版本 | 参考 SHA256 |
|---|---|---|
| Slay the Spire 2 | 0.111.0 | — |
| AutoAnthony | 0.3.138 | `689b9c5056227c0b47e406159853938c407d56f5726604b1eac3c1b72c10fa50` |
| CombatSolver | 0.50.1 | `832060172aa5eae8d79546f120a10e4571c324b7c0d6ab2c6abc2bcad24323cd` |

`CompatibilityGuard` 在 Mod 初始化时做运行时自检（CombatSolver 第三方适配文档的要求）：

- 两个程序集已加载；manifest 版本用于日志与参考，不单独决定兼容性；
- 关键类型/成员签名逐个存在（`ChaosCardModel.Generated`、`CardOnPlayMirrors.Registry`、
  `AdaptedCardOnPlayMirrors.Register`、`KnownPreRootSubscriberTypeNames` 等，全反射、零硬引用）；
- `OperationRuntimeSpec.CurrentSchemaVersion == 1`（schema 变了 = 拒绝启用）；
- 记录两个 DLL 的 MVID + SHA256 到日志；已审计哈希走快速路径，其他构建核验嵌入的行为契约基线；
- 核验相关类型的方法签名、字段/常量、规范化 IL 与异常处理，包含内部异步状态机；
  DLL 包装、版本号、无关类型及新增无关方法变化不会单独禁用桥；相关行为变化则列出具体成员并拒绝启用；
- 审计 `ChaosCardModel.OnPlay` 上的 Harmony 补丁（决定 0.1.0 走普通注册表还是 `AdaptedCardOnPlayMirrors`）。

**任何一项失败 → 桥整体禁用，一个补丁都不打、一个镜像都不注册**（日志会列出全部失败原因）。

行为基线见 `docs/compatibility-behavior-baseline.json`，只能在离线审查后显式更新，运行时不自动重建。
这是一项保守的漂移检测：相关方法的等价重构也可能要求复查；它不能证明数据资源、其他 Mod
或基线范围外的传递依赖永远不改变行为。未知 RuntimeSpec 仍在逐卡准入时拒绝。

---

## 构建

前置条件：

1. **.NET SDK 9.0+**（本机已装 9.0.318 到 `<USER_HOME>\.dotnet`；若 `dotnet` 不在 PATH，用完整路径调用）；
2. 已安装 Slay the Spire 2（Steam 自动探测，或在 `Directory.Build.props` 里写 `Sts2Path`）；
3. 已安装 AutoAnthony 与 CombatSolver（Steam 创意工坊或本地 mods 目录）。

步骤：

```powershell
cd AutoAnthonyCombatSolverBridge
# 首次：复制 LocalDependencies.props.example → LocalDependencies.props，改成你机器上两个 Mod 的实际目录
dotnet build -c Debug
```

构建成功后自动复制 `AutoAnthonyCombatSolverBridge.dll/.json/.pdb` 到
`<游戏>\mods\AutoAnthonyCombatSolverBridge\`（模板的 `CopyToModsFolderOnBuild`）。
纯代码改动 **Build 即可**，无需 Publish/PCK（`has_pck: false`；Publish 才需要 MegaDot 4.5.1）。

> 引用规则：两个 Mod 的 DLL 均 `Private=false`——桥只引用，绝不把它们打进自己的发布包，
> 否则会出现程序集重复/双 CombatSolver 加载。发布 ZIP 只含本 Mod 的 dll + json + README。

## 验证里程碑 ①–④

1. 启动游戏（装着 AutoAnthony + CombatSolver + 本桥），主菜单不报错 → ①；
2. 日志出现 `[AA-CS Bridge] 桥已启用：AutoAnthony 0.3.137（MVID ...）+ CombatSolver 0.50.1（MVID ...）`（含 MVID/SHA256）→ ②；
3. 开一局带生成牌的战斗，日志出现 `CHAOS_CARD id=... operations=N specs_present=N` → ③；
4. 每张牌下面逐条 `op[i] template=... scope=...` 与 `spec schema=1 opcode=... variant=... target=... zones=... flags=... values=...` → ④。

> 转储行（`CHAOS_CARD` / `op[i]` / `spec`）的字段名保持英文技术格式，便于 grep 与跨局 diff；
> 守卫与状态类日志消息为中文。守卫失败时会逐条列出 `[guard]` 失败原因（中文）。

转储同时写入独立文件（方便 grep / diff）：
`<Godot 用户数据目录>/AutoAnthonyCombatSolverBridge/spec-dump.log`
（Windows 默认 `%APPDATA%\Godot\app_userdata\Slay the Spire 2\AutoAnthonyCombatSolverBridge\spec-dump.log`）。

---

## 设计红线（后续版本同样适用）

1. **Fail closed**：守卫失败/未知 Opcode/未注册 handler → 禁用或中止，绝不静默当空操作。
2. **初始化期一次注册完**：CombatSolver 的注册表在首次根捕获后冻结（部分表迟到直接抛异常，
   其余静默无效）；战斗中途注册是禁止的。
3. **只写模拟分支**：桥的 handler 永远操作 `SimulatedCombatState` / 分支状态，绝不调用真实
   `OnPlay` 或触碰真实 `CombatState`。
4. **RNG 用模拟分支的**（`context.Rng`），绝不用 `System.Random` / 真实游戏 RNG。
5. **验收标准**：预测状态与真实执行状态严格 diff 零差异 + PredictionGaps 非补偿项为空；
   每个夹具做反向对照（删掉登记必须失败）。

## 路线图

| 版本 | 内容 |
|---|---|
| 0.0.1–0.0.4 | ✅ 模板 / 双 DLL 引用 / 守卫 / ChaosCard 识别 / RuntimeSpec 转储（全部实机验证） |
| 0.1.0 | ✅ Damage / Block / Draw / Energy(+lose_hp/heal) handler + 逐具体类镜像注册 + fail-closed 校验（实机验证） |
| 0.2.0 | ✅ apply_power 20 variant + Power 卡 Unpowered 伤害 + strength_scaled 格挡修饰符（**严格 diff 零差异**实机验证） |
| 0.2.0+ | ✅ 保守可打性模式（矩阵外卡模拟中不可打，搜索可完成） |
| 0.3.0 | ✅ 牌堆移动：exhaust_card/discard_card(all) + create_copy(this_card) + draw_and_discard(nonzero_cost)（严格 diff + 生成牌递归实机验证） |
| 0.4.0 | ✅ X 费卡（OnPlay X 解析复刻）+ 随机目标（分支 RNG 消耗对齐） |
| 0.5.0 | ✅ 玩家选牌：exhaust/discard/move(selected)——CardChoiceMirrors 原生 Effect 登记（**选牌分支 5551 展开 + 计划选择部署 + 严格 diff 零差异**实机验证） |
| 0.6.0 | ✅ 受限 Trigger / `ChaosCompositePower` 跨回合，隐藏状态根捕获 / Fork / 指纹 / 续接（编译和离线检查通过，待实机严格 diff） |
| 0.7.0 | 生成牌递归模拟 |
| 0.8.0 | 全 Component Catalog 审计（931 条 spec 逐条核对） |
| 1.0.0 | strict diff 全通过 + PredictionGaps = 0 → 发布 |

## 0.8.0 目录审计（2026-10-06）

对组件目录 931 条 RuntimeSpec 逐条核对支持矩阵：

**总覆盖率：507 / 931 = 54.5%**

| 状态 | 条目数 | 说明 |
|---|---|---|
| handler（即时效果） | 430 | 37 个 (Opcode, Variant) 键 |
| trigger（受限触发器） | 64 | 10 种回合/事件边界 kind |
| selection（选牌分支） | 13 | 6 种选择形状 |
| **合计支持** | **507** | **54.5%** |

不支持家族按大类（424 条）：

| 家族 | 条目数 | 说明 |
|---|---|---|
| `template_self_action` | 130 | 角色专属模板（召唤/球/ Shiv/锻造/毒等）——遗留模板路由 |
| `template_independent_action` | 71 | 代理/独立模板（Discovery/Splash/Begone/Quasar 等）——遗留模板路由 |
| `trigger`（不支持的事件种类） | 52 | for_each 家族/energy_spent/vulnerable_applied 等事件触发 |
| `template_modifier` | 46 | 伤害/命中修饰符（DamageAndHits 数学） |
| `template_target_action` | 33 | 目标模板（Osty 伤害/毒/末日等） |
| `condition` | 23 | 条件门控（fatal/hand_empty 等） |
| `combat_rule` | 21 | 战斗规则 |
| `gain_stars` | 13 | 星能获得 |
| 其余 | 95 | 零散形状（modify_cost/choose/end_turn 等） |

**关键结论**：
1. 剩余缺口的 **82%** 集中在 `template_*` 遗留模板族（280 条）——这些操作的执行不走
   结构化 RuntimeSpec 分派，而是走 AutoAnthony 的模板路由（遗留代码路径）。支持它们
   需要逐模板镜像遗留实现，是独立的大工程；
2. 触发器族已覆盖 64/116 = 55%（10 种回合/事件边界 kind）；剩余 52 条是
   for_each/energy_spent/vulnerable_applied 等事件触发——需要复合 Power 的事件派发
   扩展到这些 kind；
3. 条件门控（23 条）需要复合 Power 的条件评估镜像；
4. `gain_stars`（13 条）是唯一一个"简单但未做"的即时效果——simulator 有
   `GainStars` 入口，补一个 handler 即可。

**卡级生效率**（实测牌组）：铁甲局 10-13/14 张矩阵内（71-93%）；死灵法师局
1-3/12（Osty 模板族占主导）。卡级生效率取决于牌组的模板族占比，而非总覆盖率。

**已实机验证（2026-10-06 第四/五次测试）**：

- `已登记 514 个具体 Chaos 卡类型的 OnPlay/IsPlayable 镜像。`（首次测试曾因反射查找撞同名重载
  全部失败——`AmbiguousMatchException`，已修复：`GetMethods + IsGenericMethodDefinition` 消歧）；
- CombatSolver 搜索尝试打出矩阵外的卡（如 `create_copy`/`apply_power` 组合）时，桥的镜像被
  实际调用（CombatSolver 日志堆栈：`ChaosCardOnPlayMirror.Execute → MethodMirrorRegistry.Invoke →
  CardOnPlayMirrors.Invoke`），校验给出精确中文原因（卡牌、操作索引、模板、opcode/variant），
  `IncompatibleGameplayModException` → 整场搜索中止——**fail-closed 全链路按设计工作**；
- 游戏全程稳定，战斗可正常进行/获胜；
- 实测牌组 3/11 张卡在矩阵内（纯 gain_block ×2 + 纯 deal_damage）。**注意**：搜索会评估手牌中
  每张可打的卡作为候选——只要手牌里有任何矩阵外的卡，搜索就会中止。因此 0.1.0 覆盖下搜索
  大概率失败（诚实失败，优于错误预测）；要看到"矩阵内卡被成功预测"需要运气（手牌恰好全是
  矩阵内卡+原生卡）或等 0.2.0 扩大覆盖。

后续验证：

1. 启动游戏（AutoAnthony + CombatSolver + 本桥），日志确认：
   `[AA-CS Bridge] 翻译表就绪：7 个 (Opcode, Variant) 形状。`
   `[AA-CS Bridge] 已登记 514 个具体 Chaos 卡类型的 OnPlay/IsPlayable 镜像。`
2. 开一局进入战斗，让 CombatSolver 执行搜索（自动或手动）：
   - 手牌里有**矩阵内的简单牌**（纯 deal_damage / gain_block 组合，参考
     spec-dump.log 里 `opcode=deal_damage variant=selected` 这类行）→ 搜索应正常完成，
     路线里打出该牌的预测应与实际一致；
   - 手牌里有**矩阵外的牌**（带 Modifier/触发器/选牌等）→ 搜索中止，CombatSolver 面板
     显示失败，日志出现 `IncompatibleGameplayModException` + 桥的中文原因（指明卡牌、
     操作索引、排除原因）——这是设计行为（fail-closed），不是 bug。
3. 验收口径（CombatSolver 官方标准）：预测状态与真实执行状态严格 diff 零差异 +
   PredictionGaps 非补偿项为空；反向对照——把某张矩阵内的牌打出前后的预测/实际对比。

## 已知边界（0.1.0）

- 支持矩阵覆盖目录 931 条中的约 332 条形状（四类 opcode 家族），但**卡级生效**要求整卡
  全部操作在矩阵内——实测开局牌组约 1–2/10 张卡满足；其余卡打出时搜索中止。这是
  fail-closed 的代价，随 0.2.0+ 逐里程碑扩大覆盖。
- 晚于本桥加载的外部角色 Mod（`ExternalChaosCardModel` 子类）无法被枚举登记——这类 Mod
  需声明对本桥的 manifest 依赖以保证加载顺序。
- 数值解析复用 AutoAnthony 的 internal 投影（`OperationAmount`/`EffectiveRuntimeSpec`/
  `RuntimeSpecValue` 等，publicized 编译期引用 + 守卫成员级核验）——AutoAnthony 更新
  改动这些 internal 时守卫会 fail-closed 禁用桥，而不是给出错误预测。
- 仓库源码研究基于 AutoAnthony 0.3.119，编译与运行时锁定针对已安装的 0.3.137；
  守卫的成员级自检就是为这个版本差准备的。
