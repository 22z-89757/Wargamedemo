# 设计

## 背景

本 change 的首版射击能力、左侧目标列表和独立开火按钮已经实现。当前 `ShootRules.Range = 6`，`ShootAbility.IsValidTarget` 使用曼哈顿距离，命中率为 Aim 减 Defense。本次修订取消独立武器射程，改为当前 SightRange 与 LOS 决定可达性，并加入可配置阶梯距离修正。

用户已重构目录：射击能力位于 `Assets/Scripts/Units/Abilities/Dic/ShootAbility.cs`，预览、结果和 `ShootRules` 位于 `Assets/Scripts/Battle/ShootInfo.cs`；场景/回合管理位于 `Battle/RuntimeData`，控制器位于 `Battle/Input`，HUD 位于 `Battle/Visual`。新增 `Battle/Data` 专门保存配置类，不移动这些现有文件。

现有 `AttributeType.SightRange` 默认值为 12，直接复用其有效属性值，不新增同义属性类。`GridLineOfSightMapModifier` 是现有正确名称；继续通过它调用独立 `GridLineOfSight`，不修改主规格的 supercover 算法及阻挡定义。

## 目标 / 非目标

**目标：**

- 统一视野判断、命中预览、开火结算和结果记录所使用的整数距离。
- 在保留存活、敌我、棋盘登记、AP、回合等条件的前提下，让视野内且 LOS 畅通的敌人均能成为射击目标。
- 用场景显式引用、能力构造注入的配置控制阶梯命中率修正，保持 UI、输入和战斗规则分离。
- 复用现有能力生命周期、属性、死亡与占格清理，以及目标选择后再点击开火的操作流程。

**非目标：**

- 不建立完整武器、装备、弹药、伤害类型、暴击、反击或 AI 数据模型。
- 不实现战争迷雾、全局可见性缓存、攻击动画、掩体命中修正或高低差。
- 不修改移动的距离/寻路规则，不让单位占用、半障碍或方向掩体阻挡 LOS。
- 不支持战斗中热更新配置；运行中变化的单位属性、位置与障碍仍实时生效。

## 关键决策

### 1. 可达性只使用 SightRange 与 LOS，距离算法统一

在共享规则层提供无场景依赖的二维格距函数，按双方棋盘逻辑坐标计算：

`distance = floor(sqrt(dx² + dy²))`

这是欧氏距离向下取整，不是高斯分布函数，也不是曼哈顿距离。例如 `(1,1)` 为 1 格、`(3,4)` 为 5 格。视野比较、曲线采样和结果记录均使用这个函数，不使用世界空间坐标，不重复实现近似算法。

`ShootAbility.IsValidTarget` 保留现有敌我、存活、必要属性和棋盘登记检查，新增攻击者具有有效 SightRange 的检查，距离条件改为 `distance <= 当前 SightRange`，等于边界时可见。SightRange 使用属性系统当前有效值；缺失、负值或非有限值不产生合法视野。随后调用现有 LOS 查询。删除 `ShootRules.Range` 及独立的武器射程判定；曲线尾端也不是射程上限。

能力生命周期继续负责 AP、回合、标记和独占检查。`BattlePlayerController` 根据能力预览生成列表，目标可用性与能否支付 AP 分开判断，不因 0% 命中率排除可见敌人。这样无需引入单独的视野服务或完整武器系统。

### 2. GameSettings 通过场景装配注入能力

新增布局：

```text
Assets/Data/Game/GameSettings.asset
Assets/Scripts/Battle/Data/GameSettings.cs
Assets/Scripts/Battle/Data/ShootSettings.cs
```

`GameSettings` 继承 ScriptableObject，提供可序列化的 `ShootSettings` 字段。`ShootSettings` 是 `[Serializable]` 普通类，作为 SO 内嵌数据保存，不是另一个 SO；其中的 `AnimationCurve` 保存距离命中率修正。运行时代码位于 Scripts，数据资产位于已有 Data 下新建的 Game 文件夹。

`Battle/RuntimeData/BattleSceneManager` 持有显式序列化的 GameSettings 引用，在授予能力时把 ShootSettings 通过构造函数传给 ShootAbility，继续支持注入 `IShootRandomSource`。能力只接收所需射击设置，不持有 GameSettings、BattleSceneManager 或 HUD 引用，不调用 `Resources.Load`，不建立全局单例。

初始化时验证资产引用、ShootSettings 和曲线存在，关键帧有序、数值有限且适合阶梯采样；缺失或无效时报告明确配置错误，不静默切回旧规则。编辑器校验将曲线关键帧设置为 Constant 阶梯、首尾 Clamp；运行时在能力创建时保存只读配置快照，避免共享 AnimationCurve 被能力修改。Inspector 的配置调整在下一次初始化生效；测试用显式构造的设置覆盖默认值。

### 3. 曲线保存百分点修正，10 格为基准

曲线 X 为整数格距，Y 为命中率修正百分点，不是命中倍率，也不是最终概率。默认节点为：

| 节点距离 | 修正百分点 | 生效整数区间 |
| --- | --- | --- |
| 0 | +20 | 0–4 |
| 5 | +10 | 5–9 |
| 10 | 0 | 10–14 |
| 15 | −10 | 15–19 |
| 20 | −20 | 20 及以上 |

