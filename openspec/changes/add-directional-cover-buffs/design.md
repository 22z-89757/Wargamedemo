# Design

## Context

参见 `proposal.md` 的动机。当前 `GridCell` 已保存北、东、南、西四边的 `CoverType`，`GridManager.Changed` 会在单位占格或格子数据变化后触发；Unit 已有带来源计数的 `GameplayTag.InCover` 和通用 `BuffComponent`。射击由 `ShootAbility` 在预览与提交路径分别调用 `ShootRules`，目前 `ShootPreview` 只携带一个最终命中率，`BattleHUD` 直接格式化该数值。

这项变更横跨棋盘状态、Unit/Buff、射击配置与 UI。设计必须保持 LOS、通行和 Defense 属性语义不变，并保证确认界面能够解释计算过程而不成为第二套规则实现。

## Goals / Non-Goals

**Goals:**

- 让 Unit 上的专用控制器把当前格四边掩体稳定映射成方向性 CoverBuff。
- 让射击规则按攻击者实际占格方向读取目标掩体，并由预览与提交共用一个命中率计算结果模型。
- 用结构化、可扩展的修正项支持确认公式及其他界面的最终值展示。
- 让半掩体和全掩体减值由现有 GameSettings 注入链路管理并验证。

**Non-Goals:**

- 不改变掩体地图烘焙、LOS、寻路、移动终点或探头射线选择规则。
- 不把 CoverBuff 直接实现为 Defense 属性修饰，也不让通用 Buff 系统理解攻击方向。
- 不在本次变更中加入掩体破坏、蹲伏、侧翼判定、掩体 UI 图标或 AI 掩体评分。
- 不为所有战斗公式建立通用表达式语言；本次只建立可复用的命中率明细数据结构和渲染边界。

## Decisions

### 1. UnitCoverController 通过显式棋盘注入同步环境 Buff

在 Player/Enemy Unit 预制体根对象挂载 `UnitCoverController`，要求同对象存在 `Unit`。`BattleSceneManager` 在单位生成并成功登记占格后向控制器注入当前 `GridManager`；控制器订阅 `GridManager.Changed`，每次事件只查询自己的当前占格和四边 CoverType。禁用、销毁或离开棋盘时解除订阅并清理环境 Buff。

选择这一方式是因为 Unit 本身不持有 GridManager，显式注入延续现有场景装配方式，且能覆盖出生、移动、运行时改图和移除。备选方案是在 `Update` 中按 Transform 轮询，但它会受移动插值影响并产生无谓查询；另一方案是扩展每个 GridManager 占格 API 逐一通知控制器，但容易漏掉 `TrySetCover` 等环境变化入口。

控制器刷新应比较“期望四向状态”和“当前由自己维护的状态”，只对变化方向执行 Buff 操作，避免每次全棋盘事件都刷新相同 Buff 并制造无意义 UI 事件。

### 2. 每个受保护方向使用一个环境 CoverBuff

`CoverBuff` 保存 `GridDirection` 与 `CoverType`，只允许 HalfCover/FullCover；其 BuffId 按方向稳定区分（例如 `EnvironmentCover.North`），使用永久时长与 Replace 策略。同一方向等级改变时由新实例替换，四个方向可以并存。每个实例授予 `GameplayTag.InCover`，现有 TagComponent 的来源计数保证移除一个方向不会提前清除其他方向的 InCover 标记。

CoverBuff 不添加 AttributeModifier。这样不会把“来自哪个方向”压扁为全局 Defense，也不会使非射击系统意外获得掩体减值。备选方案是四个独立标签或四个 Defense 修饰，前者会扩大 GameplayTag 枚举并重复 CoverType，后者无法正确处理射击来向，均不采用。

`UnitCoverController` 对外提供只读的按方向查询接口；射击依赖该查询契约，而不扫描 Buff 列表或识别 BuffId 字符串。

### 3. 来袭方向只使用双方实际占格坐标

以 `delta = attackerCell - targetCell` 判定目标朝向攻击者的边界：`delta.x > 0` 读取 East，`delta.x < 0` 读取 West；`delta.y > 0` 读取 North，`delta.y < 0` 读取 South。正交关系读取一个方向；两个分量均非零时读取两个方向并取较高 CoverType，只应用一次相应减值。

方向不使用 `FiringOrigin`。探头格是 LOS 与射击视觉的临时射线起点，并不改变攻击者实际占格；让它改变掩体来向会导致同一单位仅因 LOS 候选顺序不同而绕过掩体，也与已确认的玩法规则冲突。

### 4. 命中率采用不可变 Breakdown，而不是业务字符串

新增只读命中率结果模型，概念上包含：

