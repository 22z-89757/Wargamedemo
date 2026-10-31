# 任务

首版最小攻击闭环已实现。本次按确认后的视野与距离曲线规则重新打开受影响任务；已勾选项仅表示现有且未受影响的基础设施，不能视为新规则已经验证。实施时保留用户已有目录重构。

## 0. 射击配置资产

- [x] 0.1 在 `Assets/Scripts/Battle/Data` 新增 `GameSettings.cs`（ScriptableObject）和 `[Serializable] ShootSettings.cs`；GameSettings 的序列化 `ShootSettings` 字段嵌入配置，ShootSettings 用 AnimationCurve 保存命中率修正百分点。提供必要配置校验、Constant 阶梯与 Clamp 首尾设置，并在能力初始化时形成只读配置快照；用 EditMode 测试验证有效配置和缺失/非法配置行为。
- [x] 0.2 在已有 `Assets/Data` 下新建 `Game`，创建 `GameSettings.asset`，默认节点为 `(0,20)`、`(5,10)`、`(10,0)`、`(15,-10)`、`(20,-20)`；验证节点间不平滑插值、节点立即跳变、首尾保持端点值，并生成对应 Unity 元数据。

## 1. 射击规则与能力核心

- [x] 1.1 修订 `Assets/Scripts/Battle/ShootInfo.cs` 的共享规则，删除固定 Range，新增 `floor(sqrt(dx² + dy²))` 二维逻辑格距，统一用于视野、曲线采样和结果记录；命中率改为 `clamp(Aim - Defense + 距离修正, 0, 100)`，保留 AP 消耗 1、普通伤害 4、擦伤伤害 2 及 Dodge/Armor 规则。用 EditMode 测试覆盖 `(1,1)`、`(3,4)`、钳制、阶梯节点与配置覆盖默认值。
- [x] 1.2 更新 `Assets/Scripts/Units/Abilities/Dic/ShootAbility.cs` 的构造注入、目标检查和 GetPreview：改用当前 `AttributeType.SightRange` 与 LOS，保留存活、敌我、必要属性、棋盘登记和能力检查，不再使用曼哈顿或独立武器射程；ShootPreview 增加整数距离，HitChance 使用最终命中率。测试视野等值边界、SightRange 动态变化、超过旧 6 格仍可射击、LOS 阻挡、0% 目标仍合法，以及预览无副作用。
- [x] 1.3 更新提交与结算，开火时重新计算当前距离和最终命中率；提交前复核 SightRange、LOS 与全部能力条件，准备期间输入改变或随机源异常时不扣 AP。ShootResult 增加本次 Distance、HitChance 快照，继续使用既有 Health 扣血与 EndAbility；测试预览/结果一致、移动跨阶梯后重算、0% 必失、普通命中/擦伤及失败无半提交。

## 2. 战斗场景接入

- [x] 2.1 在 `Battle/RuntimeData/BattleSceneManager.cs` 显式引用 GameSettings，并更新 `Map1` 场景配置；生成玩家单位时将 ShootSettings 注入 ShootAbility，缺失必要配置时给出明确错误，不使用全局单例或隐式资源查找。更新场景初始化测试与相关构造调用，保持敌方控制边界、死亡后占格和回合注销流程。
- [x] 2.2 已将单位属性/标签变化和射击能力事件接入现有 Changed 刷新通道，并在清理时解除订阅。保留这套刷新基础设施；SightRange 新语义及列表行为的验证列入 3.1、4.2。

## 3. 目标选择控制器与 HUD

- [x] 3.1 更新 `Battle/Input/BattlePlayerController.cs` 的目标筛选与刷新，使用能力预览中的 SightRange+LOS 合法性及最终命中率，保持稳定排序和切换己方单位清除目标；SightRange 缩小时移除无效目标，扩大时加入新目标，合法目标跨阶梯时更新命中率；0% 敌人仍可选，AP 不足仅限制开火。用 PlayMode 测试验证这些行为。
- [x] 3.2 已在 `Battle/Visual/BattleHUD.cs` 和场景中配置屏幕左侧目标列表、目标名称/命中率/生命文本与开火按钮。保留 UI 只渲染预览数据的边界；新最终命中率的显示验证列入 4.2。
- [x] 3.3 已连接“选择玩家 → 点击列表项选目标 → 查看命中率 → 点击开火”的交互链；列表点击不扣 AP，开火走能力完整校验，死亡后移除目标并清理选择。保留交互流程，其新规则回归列入 4.2。

