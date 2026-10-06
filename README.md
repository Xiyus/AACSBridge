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

## 当前状态：v0.0.4（观察模式）

| 里程碑 | 内容 | 状态 |
|---|---|---|
| ① | 空模板 Mod 能编译并被游戏加载 | ✅ 代码就绪，待你启动游戏验证 |
| ② | 同时引用 AutoAnthony.dll + CombatSolver.dll | ✅ 编译期引用 + Publicizer 已配置 |
| ③ | 识别 `ChaosCardModel`（AutoAnthony 生成牌） | ✅ `RuntimeSpecDumpPatch` 已实现 |
| ④ | 把生成卡的全部 `OperationRuntimeSpec` 打进日志 | ✅ 结构化转储已实现 |

0.0.4 **不向 CombatSolver 注册任何镜像**（fail-closed：部分适配比完全不适配更危险）。
进入战斗后桥只做两件事：识别生成牌、转储 RuntimeSpec。CombatSolver 对 AutoAnthony 内容
的既有行为不变。

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
    │   └── CombatSolverRegistrar.cs          # 注册器（0.0.4 刻意零注册，0.1.0 填充）
    ├── Translation/
    │   ├── OperationKey.cs                   # (Opcode, Variant) 路由键
    │   ├── IOperationHandler.cs              # 翻译器接口（0.1.0 起承载执行）
    │   ├── OperationHandlerRegistry.cs       # 处理器注册表
    │   ├── RuntimeSpecTranslator.cs          # spec → handler（无 handler 即抛异常）
    │   └── UnsupportedRuntimeSpecException.cs
    ├── Patches/
    │   └── RuntimeSpecDumpPatch.cs           # 里程碑③④：CombatState.AddCard 只读转储（开局牌组+战斗中生成牌全覆盖）
    └── Diagnostics/
        └── BridgeLog.cs                      # 游戏 Logger + 独立 spec-dump.log 文件
```

---

## 兼容性锁定

| 组件 | 版本 | SHA256 |
|---|---|---|
| Slay the Spire 2 | 0.111.0 | — |
| AutoAnthony | 0.3.137 | `ad004f42f18ed86ccc7f66317823cf77fb40195b6466872bbd7038b3d8952965` |
| CombatSolver | 0.50.0 | `304699188c544c34795d5409e8811d2f4901412903da5bc4ba4b8043d5b5d2bd` |

`CompatibilityGuard` 在 Mod 初始化时做运行时自检（CombatSolver 第三方适配文档的要求）：

- 两个程序集已加载、manifest 版本 ≥ 锁定最低版（AutoAnthony 0.3.137 / CombatSolver 0.50.0）；
- 关键类型/成员签名逐个存在（`ChaosCardModel.Generated`、`CardOnPlayMirrors.Registry`、
  `AdaptedCardOnPlayMirrors.Register`、`KnownPreRootSubscriberTypeNames` 等，全反射、零硬引用）；
- `OperationRuntimeSpec.CurrentSchemaVersion == 1`（schema 变了 = 拒绝启用）；
- 记录两个 DLL 的 MVID + SHA256 到日志；
- 审计 `ChaosCardModel.OnPlay` 上的 Harmony 补丁（决定 0.1.0 走普通注册表还是 `AdaptedCardOnPlayMirrors`）。

**任何一项失败 → 桥整体禁用，一个补丁都不打、一个镜像都不注册**（日志会列出全部失败原因）。

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
2. 日志出现 `[AA-CS Bridge] 桥已启用：AutoAnthony 0.3.137（MVID ...）+ CombatSolver 0.50.0（MVID ...）`（含 MVID/SHA256）→ ②；
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
| 0.0.1–0.0.4 | ✅ 模板 / 双 DLL 引用 / 守卫 / ChaosCard 识别 / RuntimeSpec 转储 |
| 0.1.0 | Damage / Block / Draw / Energy 四个 handler + `CardOnPlayMirrors`/`CardIsPlayableMirrors` 注册 |
| 0.2.0 | Power / Debuff（`SimulatedCombatState.Apply`） |
| 0.3.0 | 牌堆移动（Discard / Exhaust / Create / Shuffle） |
| 0.4.0 | Target 展开 / X 费 / 模拟 RNG |
| 0.5.0 | Player Choice（选牌分支） |
| 0.6.0 | Trigger / `ChaosCompositePower` 跨回合（含 `PowerHiddenStateMirrors`） |
| 0.7.0 | 生成牌递归模拟 |
| 0.8.0 | 全 Component Catalog 审计（931 条 spec 逐条核对） |
| 1.0.0 | strict diff 全通过 + PredictionGaps = 0 → 发布 |

## 已知边界（0.0.4）

- 桥对 CombatSolver 的适配 API **只做反射验证，尚未调用**——publicizer 已在 csproj 配好，
  0.1.0 直接可用；运行时游戏加载的仍是原版 `CombatSolver.dll`。
- AutoAnthony 侧只用公共 API（`Generated` / `Definition` / `Operations` / `RuntimeSpec`）。
  `ChaosOperationExecutor.EffectiveRuntimeSpec`（含升级 delta 的执行视角）是 internal，
  0.1.0 需要时再评估：走 publicizer 或自算升级投影。
- 仓库源码研究基于 AutoAnthony 0.3.119，编译与运行时锁定针对已安装的 0.3.137；
  守卫的成员级自检就是为这个版本差准备的。
