# 提案

## 为什么需要

项目已经具备单位能力、行动点、属性、棋盘占格、死亡事件和独立 LOS 查询。本 change 已实现首版远程攻击闭环，本次修订将固定射程改为由视野决定攻击可达性，并把距离命中率修正提取为配置，继续复用目标列表和独立开火操作，避免规则耦合输入、HUD 或场景物理。

## 变更内容

- 射击能力统一检查攻击者回合与存活状态、目标存活状态、敌我关系、棋盘登记、视野、LOS 和 AP；取消独立武器射程上限，目标在攻击者 `AttributeType.SightRange` 范围内且 LOS 畅通，即满足距离与视线条件。
- 统一采用二维逻辑格的欧氏距离向下取整：`distance = floor(sqrt(dx² + dy²))`，同时用于 SightRange 比较和命中率曲线采样，不使用曼哈顿距离。
- 射击消耗 1 AP，基础伤害 4，最终命中率为 `clamp(Aim - Defense + 距离修正, 0, 100)`；命中后按目标 `Dodge` 概率转为擦伤，擦伤伤害 2；最终伤害为 `max(0, 伤害 - Armor)`，扣减目标 `Health`。
- 在 `Assets/Data/Game` 新增 SO 数据资产 `GameSettings.asset`，其序列化字段 `ShootSettings` 嵌入一个 `[Serializable] ShootSettings`。`GameSettings.cs` 和 `ShootSettings.cs` 放在新建的 `Assets/Scripts/Battle/Data`。
- 用非平滑阶梯 `AnimationCurve` 配置距离修正百分点：默认节点为 `(0,+20)`、`(5,+10)`、`(10,0)`、`(15,-10)`、`(20,-20)`，10 格为零修正基准，节点间保持左端值，首尾区间保持端点值；曲线只修正命中率，不限制可达性。
- 复用只读攻击预览/结算边界，统一预览与开火使用的距离和最终命中率，并让 `ShootResult` 记录本次结算距离与最终命中率；UI 不直接访问属性公式或修改生命值。
- 屏幕左侧可瞄准敌人列表按当前选中己方单位的 SightRange 和 LOS 实时筛选；切换己方单位时刷新对应列表，列表项可切换射击目标并显示包含距离修正的最终命中率。命中率为 0% 的可见敌人仍可选择和开火。
- 只有存在有效选中目标且射击能力当前可激活时显示并启用“开火”按钮；点击按钮才提交 AP 并执行射击，失败时不扣 AP。
- 射击完成后刷新 UI；目标死亡继续通过现有 `Died`、棋盘占格清理和回合注销流程处理，并清理失效的目标选择。
- 不引入完整武器/装备数据模型、弹药、动画、掩体命中修正、暴击、反击、AI、战争迷雾或复杂战斗表现；本次只增加统一射击配置，不增加全局可见性缓存或视野系统。

## 能力范围

### 新增能力

- `ranged-attack`：定义单位射击的合法性、攻击预览、命中与伤害结算，以及与 AP、属性、LOS 和死亡流程的连接。
- `ranged-attack-targeting-ui`：定义按选中己方单位生成可瞄准敌人列表、目标切换、命中率展示和开火按钮的交互契约。

### 修改能力

- 无。现有 `grid-line-of-sight` 规格只作为射击合法性检查的依赖，不修改其要求。

## 影响范围

- 运行时代码：修订 `Assets/Scripts/Units/Abilities/Dic/ShootAbility.cs` 与 `Assets/Scripts/Battle/ShootInfo.cs`，新增 `Assets/Scripts/Battle/Data` 下的配置类，并在 `Battle/RuntimeData/BattleSceneManager.cs` 完成配置注入；保持用户已有目录重构。
- 数据与场景：新增 `Assets/Data/Game/GameSettings.asset`，由战斗场景显式引用；配置通过构造函数注入能力，不使用全局单例或运行时资源查找。
- UI：复用 `Battle/Input/BattlePlayerController.cs` 与 `Battle/Visual/BattleHUD.cs` 的目标列表和开火按钮，响应 SightRange、位置、属性、死亡和 LOS 变化，展示最终命中率。
- 测试：在 EditMode 覆盖整数欧氏距离、视野边界、阶梯曲线、共享预览/结算及 AP 原子性，在 PlayMode 覆盖视野与距离变化后的列表、命中率和死亡清理。
- 依赖：复用 `AbilityComponent` 的激活/提交生命周期、`GridLineOfSightMapModifier` 与 `GridLineOfSight`、`GridManager` 的占格查询、`BattleTurnManager` 的回合状态和现有 `Unit.Died` 清理链。