## 4. 验证与文档

- [x] 4.1 更新旧固定射程/曼哈顿/无距离修正的 EditMode 测试与构造调用，覆盖整数欧氏距离、SightRange 边界与失效、阶梯全部边界及尾端、配置注入、最终概率、结果快照、AP 原子性和死亡清理；运行 `Wargame.EditModeTests` 并记录新规则结果。
- [x] 4.2 更新并运行 `Wargame.PlayModeTests`：覆盖不同玩家 SightRange 对应不同列表、视野扩大/缩小与 LOS 变化、跨阶梯移动后的最终命中率、0% 目标选择与开火、AP 不足、死亡清理；回归现有目标切换、独立开火、移动与相机流程，并记录新规则结果。
- [x] 4.3 更新 `Assets/Scripts/README.md`，说明当前目录、GameSettings/ShootSettings 配置与注入、整数欧氏距离、视野可达性、默认阶梯百分点、最终命中率公式及 ShootResult 快照；核对公开 API 与 UI 操作顺序，保留原项目通过已打开 Editor 运行测试的说明。

## 工作流后续

- 使用 `$openspec-apply-change` 实现本次重新打开和新增的任务。
- 完成实现与测试后，运行 OpenSpec 校验，再归档此 change。

## 验证记录

以下均为 **本次规划修订前的旧版规则验证**，使用固定 6 格、曼哈顿距离及不含距离修正的命中率；不代表 SightRange、整数欧氏距离、GameSettings 或阶梯曲线已实现或通过测试。

- 2026-10-31，使用 Unity 6000.3.27f1 在独立项目副本运行测试，保留当前打开的 Editor。
- `Wargame.EditModeTests`：153/153 通过；覆盖旧固定公式、合法性检查、AP 不足与随机源异常、失效预览、命中/擦伤/未命中及死亡事件。
- `Wargame.PlayModeTests`：18/18 通过；覆盖鼠标点击选敌与开火、多敌人/多玩家切换、属性与 LOS 刷新、AP 不足、击杀清理和既有移动/相机流程。
- 原始结果保存在本地忽略目录 `Logs/Tests/add-minimal-ranged-attack-20261031/editmode.xml` 和 `playmode.xml`。
- 同日通过 Unity CLI 连接原项目已打开的 Editor 再次验证旧版规则：EditMode 153/153 通过（约 5 秒）；PlayMode 使用 `--async_tests`，18/18 通过（约 19 秒）。原项目结果保存为同目录下的 `original-editmode.json` 和 `original-playmode.json`。

### 本次视野与阶梯曲线规则验证（2026-10-31）

- 通过 Unity CLI 连接原项目已打开的 Unity 6000.3.27f1 Editor；脚本重新编译通过，没有启动第二个 Editor。
- `Wargame.EditModeTests`：197/197 通过（约 10 秒）；包含整数欧氏距离、SightRange 边界与变化、阶梯节点/首尾、默认资产加载、自定义配置与只读快照、预览/结算一致、0% 未命中、准备期间输入变化不扣 AP，以及既有属性/移动/死亡等回归。
- PlayMode 首次运行 20/21 通过；新射击用例全部通过，原有边缘滚屏测试在输入队列处理松手前采样期望坐标，发生跨帧差异。已将断言改为松手处理后的最终相机坐标，并使用松手前的可达快照核对 AP 费用，未修改相机或移动生产逻辑。
- `Wargame.PlayModeTests` 重跑：21/21 通过（约 23 秒）；覆盖玩家视野差异、视野扩大/缩小、LOS 失效、跨阶梯概率与提交值一致、0% 开火、AP 限制、死亡清理，以及原有鼠标/触屏移动与相机流程。
- 本次结果保存在本地忽略目录 `Logs/Tests/ranged-attack-sight-curve-20261031/`，包括 `editmode.json`、`playmode-first-run.json` 以及最终 PlayMode 结果。