关键帧间使用 Constant 阶梯，不做线性或平滑插值；节点处采用该节点的新值，节点前保持左端值，首尾范围外保持端点值。例如 9 格为 +10、10 格为 0，19 格为 −10、20 格及更远为 −20。配置与运行时采样必须保证这一行为，不能只让 Inspector 看起来像阶梯而执行时插值。

最终命中率为：

`clamp(Aim - Defense + 距离修正, 0, 100)`

Aim 为 70、Defense 为 20 时，默认曲线在 5、10、15 格分别得到 60%、50%、40%。即使最终命中率为 0%，目标只要满足视野及其他合法性条件，仍能选择和开火；开火消耗 AP 并未命中。默认最远节点不限制攻击距离；更远处只要 SightRange 足够且 LOS 畅通就能射击。

AP 消耗 1、普通伤害 4、擦伤伤害 2 继续保留在共享规则中，本次不扩大可配置范围。命中后按 `clamp(Dodge, 0, 100)` 判定擦伤，再计算 `max(0, 伤害 - Armor)`；Dodge 不从 UI 的最终命中率中再扣除。

### 4. 预览与结算复用规则，ShootResult 只保存结果

`ShootInfo.cs` 中的 ShootRules 统一提供距离与命中率公式；ShootAbility 从棋盘与属性系统取值，调用注入设置采样曲线。GetPreview 与 TryCommit 使用同一套计算，不在控制器或 HUD 复制公式。

只读 ShootPreview 继续包含目标、名称、生命、可用状态、CanFire 和失败原因，新增整数距离；其 HitChance 明确代表包含距离修正的最终命中率。预览不修改任何属性。

ShootResult 当前是只读结果结构，没有射程判断逻辑。本次真正修改点是 ShootAbility 与 ShootRules；结果结构保留 Target、Hit、Grazing、Damage，并增加本次提交时的整数 Distance 和最终 HitChance 快照。后续属性变化、移动或死亡不重算历史结果。

开火不能沿用旧预览值：TryCommit 根据最新距离、属性和配置快照重新准备命中率及伤害，在提交前重新校验目标、SightRange、LOS、AP 和能力状态；再通过基类提交 AP，在 OnActivate 应用结果并 EndAbility。沿用可替换随机源与异常失败不扣 AP 的处理，避免外部随机调用引起半提交；如果准备期间相关输入改变，丢弃该次准备并报告失败，让后续请求重新计算，不提交过期参数。

状态不变时，预览 HitChance、随机命中判定与 ShootResult.HitChance 必须一致；状态变化时以成功提交的最新值为准。通过 `CharacterAttribute.ApplyBaseDelta(AttributeType.Health, -damage)` 扣血，继续进入现有 Health 归零、Unit.Kill、Died 与场景 ReleaseUnit 链，不增加平行死亡流程。

### 5. UI 保持选目标再开火，事件负责刷新

控制器仍只维护当前己方单位和当前目标，通过能力预览枚举 `scene.SpawnedUnits` 并按 UnitId 稳定排序。HUD 复用左侧列表、目标生命、命中率文本、结果文本与开火按钮；列表点击只选目标，点击开火才调用 `AbilityComponent.TryActivate`。棋盘己方点击继续选单位，拖动继续移动预览，开始移动或切换己方单位时清除攻击目标。

复用场景 Changed 与棋盘 Changed，以及已接入的 AttributeChanged、TagChanged 和能力事件。SightRange、Aim、Defense、AP、位置、LOS 和死亡变化均触发刷新；SightRange 缩小导致目标不可见时清除选择并隐藏开火按钮，扩大时加入新增可见敌人。目标仍合法但跨过曲线阶梯时保留有效选择、更新最终命中率。

AP 不足不会改变目标的视野合法性，但开火按钮隐藏或不可交互；0% 命中率不会阻止按钮启用。场景仍只在玩家命令窗口内接受操作，死亡继续取消能力、释放占格和注销回合后刷新 UI。能力无需知道任何控件或输入手势。

## 风险 / 权衡

- **[向下取整使部分对角格距离更短]** → 这是已确认规则；用 `(1,1)`、`(3,4)` 与视野边界测试固定语义，只用于本次射击相关距离，不影响移动。
- **[AnimationCurve 意外平滑或越界循环]** → 默认资产与编辑器校验使用 Constant/Clamp，测试各节点前后及尾端采样；配置错误必须明确暴露。
- **[预览后视野、属性或位置改变]** → 事件刷新配合提交时复核，结算使用最新参数，失败不扣 AP，结果保存提交快照。
- **[共享配置被运行时修改]** → 场景显式注入、能力只读快照；本次不实现热更新或配置变化事件总线。
- **[扩大视野后列表变长]** → 沿用当前简单列表和稳定排序，取得性能数据后再考虑缓存或虚拟列表。

## 迁移计划

1. 先新增配置类、默认 GameSettings 资产和共享整数距离/阶梯曲线规则，并编写对应 EditMode 测试。
2. 更新 ShootAbility、ShootPreview、ShootResult 和场景注入，删除固定射程与曼哈顿判定，保留现有目录结构和随机源测试入口。
3. 使用现有事件刷新目标集合与最终命中率，补充 SightRange、跨阶梯移动、0% 开火和结果快照测试。
4. 运行现有 EditMode、PlayMode 测试程序集并更新 README；旧版测试记录只作为历史，不代表新规则验证完成。
5. 如需回滚本次修订，恢复旧规则与构造调用并移除新增配置引用/资产；保留既有最小攻击闭环与用户目录重构。
