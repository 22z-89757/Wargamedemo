# 设计：战斗 UI MVC 重构

## Context

当前 `BattleHUD` 同时读取 `BattleSceneManager`、拼接英文文案、创建目标按钮并绑定结束回合/开火事件；`BattlePlayerController` 同时持有选中单位、移动手势、射击目标和刷新脏标记。`BattleTurnManager` 已提供回合开始/结束事件和轮数，`Unit`/`CharacterAttribute` 提供 AP 与属性事件，`AbilityComponent` 已提供能力列表、能力生命周期事件和最终 `CanActivate`/`TryActivate` 校验。

`Map1` 目前把状态文本、提示、目标列表、开火、结束回合和旋转控件混在 `BattleUICanvas` 下；`BattleInputReader.IsOverUI` 通过 GraphicRaycaster 判断棋盘输入是否被 UI 阻挡。`UnitHealthBarCanvas` 已独立存在，不能因为本次拆分重新并入战斗 HUD。

## Goals / Non-Goals

**Goals:**

- 让战斗状态经过可测试的 UI 模型/视图模型后再进入视图，控制器只处理用户意图和战斗 API 调用。
- 让顶部回合信息、右侧选中单位信息、能力列表、目标预览和确认提示按事件或脏标记独立刷新。
- 保证只读信息层不命中 UI，交互层仍能阻止同一指针事件继续进入棋盘。
- 保留现有远程攻击合法性、预览、AP 提交、移动手势和血条生命周期。
- 让所有玩家可见文本集中经过中文文案映射，避免能力内部 ID 直接出现在 UI。

**Non-Goals:**

- 本次不改变 AbilityBase 的目标规则、射击概率、移动路径或回合轮转算法。
- 不引入通用多语言资源系统；中文是本次战斗 UI 的固定显示语言。
- 不把血条改造成 MVC 的一部分，也不让血条参与 UI 射线或目标可见性判断。
- 不设计新的敌方 AI 或能力编辑器；能力按钮只暴露已有已授予能力。

## Decisions

### 1. 使用战斗 UI 状态模型隔离数据与 Unity 控件

新增面向 UI 的纯数据快照（可按文件拆为 `BattleUiState`、`SelectedUnitUiState`、`AbilityUiState` 和 `TargetUiState`），由场景/玩家控制器在战斗事件、属性事件和选择变化后重建。快照包含回合数、行动方、选中单位、AP、能力显示名称、AP 费用、`CanActivate` 结果/中文原因以及目标预览；不保存 `Text`、`Button` 或 `RectTransform`。

`BattleUiController` 负责订阅 `BattleSceneManager.Changed`、`BattleTurnManager.FactionTurnStarted/Ended`、选中单位的属性/标签变化和 `AbilityComponent` 能力事件，接收目标选择、能力点击、开火、旋转、结束回合确认事件。它调用现有能力组件和场景管理器，之后请求模型刷新。视图组件只接受快照并渲染，不读取 `Unit` 或直接调用战斗 API。

替代方案是继续让 `BattleHUD` 直接读取场景并拆成多个子面板。该方案仍会把 Unity 控件生命周期与战斗逻辑绑定，测试难以覆盖；因此选择显式状态模型和控制器。

### 2. 按职责拆成只读视图、交互视图和弹窗视图

保留 `UnitHealthBarCanvas` 为独立层，并把原 `BattleUICanvas` 的内容迁移为：

```text
BattleUIRoot
  +-- BattleInfoCanvas          (只读，无 GraphicRaycaster)
  |     +-- 回合数 Text
  |     +-- 行动方 Text
  |     +-- 只读提示/结果
  |     +-- 选中单位名称、AP 和能力状态文本
  +-- BattleInteractionCanvas   (有 GraphicRaycaster)
  |     +-- 左侧目标列表按钮
  |     +-- 右侧能力操作按钮
  |     +-- 开火、旋转、结束回合、取消移动
  |     +-- EndTurnConfirmDialog
  +-- UnitHealthBarCanvas       (既有独立血条层)
```

能力面板可由只读文本显示能力名称/AP/原因，同时由交互 Canvas 对齐一组透明或带样式的能力按钮；按钮不得依赖只读文本的 Graphic 射线。只读 Canvas 不挂 `GraphicRaycaster`，其所有 `Graphic.raycastTarget` 关闭；交互 Canvas 保留唯一负责棋盘 UI 命中判断的 Raycaster。确认对话框位于交互 Canvas 顶层，打开时只阻挡对话框区域及其背景规定的输入。

替代方案是保留一个 Canvas 并逐个设置 RaycastTarget。这样容易遗漏动态列表和新增文本，也无法表达只读层级的约束；拆分 Canvas 可在初始化校验中直接验证。

### 3. 能力列表复用 AbilityComponent，不复制可用性规则

模型遍历选中单位 `AbilityComponent.Abilities`，按授予顺序建立稳定列表。每项从 `AbilityBase.DisplayName`、`GetActionPointCost`、冷却、剩余次数和 `AbilityComponent.CanActivate` 获取数据；中文能力名由 UI 文案映射提供，未知能力使用中文兜底名称，不修改能力的内部 ID。

