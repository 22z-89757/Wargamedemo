# Design

## Context

参见 `proposal.md` 的动机。当前 `MoveAbility` 已在视觉开始前提交棋盘占格和 AP，`UnitVisualController` 只播放传入的格路径；这为替换视觉路径而不改动规则提供了清晰边界。`ShootAbility` 当前在 `OnActivate` 立即应用伤害并结束，LOS 查询只返回布尔值，未保留实际使用的当前格或探头格。

项目已经安装 `com.unity.ai.navigation 2.0.16`，但场景和预制体中尚无 `NavMeshSurface`、`NavMeshAgent`、`NavMeshObstacle` 或 `NavMeshModifier`。运行时程序集目前也未引用 `Unity.AI.Navigation`。半障碍和全障碍已有 Collider，但没有 Rigidbody。

## Goals / Non-Goals

**Goals:**

- 让规则层继续以棋盘坐标、加权可达性、LOS、AP 和占格为唯一真值。
- 让移动和探头射击使用连续路径、可等待完成、可安全取消的视觉执行器。
- 让玩家与敌人通过相同能力和视觉组件执行射击。
- 让配置缺失、导航失败和场景清理具有确定的兜底与诊断。

**Non-Goals:**

- 不以 NavMesh 取代棋盘移动搜索、MoveCost 或单位占格。
- 不新增敌人决策、自动选目标或敌方回合 AI。
- 不引入弹道物理、碰撞判伤、命中扫描或网络同步。
- 不改变射击数值、随机规则、LOS 算法或 UI 交互流程。

## Decisions

### 1. 使用 AI Navigation 作为视觉路径提供者，不作为规则寻路器

战斗场景增加一个 `NavMeshSurface`，从场景 Collider 烘焙静态导航数据。`MoveAbility` 仍生成并提交 `MovePlan`；`UnitVisualController` 只接收逻辑起点和终点，调用 `NavMesh.SamplePosition` 校正端点并通过 `NavMesh.CalculatePath` 取得 corner 列表，再用 DOTween 沿 corners 移动和转向。

选择 `NavMesh.CalculatePath + DOTween`，而不是把单位交给 `NavMeshAgent.SetDestination`：

- 当前视觉控制器已经基于 Tween 管理完成回调、取消、转向和 bob，复用成本更低。
- 单位逻辑位置在动画开始前已经提交，Agent 的动态避让、停止距离和自动位置更新会引入额外非确定状态。
- 计算一次完整路径后播放，可以在取消时立即 Kill 并精确对齐逻辑终点。

替代方案是实现基于网格路径的曲线平滑或 funnel/string-pulling。它能完全匹配棋盘通道，但需要自行处理障碍膨胀、拐角安全距离和不同 Collider，当前项目已有 Navigation 包时性价比较低。若未来要求视觉严格不得进入任何逻辑不可通行格，应再评估该方案或为 NavMesh 建立与格规则严格一致的几何。

### 2. 两种障碍用 Collider 参与 NavMesh，Rigidbody 仅满足物理配置要求

`HalfBlocked` 与 `FullBlocked` 保持 Collider，并在根物体增加 `Rigidbody`：`isKinematic = true`、`useGravity = false`，防止障碍被单位或其他物理体推动。NavMesh 的阻挡来自烘焙时收集的 Collider/Modifier，而不是 Rigidbody 本身。

若场景中的障碍全部是静态烘焙内容，不为每个障碍增加 `NavMeshObstacle`；Carving 主要用于运行时移动障碍，会增加更新成本并可能与烘焙几何重复。若以后障碍会动态生成或移动，再针对动态对象增加 Carving 或运行时重建策略。

### 3. 移动视觉失败采用“逻辑成功、视觉吸附”的兜底

`MoveAbility` 已在激活前完成 `TryMoveUnit` 和 AP 提交，因此视觉路径失败不能安全回滚。视觉控制器遇到端点无法采样、路径非完整或播放异常时：

1. 记录包含单位、起点、终点和路径状态的错误；
2. 清理 Tween、bob 和导航临时状态；
3. 将根 Transform 对齐到逻辑目标格；
4. 通过完成回调结束能力。

这维持规则一致性，也避免独占能力永久占用。

### 4. 新增独立 `UnitShootVisualController`，由 `ShootAbility` 直接编排

射击视觉组件挂在 Unit 根物体，与 `UnitVisualController` 平级。它序列化持有：

- `modelRoot` 或可旋转视觉根；
- `muzzle` 发射点；
- 子弹预制体；
- 探头移动速度、旋转时长、子弹速度和可选命中高度偏移。

组件公开一个异步回调式入口，输入包括 Grid、目标 Unit、逻辑原格、实际射线起点和完成回调。组件负责探头出位、朝向、实例化子弹、子弹飞行、清理、归位，不读取 AP、命中率或伤害。

`ShootAbility` 构造时注入该组件，延续当前 `MoveAbility` 注入视觉控制器的模式。`BattleSceneManager` 在授予射击能力前校验组件配置；这比让 UI Controller 订阅 `AbilityActivated` 再猜测具体射击数据更直接，也能让敌人或其他调用者天然复用。这里的“类 MVC”边界是：能力/规则为 Model，能力生命周期为 Controller，射击视觉组件为 View。

