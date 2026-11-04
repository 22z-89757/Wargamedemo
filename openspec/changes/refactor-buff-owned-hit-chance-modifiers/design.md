# Design

## Context

参见 `proposal.md`。当前 `CoverBuff` 只保存方向、等级和显示名；`ShootAbility` 直接取得目标的 `UnitCoverController`，由 `ShootRules.GetTargetCover` 选出 `CoverType`，再读取 `ShootSettings` 中的 20/40 配置并构造 `HitChanceModifier`。因此能力和规则层同时知道具体 Buff、环境组件、配置字段、稳定 ID 与中文名称。

现有 `HitChanceBreakdown` 已能保存有序修正项并被 UI 通用格式化，`BuffComponent.Buffs` 也已提供只读的活动 Buff 顺序。重构应复用这两个边界，维持方向按双方实际占格计算、斜向取相邻两边最高掩体、预览与提交重新求值以及最终值钳制等既有语义。

## Goals / Non-Goals

**Goals:**

- 让具体 Buff 声明其命中率效果、适用条件、显示语义和聚合规则。
- 让射击能力只提供攻击事实并消费已解析的通用修正，不依赖任何掩体具体类型。
- 为后续命中率 Buff 提供可扩展、确定性且可快照的收集与聚合机制。
- 让掩体配置归属于掩体效果，并以不可变快照显式注入，不引入全局查找。
- 保持预览、确认公式、随机判定与提交结果来自同一套结构化数据。

**Non-Goals:**

- 不改变 LOS、距离、伤害、Dodge、Defense、目标合法性或 UI 排版。
- 不把 CoverBuff 实现为 Defense 属性修饰，也不让通用 Buff 生命周期组件理解射击公式。
- 不在本次重构中实现武器穿透、正面/侧翼标签、非射击攻击或通用表达式语言。
- 不引入事件式的“命中前修改”流程，也不允许效果直接修改结算中的可变命中率。

## Decisions

### 1. 使用只读命中率修正来源契约，而不是让能力识别 Buff

定义面向命中率领域的 `IHitChanceModifierSource`。来源接收只读 `HitChanceContext`，向收集器提供零个或多个 `HitChanceModifierCandidate`。上下文至少包含攻击者、目标、双方实际占格、攻击类型和由实际占格差值生成的目标边界方向掩码；不包含 `FiringOrigin`，从类型边界上防止探头格改变掩体来向。

`CoverBuff` 实现该契约：仅当自己仍挂在上下文目标上、攻击类型为远程射击且自身方向包含在来袭方向掩码中时，提供一个负命中率候选。候选携带稳定 ID、显示名、配置减值、`cover` 聚合组和“最强减值”策略。

这样 `ShootAbility` 不再引用 `UnitCoverController`、`CoverType`、半/全掩体配置字段或掩体文案。后续 Buff 可以实现同一契约；非 Buff 来源也可以由解析器显式接收，而无需伪装成 Buff。

备选方案是给 `CoverBuff` 添加 `GetPenalty(settings)` 并由能力强转调用，但能力仍认识具体类型和配置，未消除耦合。另一方案是在 Buff 上触发 `OnBeforeHitChance` 事件并原地修改数值，但事件顺序、重复执行和预览快照难以审计，因此不采用。

### 2. 独立解析器负责收集与聚合，BuffComponent 只负责生命周期

新增无状态的 `HitChanceModifierResolver`。它按确定顺序遍历目标当前活动 Buff 中实现来源契约的实例，收集候选，再按候选声明的规则解析为现有 `HitChanceModifier` 列表。`BuffComponent` 继续只维护施加、替换、层数、持续时间、标记和属性修饰，不新增射击专用分支。

候选支持两种本次需要的聚合方式：

- 独立追加：候选按来源顺序全部保留；
- 互斥组最强减值：同一组只保留 `Value` 最小的负修正，数值相同时保留最先出现的候选。

解析器在组第一次出现的位置保留结果槽；后续更强候选替换该槽而不改变整体顺序，保证 UI 明细顺序稳定。相同组声明冲突的聚合策略属于配置错误，应明确失败而不是依赖遍历结果。

四向掩体因此自然得到以下结果：正交来袭只有一个候选；斜向来袭的两个相邻 Buff 同属 `cover` 组，只留下较大减值；反向 Buff 不提供候选。解析器本身不认识 `CoverBuff` 或方向。

备选方案是把斜向取最高继续放在 `ShootRules` 中，但会保留规则层对掩体模型的依赖。把候选直接全部相加也会改变现有玩法，故不采用。

### 3. 掩体减值拆分为 CoverEffectSettings，并在 Buff 创建时固化

在 `GameSettings` 下新增可序列化的 `CoverEffectSettings`，保存半掩体和全掩体命中率减值、20/40 默认值、有限非负校验以及“全掩体不低于半掩体”的约束；其不可变 `Snapshot` 根据 `CoverType` 返回对应减值。`ShootSettings` 只保留距离曲线及其快照。