能力按钮事件只携带能力引用或 `AbilityId`，由控制器映射到现有交互：

- `MoveAbility` 进入/保持当前拖拽移动模式，最终仍由现有释放流程调用 `TryActivate`。
- `ShootAbility` 打开左侧目标列表，目标按钮仍只设置选中目标，开火按钮才调用 `TryActivate`。
- `TargetType.None` 的能力可在通过控制器复核后直接调用 `TryActivate`；`Self`、`Cell` 或其他能力按其既有目标入口处理。

能力按钮的禁用状态由模型快照提供，点击时仍必须再次调用能力组件的最终检查，避免 AP 或回合在刷新间隔内改变造成绕过。

### 4. 将结束回合确认建模为显式的 UI 状态

场景管理器提供只读查询：当前为玩家回合、战斗可结束、无活动手势/旋转，并且当前玩家阵营存活单位中是否存在 AP 大于零者。UI 控制器点击结束回合时先执行查询：

```text
[空闲]
   | 点击结束回合
   v
[检查玩家存活单位 AP]
   | 全部为 0                 | 任一 > 0
   v                          v
[直接请求结束回合]       [显示确认弹窗]
                              | 取消 -> [空闲]
                              | 确认 -> 再检查条件
                                           | 仍有效 -> [请求结束回合]
                                           | 已失效 -> [刷新/关闭]
```

确认按钮不可保存旧的“可结束”结果；再次确认时重新检查当前回合、活动能力、手势、旋转和战斗状态。场景管理器保留原子 `TryEndPlayerTurn` 作为唯一实际轮转入口，避免直接操作 `BattleTurnManager`。

### 5. 统一中文文案并保留领域数据格式

新增集中式 `BattleUiText` 或等价文案映射，将回合、提示、按钮、失败原因和射击结果转换为中文。命中率、AP、距离和生命值仍按现有数值格式渲染；`ShootResult`、`AbilityFailReason`、`AbilityId` 等领域对象不改名、不序列化中文替代值。这样可以避免在多个视图中出现同一状态的不同翻译。

### 6. 以事件驱动刷新，保留现有 LateUpdate 手势时序

控制器继续在现有 `LateUpdate` 中完成相机后的移动目标投影和射击预览刷新；UI 模型刷新可以由事件标记为 dirty，在同一帧的 UI 渲染阶段读取最终属性。目标按钮和能力按钮的动态对象只由对应交互视图维护，视图重建必须保持按钮索引与快照项一致，并在切换单位时清除旧事件监听。

## Risks / Trade-offs

- [风险] 现有测试直接依赖 `BattleHUD` 的 `TargetButtons`、`FireButton`、`Render` 等旧入口，迁移期间可能失去覆盖 → [缓解] 先把面向玩家行为的断言改为新的状态快照、控制器事件和视图结果，再删除只验证旧 getter、按钮集合或渲染入口的非必要测试；旧测试入口不作为兼容契约保留。
- [风险] 只读 Canvas 拆分后动态 Text 或分隔线遗漏 `raycastTarget=false` → [缓解] 初始化校验遍历只读 Canvas 的全部 `Graphic`，并在 PlayMode 对 `BattleInputReader.IsOverUI` 做实际鼠标/触摸验证。
- [风险] 能力列表显示的可用状态与点击时状态之间存在一帧差异 → [缓解] 按钮点击始终重新调用 `CanActivate/TryActivate`，失败只刷新中文原因，不扣除额外 AP。
- [风险] 右侧信息和能力按钮分属两个 Canvas 后布局错位 → [缓解] 两个 Canvas 使用相同参考分辨率、缩放模式和安全区根节点；用场景装配检查和至少两种分辨率验收。
- [风险] 结束回合确认弹窗与移动/相机手势同时存在 → [缓解] 弹窗打开时由控制器取消或冻结当前手势，并让交互 Canvas 阻止棋盘输入；取消后恢复选择和快照，不自动提交移动。
- [风险] 部分能力没有中文显示名 → [缓解] 优先读取显式 UI 映射，未知能力使用“未命名操作”加内部能力索引的中文兜底，并记录配置错误供场景验收。

## Migration Plan

1. 先新增 UI 状态模型、中文文案映射和分层视图组件，允许旧 `BattleHUD` 仅作为临时装配过渡，不把其测试专用入口列为兼容目标。
2. 将 `BattlePlayerController` 的目标/开火事件和 `BattleSceneManager` 的回合状态事件接入新控制器，逐步把旧 `Render`/`RenderAttack` 调用迁移为快照渲染。
3. 在 `Map1` 复制并重排现有 UI 引用，建立只读 Canvas、交互 Canvas 与结束回合确认弹窗；确认血条 Canvas 引用不变。
4. 先迁移 EditMode/PlayMode 测试：用快照、控制器和视图的行为断言替换旧 `BattleHUD` 控件入口测试，删除只验证非必要旧接口的测试，再删除旧的重复渲染和事件绑定，运行完整回归。
5. 若回退，恢复 `BattleHUD` 的序列化引用和旧 Canvas 层级，移除新控制器入口；不回退 `UnitHealthBarCanvas` 或既有远程攻击/血条规格。