### 5. LOS 查询返回稳定的开火解，而非只返回布尔值

新增只读开火解数据（名称可为 `ShootLineSolution`），至少包含攻击者逻辑格、目标逻辑格和实际射线起点。查询顺序固定为：

1. 攻击者当前格；
2. 现有 `ShootLineOffsets` 顺序中的正交相邻格。

`IsValidTarget`、预览和 `TryCommit` 共用同一查询函数。预览可携带开火格以便 UI 或调试查看；提交时重新查询并把开火格快照存入 pending 数据和最终 `ShootResult`。距离始终用逻辑起终点计算，不使用探头格。

### 6. 射击分成提交、视觉、命中应用三个阶段

射击状态流如下：

```text
CanActivate / Preview
        |
        v
TryCommit: revalidate + spend AP + roll result + snapshot firing origin
        |
        v
OnActivate: start visual (peek -> rotate -> bullet)
        |
        v
Bullet arrives: apply committed damage once
        |
        v
return from peek -> EndAbility
```

为防止取消竞态，能力保存 `damageApplied` 标记。视觉组件可在“子弹到达”与“全部视觉完成”两个节点回调，或由一个阶段枚举回调：

- 子弹到达：能力若仍激活且尚未应用结果，则应用伤害并创建 `LastResult`；
- 全部完成：结束能力；
- 取消：停止视觉并归位，若子弹已到达则不重复伤害，若未到达则不应用伤害并记录取消。

这里选择“子弹到达时判伤”，符合新增视觉的直觉。随机数和最终伤害仍在提交时固定，避免飞行期间属性变化导致二次结算。

### 7. 探头只移动 Transform，不修改 GridManager

探头开火时，射击视觉保存开始世界位置，将 Unit 根 Transform 移动到 `GridToWorld(firingOrigin)`，完成射击后返回保存位置。整个过程不得调用 `TryMoveUnit`、修改 Occupant 或改变用于距离计算的坐标。

探头路径距离仅一个正交格，默认直接 Tween；为防止视觉穿过全障碍，开火解本身已经排除 `FullBlocked`。若希望探头也绕过复杂 Collider，可复用导航路径提供者，但不作为首版必要复杂度。

### 8. 资源与程序集装配

- `Wargame.Runtime.asmdef` 增加 `Unity.AI.Navigation` 引用。
- 战斗场景加入并烘焙 `NavMeshSurface`，覆盖棋盘地面与两类障碍 Collider。
- 玩家和敌人预制体都加入 `UnitShootVisualController` 及 muzzle 子节点；保留现有 `UnitVisualController`。
- 新建轻量子弹预制体，只承担视觉，不使用 Collider 或 Rigidbody 判伤。
- `BattleSceneManager.Validate` 校验 Unit 视觉组件；对实际被授予 `ShootAbility` 的条目额外校验射击视觉配置。当前玩家获得 ShootAbility；敌人暂不自动授予，但预制体预先具备组件。

## Risks / Trade-offs

- [NavMesh 连续路径可能穿过棋盘逻辑上不可通行但没有对应几何的格子] -> 保持规则结果不变，并要求导航烘焙几何覆盖所有视觉不可穿越区域；增加场景验证和代表性路径测试。
- [单位 Transform 已到目标前，逻辑占格已提前提交] -> 延续现有设计，战斗命令通过独占能力锁等待视觉完成；不在动画期间重新查询单位世界位置作为规则依据。
- [Rigidbody 可能改变 Prefab 的物理行为] -> 使用 kinematic、关闭重力，不移动障碍；通过 PlayMode 测试确认障碍位置稳定。
- [射击取消时“是否判伤”产生歧义] -> 以子弹到达回调为唯一伤害边界；到达前取消不伤害，到达后取消不回滚且不重复。
- [目标在子弹飞行期间死亡或销毁] -> 子弹使用提交时捕获的目标表现位置或可用 Transform；应用伤害前检查目标仍存在且存活，避免 MissingReferenceException，并仍完成攻击者归位与能力清理。
- [同步 NavMesh 烘焙资源容易被遗漏] -> 将场景 NavMesh 配置、烘焙结果和验证列为独立任务，并在 PlayMode 初始化测试中检查路径可用。

## Migration Plan

1. 先增加导航引用、路径计算与移动视觉测试，保持现有逻辑移动接口可回退。
2. 配置障碍刚体和战斗场景 NavMesh，验证玩家移动绕障与失败兜底。
3. 增加射击开火解和数据快照，保持旧即时结算测试通过或按新时序更新。
4. 增加射击视觉组件与子弹资源，将玩家射击切换到异步流程。
5. 为玩家/敌人预制体装配组件，验证探头射击、取消、死亡和场景清理。
6. 若回归失败，可暂时恢复旧 `UnitVisualController` 格路径播放和即时射击激活逻辑；NavMesh 与新增预制体资源可保留但不接入能力。
