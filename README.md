# AutoAnthony × CombatSolver 兼容桥

Slay the Spire 2 的纯代码 Bridge Mod（id：`AutoAnthonyCombatSolverBridge`）：让
[CombatSolver](https://github.com/Torch1230/CombatSolver) 能够预测
[AutoAnthony](https://github.com/mewcodex/AutoAnthony) 生成的随机卡牌。

它不生成新内容，也不修改两个原 Mod。它做的事只有一条链：

```
AutoAnthony 生成牌 → 读取结构化 OperationRuntimeSpec → 翻译成 CombatSolver 的预测行为 → CombatSolver 正常搜索
```

绝不解析卡牌中文/英文描述——AutoAnthony 官方契约（`COMPONENT_API.md`）明确
`OperationRuntimeSpec`（Opcode / Variant / Target / Zones / Flags / ValueSlots /
Condition / Trigger）才是执行来源，本地化文本只是输出投影。

## 安装

1. 订阅并启用前置 Mod：
   - [AutoAnthony](https://steamcommunity.com/sharedfiles/filedetails/?id=3786611028) ≥ 0.3.137
   - [CombatSolver](https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961) ≥ 0.50.1
2. 订阅本 Mod（或把 `AutoAnthonyCombatSolverBridge.dll` + `.json` 放入 `<游戏>\mods\AutoAnthonyCombatSolverBridge\`）。

两个 Mod 同时启用时桥自动生效，无需配置。

## 保守排除策略（重要）

超出支持矩阵的卡牌在模拟中按**不可打出**处理，绝不给出错误预测——求解器搜索仍能正常
完成，只是不会通过未适配的卡。设置环境变量 `AA_BRIDGE_STRICT=1` 可关闭该模式
（矩阵外卡打出时搜索中止，fail-closed）。

## 兼容性

| 组件 | 已审计版本 |
|---|---|
| Slay the Spire 2 | 0.111.0 |
| AutoAnthony | 0.3.138 |
| CombatSolver | 0.50.1 |

`CompatibilityGuard` 在 Mod 初始化时做运行时自检（全反射、零硬引用）：关键类型/成员
签名逐个核验、RuntimeSpec schema 版本检查、两个 DLL 的 MVID + SHA256 记录、
`ChaosCardModel.OnPlay` 上的 Harmony 补丁审计。**任何一项失败 → 桥整体禁用**（日志
逐条列出失败原因），游戏本身不受影响。

## 工作原理

- **镜像注册**（`CombatSolverRegistrar`）：反射枚举全部具体 Chaos 卡类（500+ 个
  sealed 类），逐个登记 OnPlay / IsPlayable / 选牌镜像。CombatSolver 按精确运行时
  类型派发，全部登记在 Mod 初始化期完成（注册表在首次根捕获后冻结）。
- **翻译执行**（`Translation/`）：`RuntimeSpecTranslator` 把每条 spec 路由到
  `(Opcode, Variant)` 键对应的 handler；数值解析复用 AutoAnthony 的 internal 投影
  （`OperationAmount` / `EffectiveRuntimeSpec` 等，publicized 编译期引用 + 守卫
  成员级核验）。
- **复合 Power**（`ChaosCompositePowerMirror`）：触发器/跨回合效果通过
  `ChaosCompositePower` 的武装、事件派发、隐藏状态捕获 / Fork / 指纹接入求解器。
- **只写模拟分支**：handler 永远操作 `SimulatedCombatState`，绝不调用真实 `OnPlay`
  或触碰真实 `CombatState`；RNG 只用模拟分支的（`context.Rng`）。

## 从源码构建

前置条件：

1. .NET SDK 9.0+；
2. 已安装 Slay the Spire 2（Steam 路径自动探测，或在 `Directory.Build.props` 写 `Sts2Path`）；
3. 已订阅 AutoAnthony 与 CombatSolver。

```powershell
cd AutoAnthonyCombatSolverBridge
# 首次：复制 LocalDependencies.props.example → LocalDependencies.props，
# 改成你机器上两个 Mod 的实际目录
dotnet build -c Release
```

构建成功后自动复制 dll/json/pdb 到 `<游戏>\mods\AutoAnthonyCombatSolverBridge\`。
纯代码改动 Build 即可，无需 Publish/PCK（`has_pck: false`）。

引用规则：两个 Mod 的 DLL 均 `Private=false`——桥只引用，绝不把它们打进自己的
发布包，否则会出现程序集重复加载。发布包只含本 Mod 的 dll + json。

## 项目结构

```
AutoAnthonyCombatSolverBridge/
├── AutoAnthonyCombatSolverBridgeCode/
│   ├── MainFile.cs                    # [ModInitializer] 入口
│   ├── Bootstrap/                     # 守卫与启动（CompatibilityGuard / 行为契约基线）
│   ├── AutoAnthony/                   # AutoAnthony 公共 API 接缝 + RuntimeSpec 读取
│   ├── CombatSolver/                  # 镜像层：OnPlay/IsPlayable/选牌/触发器/复合 Power
│   ├── Translation/                   # 翻译层：handler 注册表 + 20+ 个 handler + 修饰符/条件/目标解析器
│   ├── Patches/                       # Harmony 补丁：自动预出牌、选牌效果、回合顺序、转储
│   └── Diagnostics/                   # 日志
├── tests/BridgeChecks/                # 离线一致性检查（dotnet run）
└── docs/                              # 目录审计、实机验证证据、行为契约基线
```

## 离线检查

```powershell
cd tests/BridgeChecks
dotnet run
```

覆盖翻译表完整性、守卫契约、触发器/修饰符/条件矩阵与各 handler 的准入/拒绝行为。

## 致谢

- [AutoAnthony](https://github.com/mewcodex/AutoAnthony)（mewcodex）——生成随机卡牌的本体；
- [CombatSolver](https://github.com/Torch1230/CombatSolver)（Torch1230）——战斗求解器；
- [Alchyr/ModTemplate-StS2](https://github.com/Alchyr/ModTemplate-StS2)——Empty Mod 模板。

本 Mod 人类含量为 0%。
