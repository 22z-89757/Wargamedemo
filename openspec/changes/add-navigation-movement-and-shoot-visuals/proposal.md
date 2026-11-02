# Proposal

## Why

当前移动与射击的战斗规则已经基于棋盘坐标、可达性和 LOS 独立结算，但单位表现仍直接沿逻辑格路径逐格移动，射击也会即时结算而没有可复用的视觉执行阶段。需要在不改变棋盘规则真值的前提下，引入连续避障移动、子弹表现和探头射击往返动作，使玩家与后续敌人行为共享一致、可等待且可取消的视觉闭环。

## What Changes

- 保留现有棋盘加权可达性、逻辑路径、AP 消耗与占格提交作为移动规则真值；移动视觉改用 Unity AI Navigation 计算连续世界空间路径，不再逐格播放逻辑路径。
- 为战斗地图配置可烘焙的 NavMesh 表面，并让 `HalfBlocked`、`FullBlocked` 预制体通过 Collider 参与导航几何；按需求增加 kinematic Rigidbody，但不依赖刚体决定 NavMesh 阻挡。
- 为单位增加独立的射击视觉组件；拥有 `ShootAbility` 的单位必须同时具有可用的射击视觉组件，战斗装配阶段进行校验。
- 新增子弹预制体与射击视觉参数，射击视觉负责朝向目标、生成并移动子弹，以及在完成或取消时清理临时对象。
- 将射击结算扩展为异步视觉流程：能力提交时固定命中、擦伤、伤害、实际开火格等结果，视觉完成后应用伤害并结束能力，防止表现期间重复下令。
- 射击 LOS 查询返回实际采用的射线起点；若使用正交相邻探头格，单位视觉先临时移动到该格开火，再返回其逻辑原位。棋盘坐标、占格、距离、AP 与目标合法性始终使用原逻辑位置。
- 玩家与敌人共用同一套射击视觉接口；本变更不新增敌人决策 AI，但敌人一旦获得 `ShootAbility` 即使用相同的探头、子弹和归位流程。

## Capabilities

### New Capabilities

- `navigation-movement-visuals`: 定义在棋盘移动规则保持不变时，单位如何通过 NavMesh 连续避障移动到已提交的目标格。
- `shooting-visuals`: 定义射击视觉组件、子弹表现、射击结果应用时序，以及探头格临时出位和归位行为。

### Modified Capabilities

- `ranged-attack`: 射击合法性查询需要选定并记录实际射线起点，射击结算由即时完成改为等待视觉流程结束，探头移动不得改变逻辑位置或规则参数。

## Impact

- 受影响代码：`MoveAbility`、`ShootAbility`、`ShootInfo`、`UnitVisualController`、`BattleSceneManager`，并新增射击视觉组件及相应测试。
- 受影响资源：玩家/敌人 Unit 预制体、`HalfBlocked`/`FullBlocked` 障碍预制体、战斗场景、导航数据以及新子弹预制体。
- 依赖：继续使用已安装的 `com.unity.ai.navigation 2.0.16`，运行时程序集需要引用 `Unity.AI.Navigation`。
- 兼容性：不改变现有移动可达范围、移动 AP 成本、射击距离公式、命中率公式、LOS 阻挡规则和棋盘占格语义。
