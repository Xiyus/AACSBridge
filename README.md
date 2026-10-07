# AACSBridge

Slay the Spire 2 的纯代码 Bridge Mod（id：`AutoAnthonyCombatSolverBridge`）：让
[CombatSolver](https://github.com/Torch1230/CombatSolver) 能够预测
[AutoAnthony](https://github.com/mewcodex/AutoAnthony) 生成的随机卡牌。

## 重要

本 Mod 人类含量为 0%，出现问题就是蓝色大肥鱼的锅。噢，还有白色龙娘。
以后也未必更新，想适配新版本直接拿本仓库扔给AI。

## 当前状态

项目仍在实机验证阶段。当前有 241 个翻译注册键，156 项离线检查通过。
安装版目录的 467 个原子中，440 个能单独准入；其余需要合法配对上下文。
这些统计不代表全部整卡组合已通过战斗等价验证。实机测试曾发现并修复选牌候选、
触发目标、费用授权、临时属性和变形等偏差，仍可能存在未覆盖的组合。

它不生成新内容，也不修改两个原 Mod。它做的事只有一条链：

```
AutoAnthony 生成牌 → 读取结构化 OperationRuntimeSpec → 翻译成 CombatSolver 的预测行为 → CombatSolver 正常搜索
```

不解析卡牌中文/英文描述——AutoAnthony 官方契约（`COMPONENT_API.md`）明确
`OperationRuntimeSpec`（Opcode / Variant / Target / Zones / Flags / ValueSlots /
Condition / Trigger）才是执行来源，本地化文本只是输出投影。

## 安装

1. 订阅并启用前置 Mod：
   - [AutoAnthony](https://steamcommunity.com/sharedfiles/filedetails/?id=3786611028) ≥ 0.3.137
   - [CombatSolver](https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961) ≥ 0.50.1
2. 将构建或发布包中的 `AutoAnthonyCombatSolverBridge.dll` 与 `AutoAnthonyCombatSolverBridge.json`
   放入 `<游戏>\mods\AutoAnthonyCombatSolverBridge\`，然后在游戏中启用并重启游戏。

两个 Mod 同时启用时桥自动生效，无需配置。

## 保守排除策略

超出支持矩阵的卡牌在模拟中按**不可打出**处理，减少使用未适配效果的风险。
已准入的效果仍可能存在实现偏差，保守排除不能保证全部预测正确。设置环境变量 `AA_BRIDGE_STRICT=1` 可关闭该模式
（矩阵外卡打出时搜索中止，fail-closed）。

## 兼容性

| 组件 | 已审计版本 |
|---|---|
| Slay the Spire 2 | 0.111.0 |
| AutoAnthony | 0.3.138 |
| CombatSolver | 0.50.1 |

`CompatibilityGuard` 在 Mod 初始化时核验关键类型/成员、RuntimeSpec schema，记录两个 DLL 的
MVID 与 SHA256，并审计相关 Harmony 补丁。版本号或哈希变化不会单独阻止启用；
未匹配已审计哈希的构建还须通过相关方法和字段的行为契约检查。
相关行为的等价重构也可能要求复查。**任何必需契约检查失败 → 桥整体禁用**（日志
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
git clone https://github.com/Xiyus/AACSBridge.git
cd AACSBridge
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
└── docs/                              # 目录准入审计、行为契约基线
```