- `BaseChance`：按现有 `clamp(Aim - Defense + distanceModifier, 0, 100)` 得到的掩体前命中率；
- 有序 `Modifiers`：每项具有稳定语义 ID、本地化显示名和带符号百分点值；本次新增 HalfCover/FullCover 项；
- `FinalChance`：从 BaseChance 顺序应用修正后钳制到 0–100 的值。

`ShootRules` 提供唯一计算入口，`ShootAbility.GetPreview` 与提交结算都调用它。`ShootPreview` 携带完整 Breakdown 并继续暴露 FinalChance 兼容现有只读消费者；`ShootResult` 至少快照最终值，若测试和结果审计需要也可保存同一不可变 Breakdown。所有集合复制成只读快照，不能由 UI 回写。

备选方案是在 ShootPreview 中加入 `CoverName`、`CoverPenalty` 和预拼公式字符串，但每新增影响因素都要扩字段并修改业务字符串分支，耦合过高。另一方案是让 UI 接收 Aim、Defense、距离和 CoverBuff 后自行计算，会产生两套公式，也不采用。

界面最终仍需要生成文本，但只在 `BattleUiText`/视图边界由通用 formatter 遍历结构化项并处理正负号、标签和数值；战斗域不提供拼好的公式，formatter 也不认识 CoverBuff 或棋盘。

### 5. 掩体减值扩展现有 ShootSettings 快照

在 `ShootSettings` 增加 HalfCoverHitChancePenalty（默认 20）和 FullCoverHitChancePenalty（默认 40），由 `GameSettings.TryValidate` 沿现有链路验证并复制进 `ShootSettings.Snapshot`。值必须有限、非负，且全掩体减值不得小于半掩体减值。运行时只读取构造 ShootAbility 时取得的不可变配置快照，与距离曲线的行为一致。

将配置放进已有 ShootSettings 而不是新建全局单例，是因为两项直接参与射击规则，现有场景已经显式注入该对象。默认资产更新为 20/40，并补充旧资产序列化字段缺省值与验证测试。

### 6. 确认窗口和简略位置使用同一预览的不同投影

`TargetUiState`/`BattleUiState` 保留结构化 Breakdown。目标列表按钮、结果提示及其他非确认位置继续读取 FinalChance；选中目标后的射击确认区域调用通用 formatter 显示“基础值 + 修正项 = 最终值”。状态刷新仍由现有控制器取得最新 ShootPreview，UI 不订阅 GridManager、不读取 UnitCoverController，也不自行判断方向。

这样可以保证移动、地图掩体修改或属性变化后，目标列表和确认公式来自同一次最新预览。确认提交仍由 ShootAbility 重新计算，防止 UI 快照被当作结算依据。

## Risks / Trade-offs

- [GridManager.Changed 是全局粗粒度事件，任一格变化会刷新全部单位控制器] → 控制器仅执行常数次本单位查询，并通过状态比较避免无变化 Buff 操作；若将来单位规模增大，再把事件细化为携带格坐标的通知。
- [BuffComponent 允许外部驱散环境 CoverBuff，可能短暂破坏棋盘与 Buff 一致性] → 控制器在下一次棋盘变化会恢复；实现时限制环境 Buff 的公开操作入口并增加一致性测试，后续如需严格即时恢复可订阅 BuffRemoved。
- [旧预制体或测试临时 Unit 缺少 UnitCoverController] → BattleSceneManager 的场景验证明确报告缺失组件；不经过战斗场景的纯射击单元测试可显式添加并初始化控制器，或使用无掩体查询桩。
- [基础命中率先钳制再减掩体与未来“所有修正相加后统一钳制”语义不同] → 本设计按“现有最终命中率作为基础，再扣掩体”的产品表达固定顺序；Modifier 保持有序，为将来有意调整顺序提供迁移点。
- [确认公式长度随修正项增加] → formatter 支持换行或分项布局，数据模型不限制具体排版；本次至少保证现有确认区域能显示基础值、一个掩体项和最终值。

## Migration Plan

1. 扩展 ShootSettings 与 GameSettings.asset，先保证旧场景可通过新配置校验。
2. 新增 CoverBuff、UnitCoverController 和方向查询接口，更新 Player/Enemy 预制体及 BattleSceneManager 装配验证。
3. 引入命中率 Breakdown 并迁移 ShootRules、ShootAbility、ShootPreview/Result 和现有测试，使无掩体行为保持原结果。
4. 接入方向性掩体修正，再迁移 BattleUiState、BattleUiText 与 BattleHUD 的详细/简略展示。
5. 运行聚焦 EditMode、完整 EditMode 与相关 PlayMode 测试，并验证 Map1 中移动、斜向掩体和确认公式。

回滚时同时还原两个 Unit 预制体、GameSettings.asset 与代码；环境 CoverBuff 不写入存档或场景运行时状态，因此没有持久数据迁移。