使用持有 `CoverEffectSettings.Snapshot` 的 `ICoverBuffFactory` 创建 `CoverBuff(direction, coverType, penalty)`。`BattleSceneManager` 校验两份配置并创建工厂，再将同一个工厂与棋盘显式注入所有 `UnitCoverController`。控制器只负责比较格子环境并请求工厂创建对应 Buff，不读取 GameSettings 或解释减值。

Buff 实例持有创建时的不可变 penalty，使它能够独立产生命中率候选，不在结算时访问全局资产。项目当前配置在战斗初始化后视为快照；运行时修改 ScriptableObject 不追溯更新已存在 Buff，与现有 ShootSettings 快照语义一致。

备选方案是把 `CoverEffectSettings` 直接传给每个 `UnitCoverController`，实现更少但让环境同步组件承担配置映射；工厂能保持职责更窄。继续把减值放在 `ShootSettings` 中虽可工作，但会让掩体 Buff 的效果配置仍归射击能力所有，故不采用。

### 4. 射击通过统一 ShotEvaluation 快照复用预览与提交路径

将合法性事实、距离、实际射线起点、基础属性输入、已解析修正项和 `HitChanceBreakdown` 汇总为一次不可变 `ShotEvaluation`。预览调用统一求值入口并直接投影为 `ShootPreview`；提交在消耗 AP 和应用伤害前重新调用相同入口。

为保留现有“随机准备期间输入变化则拒绝提交”的原子性，提交在随机值取得后再次求值，并结构化比较双方坐标、相关属性、射线起点及修正项的 ID/显示名/值序列。只有结果仍等价才允许进入现有 AP 提交和视觉流程。这样 CoverBuff 替换、移除或其他未来命中率 Buff 变化都会被捕获，而无需在 `ShotParameters` 中增加具体 Buff 字段或依赖全局 Buff 版本。

`ShootRules` 继续只计算整数距离、基础命中率、Dodge 和伤害；删除 `GetTargetCover`。命中率解析器输出的普通 `HitChanceModifier` 仍交给 `GetHitChanceBreakdown`，因此 UI 和 `ShootResult` 数据结构无需改变。

备选方案是给 `BuffComponent` 增加全局 Revision 并在提交时比较，但任何与命中率无关的 Buff 变化都会造成误拒绝；结构化比较只关注本次射击实际输入。

### 5. 装配校验继续保证正式战斗单位具有掩体同步能力

`ShootAbility` 不再把 `UnitCoverController` 是否存在或初始化视为射击目标合法性的一部分；目标的通用 `BuffComponent` 是修正来源入口，没有候选就等价于无额外修正。正式战斗仍由 `BattleSceneManager` 校验 Unit 预制体具备启用的 `UnitCoverController`，并在占格登记后注入棋盘和工厂，因此缺失环境同步组件会在场景初始化阶段明确失败。

这一区分使能力不依赖环境实现，同时不会让正式场景静默失去掩体。临时测试 Unit 可按测试目的决定是否装配控制器，不再为测试无掩体射击而创建空控制器。

## Risks / Trade-offs

- [Buff 自己引用命中率效果契约，Units 层仍知道“命中率”概念] → 契约放在独立的战斗效果边界中，只暴露数据和上下文，不引用 ShootAbility、UI 或棋盘服务；这是效果所有权所需的单向依赖。
- [候选聚合规则扩展后可能出现组策略冲突] → 候选包含显式组和策略，解析器对同组冲突立即报错，并用稳定顺序保证可复现结果。
- [移动序列化字段可能让旧 GameSettings 资产丢失 20/40] → 同一变更显式迁移项目资产并执行配置校验；默认字段仍为 20/40，不在运行时静默猜测损坏配置。
- [工厂注入增加临时 Unit 的装配参数] → 提供小型、不可变的配置工厂；只需要环境掩体的测试或场景才注入，纯无掩体射击不依赖控制器。
- [结构化二次求值增加少量遍历] → 每个目标 Buff 数量很小，预览和提交仅做线性只读遍历；换取确定性和具体类型解耦。
- [当前变更依赖尚未归档的方向性掩体实现] → 实施前以 `add-directional-cover-buffs` 的当前代码和同步后规格为基线；若该变更先发生调整，应先核对构造、配置和 Breakdown 边界再应用本设计。

## Migration Plan

1. 先建立攻击上下文、修正候选、来源契约、解析器及结构化等价比较，不切换现有射击入口。
2. 拆分 `CoverEffectSettings`，更新 `GameSettings.asset`、校验与快照；实现配置化 CoverBuff 工厂并迁移 `UnitCoverController` 初始化链路。
3. 让 `CoverBuff` 提供方向性候选，使用解析器验证正交、斜向最高值、反向无效和稳定顺序与旧结果一致。
4. 将预览和提交迁移到统一 `ShotEvaluation`，移除 `ShootAbility`/`ShootRules` 的具体掩体依赖及旧配置字段。
5. 更新现有临时 Unit 装配与文档，运行 Unity 编译、相关既有 EditMode/PlayMode 回归和场景配置校验；不为测试暴露生产专用接口。

回滚时恢复 `ShootSettings` 的掩体字段和原射击分支，并同时还原 GameSettings 资产、控制器初始化签名和 CoverBuff 构造；该重构不写入存档或持久运行时状态，无数据回滚步骤。
