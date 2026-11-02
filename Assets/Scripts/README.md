# 棋盘系统接入

## 场景配置

1. 创建一个空物体，添加 `Wargame.GridSystem.GridManager` 组件。
2. 将物体放在棋盘最小 X/Z 角落，保持世界旋转为零，自身及父级缩放均为 1。
3. 在 Inspector 设置宽、高和格子尺寸，默认是 10 × 10、每格 1 单位。
4. 打开 Scene 视图的 Gizmos。黄色表示半障碍、红色表示全障碍；运行后的蓝色球体表示单位占用。

棋盘不创建格子物体，也不生成地面模型。编辑模式显示场景草稿，运行模式显示实际棋盘。

## 编辑与烘焙地图

1. 选中挂有 `GridManager` 的场景物体，在 Inspector 调整棋盘宽、高和格子尺寸。尺寸输入提交后保留重叠区域，新格默认代价为 1、无障碍；缩小后可通过 Undo 恢复裁掉的格子。
2. 开启“地图编辑”，选择“同时修改”“只修改 MoveCost”或“只修改 BlockState”，设置笔刷值。全障碍的代价固定为 -1，Inspector 自动显示且禁止修改；可通行格的代价至少为 1。
3. 在 Scene 中用鼠标左键点击或拖动绘制。拖动会补齐中间格子，整次笔画可一次撤销；Alt 仍用于场景导航。按需打开“显示移动代价”。
4. 保存场景以保留草稿。草稿编辑不修改已经烘焙的地图资产。
5. 填写新地图名称，点击“烘焙地图数据”。输出自动保存到 `Assets/Data/MapData`，并关联到当前管理器。
6. 之后再次点击烘焙会更新关联资产并保留 GUID；“另存为新地图”始终创建新资产。同名文件会自动避让。若关联地图位于输出目录外，烘焙创建目录内的新副本，不覆盖外部资产。
7. 需要编辑已有地图时，先设置“地图资产”，再点击“从地图资产导入草稿”。导入复制尺寸、移动代价和障碍数据，支持 Undo。

烘焙时，每个格子分别检查北、东、南、西的紧邻格：邻格为 `HalfBlocked` 时生成该侧 `HalfCover`，邻格为 `FullBlocked` 时生成 `FullCover`；无邻格或邻格无障碍时生成 `NoCover`。全障碍格本身也按同一规则计算。此过程不依赖模型或碰撞体，不修改原有移动代价和障碍。

MapData 资产保存全部格子的坐标、代价、障碍和四边掩体，不保存单位引用及棋盘世界位置。资产 Inspector 只读；通过管理器草稿重新烘焙进行修改。已有资产的烘焙更新可撤销/重做，恢复结果也会保存回文件。新建资产在撤销关联后仍保留，可按需在 Project 中删除。

播放模式及播放切换期间禁用编辑、导入和烘焙。Prefab 实例的修改会记录为覆盖；场景和 Prefab 的正常保存仍由使用者完成。

## 运行时初始化

`Awake` 优先从关联的 MapData 复制棋盘；地图的宽、高及格子尺寸作为运行时配置，原点仍取管理器位置。未关联资产时按 Inspector 尺寸生成默认空棋盘，**未烘焙的草稿不用于运行时**。

其他组件可在 `Start` 配置局内格子数据。需要提前初始化时，可显式调用一次 `Initialize(mapData)` 或 `Initialize(width, height, cellSize)`，后续 `Awake` 会跳过。已初始化后不允许再次初始化。运行时修改只影响局内副本，不反写 MapData。

## 数据与搜索

脚本使用 `Wargame.GridSystem` 命名空间；单位组件位于 `Wargame.Units`。

`MapCellData` 是可序列化的地图数据基类，统一定义坐标、移动代价、障碍及四边掩体。`GridCell` 继承这些属性，仅增加单位占用和运行时更新方法，公共字段不重复定义。地图资产对外只读；加载时逐格创建独立的 `GridCell`，草稿导出与资产写入也逐格复制，避免类引用共享。

### 视线查询（LOS）

`GridLineOfSight` 在逻辑格子中心之间执行二维 supercover 查询。遍历器只依赖 `ILineOfSightMap` 的边界与阻挡查询；`GridLineOfSightMapModifier` 将运行时 `GridManager` 适配为该接口，不依赖单位能力、战斗输入或物理碰撞体。地图不可用或任一坐标越界时返回 `false`；端点格不检查阻挡，同格查询在坐标有效时返回 `true`。

```csharp
var lineOfSightMap = new GridLineOfSightMapModifier(grid);
bool visible = GridLineOfSight.HasLineOfSight(lineOfSightMap,
    new Vector2Int(1, 1), new Vector2Int(6, 4));
```

适配器只将 `FullBlocked` 视为阻挡；`HalfBlocked`、单位占用和方向掩体不影响查询。视线恰好穿过格角时会同时检查角点两侧相邻格，任一侧为全障碍都会阻挡。当前规则不计算高低差、部分掩体或单位轮廓；调用方通过接口查询 LOS。

### 远程射击

战斗场景只给玩家单位授予 `ShootAbility`。能力位于 `Units/Abilities/Dic/ShootAbility.cs`，共享规则及只读预览/结果位于 `Battle/ShootInfo.cs`；场景管理、输入与表现分别位于 `Battle/RuntimeData`、`Battle/Input`、`Battle/Visual`。`GetPreview(target)` 返回目标名称、生命、整数距离、最终命中率、可用状态与失败原因，不修改属性；UI 从左侧列表选择目标后，再通过独立的开火按钮请求执行。

目标的距离条件只受攻击者当前 `AttributeType.SightRange` 限制，视线由 LOS 决定，没有独立武器射程。统一逻辑格距为 `floor(sqrt(dx² + dy²))`，同时用于视野比较和曲线采样，例如 `(1,1)` 为 1 格，`(3,4)` 为 5 格；等于 SightRange 时满足距离条件。这不改变移动寻路算法。开火前再次检查存活、非中立敌对阵营、棋盘登记、SightRange、LOS、必要属性、回合和 AP，重新计算概率；准备期间参数改变或随机源异常时拒绝提交，不扣 AP。

射击先检查攻击者当前格到目标当前格的 LOS；被阻挡时，再从攻击者周围上下左右 4 个相邻格尝试射线，任一畅通即可探头射击。对角相邻格不作为候选。越界或 `FullBlocked` 的相邻格不能作为起点；半障碍和单位占用不影响候选。目标端点固定在目标当前格，双方逻辑坐标、占格与 Transform 均不因探头改变，SightRange、命中率和结果距离仍按双方实际格距计算。每条候选继续使用通用 LOS 的 supercover 与格角保守规则；开火时重新检查，所有候选被封锁后拒绝射击且不扣 AP。

`Assets/Data/Game/GameSettings.asset` 是默认配置，`Map1` 的 BattleSceneManager 通过 `gameSettings` 字段显式引用；可用 Create → Wargame → Game Settings 创建其他资产并替换场景引用。配置类位于 `Battle/Data`：GameSettings 是 ScriptableObject，其 `ShootSettings` 字段内嵌 `[Serializable] ShootSettings`，不是另一个 SO。`DistanceHitChanceModifier` 为 AnimationCurve，X 是递增非负整数格距，Y 是命中率修正百分点。Inspector 校验将切线统一为 Constant、首尾保持端点，运行时能力创建独立只读快照，不修改资产；配置调整在下一次初始化生效。缺失或非法配置会明确报告错误，不做隐式资源查找或回退。

默认阶梯为 0–4 格 +20、5–9 格 +10、10–14 格 0、15–19 格 −10、20 格及以上 −20 个百分点，10 格为零修正基准。节点间不插值，到达节点立即采用新值。曲线只修正概率，不是射程上限。最终命中率为 `clamp(Aim - Defense + 距离修正, 0, 100)`；Aim 70、Defense 20 时，5/10/15 格依次为 60%/50%/40%。0% 的可见敌人仍可选择和开火，结算必定未命中。

射击消耗 1 AP；命中后按 `clamp(Dodge, 0, 100)` 判定擦伤，普通伤害为 4、擦伤为 2，最终伤害为 `max(0, 基础伤害 - Armor)` 并从 Health 扣除。Dodge 不从 UI 命中率再次扣减。Health 归零仍由 `Unit.Died` 触发占格移除、回合注销和列表刷新。`ShootResult.Distance` 与 `HitChance` 记录成功提交时的距离、最终概率，不随后续移动或属性变化重算。

```csharp
ShootAbility shoot = unit.Abilities.GetAbility<ShootAbility>();
ShootPreview preview = shoot.GetPreview(enemy); // 只读快照，包含距离、最终命中率和目标剩余生命。
bool fired = unit.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(enemy), out var reason);
if (fired) Debug.Log($"Distance={shoot.LastResult.Distance}, Chance={shoot.LastResult.HitChance}, Hit={shoot.LastResult.Hit}, Damage={shoot.LastResult.Damage}");
```

`Map1` 的 `SafeArea/AttackTargets` 保存目标面板、滚动列表、隐藏的列表项模板、目标生命/命中率、结果文本和 `FIRE / 1 AP` 按钮；通过 HUD 的序列化引用装配，缺少引用时初始化报告错误。操作顺序为“点击己方单位 → 点击左侧敌人 → 查看命中率 → 点击开火”。切换己方单位会清除原目标；SightRange、位置、LOS、属性与死亡变化通过现有事件刷新目标和概率。AP 不足时开火禁用但保留可见列表；目标死亡或离开视野时清除选择，仍合法的目标跨过曲线阶梯则保留选择并更新概率。射击结果显示命中、擦伤或未命中，目标生命随预览刷新。界面标签由 BattleUiText 统一提供简体中文，底层 AbilityId、AP 等技术字段只作为数据格式保留。

```csharp
// 以下代码在 GridManager 初始化后调用，grid 为组件引用。
grid.TrySetBlockState(new Vector2Int(2, 1), BlockState.HalfBlocked);
grid.TrySetMoveCost(new Vector2Int(2, 1), 2);
grid.TrySetCover(new Vector2Int(1, 1), GridDirection.North, CoverType.FullCover);

GridReachabilityResult result = grid.GetReachableCells(new Vector2Int(1, 1), 6);
foreach (var reachable in result.ReachableCosts)
{
    Vector2Int coordinates = reachable.Key;
    int minimumCost = reachable.Value;
    Vector3 center = grid.GridToWorld(coordinates);
    // 使用格子中心和最小代价显示移动范围。
}

if (result.TryGetPath(new Vector2Int(3, 1), out var path))
{
    // path 为包含起点和终点的只读坐标序列，供后续移动系统使用。
}
```

- 逻辑坐标 x/y 对应世界 X/Z；北为 +Z、东为 +X。世界位置转格子时忽略高度，最大 X/Z 边界位于棋盘外。
- 移动支付进入格子的代价，起点不计费。半障碍可以穿过但不能停留或出生，代价至少为 1；全障碍的 `MoveCost` 固定为 -1，寻路不会将其计入路径或累计代价。
- `TrySetBlockState` 设置全障碍时同步把代价设为 -1；解除全障碍后默认恢复为 1，可再通过 `TrySetMoveCost` 调整。该代价接口不改变障碍状态，可通行格只接受正数，全障碍只接受 -1。
- 笔刷只修改代价时会保留全障碍的 -1；只修改障碍时会同步维护上述约定。旧地图和场景草稿读取时自动将全障碍代价统一为 -1，下次保存或烘焙写入文件。
- 四边掩体只表达本格防御效果，不改变通行，也不自动同步邻格。
- 可达集合不包含起点、半障碍和单位占用格。两参数查询保留允许穿过占用格的通用行为；`GetReachableCells(start, budget, mover)` 仅允许穿过同阵营占用格，敌军和其他阵营阻挡。移动力为 0 时集合为空。
- 返回结果为快照，数据变化后应重新搜索。负预算、非法起点和未初始化调用的异常约定见方法 XML 注释。

## 单位占用

为单位物体挂载 `Unit`，由调用方维护其生命周期和棋盘占用：

```csharp
grid.TryPlaceUnit(unit, new Vector2Int(1, 1));
grid.TryMoveUnit(unit, new Vector2Int(3, 1));
grid.TryRemoveUnit(unit);
```

这些方法只修改逻辑引用，不移动物体，也不计算或验证路径。`TryPlaceUnit` 与 `TryMoveUnit` 的目标必须是空闲的 `Unblocked` 格；原地转移视为成功。同一单位不能重复登记到本棋盘多个格子，失败的请求保持原状态。使用 `TryGetUnitCoordinates` 读取逻辑坐标；运行时地形和占用更新会触发 `Changed`。

正常销毁或移出单位前调用 `TryRemoveUnit`。若物体已经销毁，其 Unity 空引用会按未占用处理。单位跨棋盘转移由调用方协调。

## 单位能力系统（简化 GAS）

`Unit` 通过 `RequireComponent` 自动挂载 `TagComponent`、`CharacterAttribute`、`BuffComponent`、`AbilityComponent`，并以 `unit.Tags / Attributes / Buffs / Abilities` 暴露。组件均为懒初始化，Awake 前访问也可用。

- **标记**：`GameplayTag` 按用途分段显式赋值，新增时只追加。`TagComponent` 带引用计数，多个来源授予同一标记时，最后一个来源移除才清除。
- **属性**：`Current = Override ?? (Base + ΣAdd) × max(0, 1 + Σ(Mul - 1))`。伤害、消耗改基础值（`ApplyBaseDelta`），Buff 挂修饰器。Health、ActionPoints 钳制在 [0, 对应上限]。
- **Buff**：继承 `BuffBase`，在构造函数中配置授予标记、阻挡标记和修饰，按需重写 `protected` 钩子。`StackPolicy` 决定同 BuffId 的处理方式（Unique / Refresh / Replace / Stack / Independent）。持续时间在承受者回合结束时减一；在承受者回合内施加的 Buff 跳过当回合结算。
- **能力**：继承 `AbilityBase`，实现 `OnActivate`，效果结束后调用 `EndAbility`。激活流程为 CanActivate → Commit（扣行动点、次数、进入冷却）→ OnActivate。失败原因见 `AbilityFailReason`。冷却在持有者回合开始时减一。
- **回合**：`Unit.BeginTurn` 依次执行行动点回满、Buff 回合开始钩子、冷却递减；`EndTurn` 结算 Buff 倒计时。`Wargame.Battle.BattleTurnManager` 目前只负责注册单位和按阵营轮转，胜负判定与 AI 尚未实现。

```csharp
sealed class BurningBuff : BuffBase
{
    public BurningBuff() : base("Burning", 2, BuffStackPolicy.Refresh)
    {
        AddGrantedTag(GameplayTag.Burning);
        AddBlockedByTag(GameplayTag.ImmuneFire);
    }

    protected override void OnOwnerTurnStarted() =>
        Owner.Attributes.ApplyBaseDelta(AttributeType.Health, -2f);
}

unit.Buffs.TryApply(new BurningBuff(), attacker);
```

## 验证

Unity Test Runner 的 EditMode 中运行 `Wargame.EditModeTests` 程序集，覆盖坐标、障碍、占用、加权范围、路径快照、草稿保存重载、笔刷与撤销、Prefab 覆盖、掩体烘焙、资产 GUID、资产撤销持久化及运行时副本隔离；`Tests/EditMode/Units` 覆盖标记计数、属性公式与钳制、Buff 叠加策略与持续时间、能力激活流程、单位回合钩子及回合管理器轮转。

已打开项目时，通过 Unity CLI 连接现有 Editor 执行测试：`unity command "test run" --mode editor --filter Wargame.EditModeTests --filter_type assembly --timeout 60`。先用 `unity command "editor status"` 确认 `playMode` 为 `stopped` 且没有编译或域重载，再启动测试；不要在游戏播放过程中直接发起 EditMode 测试。`unity test` 或 `Unity.exe -batchmode -runTests` 会尝试启动另一个 Editor，不能与已打开同一项目的实例共用项目锁。

## 战斗场景与拖动移动

打开 `Assets/Scenes/Map1.unity` 即可运行。`GameManager` 上的 `BattleSceneManager` 在 Start 校验出生列表，动态生成 `Assets/Prefabs/Units/Player.prefab` 和 `Enemy.prefab`。默认玩家 `(1,1)`，敌人 `(18,18)`；在 Inspector 修改 Prefab、Coordinates、Yaw 或增加列表项。出生格不能重复、越界、被占用或属于 HalfBlocked / FullBlocked。Prefab 根节点需有 Unit、启用的 UnitVisualController，且有可拾取 Collider。

PC 仅使用鼠标左键，Android 使用主触点：从玩家单位按下，拖到蓝色或黄色空格，松开执行；单击只选中。拖到红色取消区域、其他 UI、棋盘外或非法格子松开均取消，不扣 AP。额外触点不接管手势，捕获手指抬起即结束，失焦或系统取消也会清除预览。

蓝色范围是 1AP，黄色范围是 2AP。路径按格子进入代价累计，每 AP 预算为 `floor(max(0, Mobility))`；默认 6，即代价 1～6 消耗 1AP，7～12 消耗 2AP。剩余 1AP 时只有蓝色范围。半障碍可作为路径中间格，友军可穿过，敌军不可穿过。预览只读，松开后重新验证才提交，不能使用旧预览绕过当前障碍、占用或 AP。

玩家的 `MoveAbility` 由场景管理器自动授予；可通过 `TryGetMovePlan(cell, out plan, out reason)` 查询只读路径、代价和 AP 费用，再通过 `unit.Abilities.TryActivate("Move", AbilityTarget.ForCell(cell), out reason)` 执行。目标相关费用通过 `GetActionPointCost` 查询，`TryCommit` 允许自定义能力拒绝提交，固定费用能力继续使用原有 Commit 钩子。

逻辑占用和 AP 先提交，`UnitVisualController` 再沿路径插值。它仅处理平移、模型转向、起伏及选中圈，模型引用、每秒格数（默认 4）、转向时间和起伏幅度均可配置。表现结束前持有独占能力和 Moving 标记，期间不能再发移动或结束回合。已提交移动被禁用、取消时对齐终点，不返还 AP；单位死亡或销毁会释放占用和回合注册。

点击“结束回合”结束玩家回合；敌方目前只占格、不行动，下帧直接返回玩家并补满 AP。AP 耗尽不会自动切回合。HUD 的状态、提示、取消区域、旋转和结束按钮使用 uGUI，按安全区域布局。

### 战斗 UI MVC

战斗 UI 的数据流为 `BattleSceneManager/AbilityComponent` → `BattleUiController` → `BattleUiState` 快照 → `BattleHUD` 视图。`BattleUiState`、`SelectedUnitUiState`、`AbilityUiState` 和 `TargetUiState` 只保存回合、行动方、选中单位、AP、能力可用性、目标预览和中文原因，不持有 `Text`、`Button` 或其他 Unity 控件；能力列表通过 `BattleUiAbilityAdapter` 读取 `AbilityComponent.Abilities` 的实时结果，不复制能力规则。

`Map1` 使用名为 `canvas` 的根物体收纳三个 Canvas：`BattleInfoCanvas` 是只读信息层，`BattleInteractionCanvas` 是交互层，`UnitHealthBarCanvas` 是血条层。交互层保留目标列表、能力按钮、开火、旋转、结束回合和确认弹窗；信息层显示独立的“回合数”和“行动方”文本、提示、选中单位 AP 与能力状态。只读 Canvas 不挂 `GraphicRaycaster`，其文字关闭 `raycastTarget`。布局、静态文本、确认弹窗及按钮均保存在场景并由 `BattleHUD` 的 Inspector 引用配置；运行时只按战斗数据更新文本/显隐并生成动态目标和能力条目。两个战斗 Canvas 共用 `CanvasScaler` 的 1280×720、Match 0.5 安全区域配置。

结束回合按钮先查询 `BattleSceneManager.HasUnspentPlayerActionPoints`，范围是当前玩家阵营的存活单位；任一单位 AP 大于零时显示中文确认弹窗。确认时重新调用原子 `TryEndPlayerTurn`，取消或过期确认只关闭弹窗并按最新快照刷新，弹窗期间玩家控制器会取消移动/相机手势。

### Cinemachine 战术相机

Map1 使用 Cinemachine 3.1.7：MainCamera 保留渲染、URP 与 AudioListener，CinemachineBrain 采用 ManualUpdate；`BattleCameraRig` 仅维护中心位置、边界、拖动和旋转状态，不覆盖场景中配置的镜头、跟随和角度。初始中心是首个玩家的出生格；横竖屏切换保持 FOV、高度与俯角，不再自动容纳全图。

静态配置直接在 Inspector 编辑：

- MainCamera / CinemachineBrain：Update Method 为 ManualUpdate，Ignore Time Scale 开启，Default Blend 为 Cut，Lens Mode Override 开启。
- BattleVirtualCamera / CinemachineCamera：Lens 为 Perspective，FOV 默认 45°，裁剪面按地图设置；Tracking Target 引用 BattleCameraPitch。
- BattleViewCenter / Transform：Y 旋转控制初始朝向（默认 45°），X/Z 旋转为零，缩放为 1。
- BattleCameraPitch / Transform：局部位置归零，X 旋转控制固定俯角（默认 55°），Y/Z 旋转为零。
- BattleVirtualCamera / CinemachineFollow：Binding Mode 为 LockToTarget，Angular Damping Mode 为 Quaternion，阻尼为零。Follow Offset 仅使用负 Z；默认 -14.649295 世界单位，对应当前每格 1 单位时高度 12 格。调整高度或格子尺寸时，按 `Offset Z = -高度格数 × 格子尺寸 / sin(俯角)` 设置。
- BattleVirtualCamera / CinemachineRotateWithFollowTarget：Damping 为零。
- BattleCameraRig：引用 Brain、虚拟相机和中心节点，仅保留旋转时长、边缘带宽度和滚屏速度等运行参数。无效装配会报告错误，不会自动改写 Inspector 设置。

旋转按钮在 Inspector 的 On Click 中分别绑定 `BattleCameraRig.Rotate(-1)` 与 `Rotate(1)`；位置、尺寸、锚点和提示面板布局均使用 RectTransform 配置，HUD 不再在运行时覆盖这些布局，安全区域仍按设备更新。

从非 UI 背景按下左键或主触点，超过 10 像素后抓住地图拖动；从可控制玩家按下始终使用移动手势。背景拖动保留单位选择，经过 UI 时暂停并在离开时重新建立锚点，不产生跳变。相机可在单位表现期间平移浏览；拖动相机不花费 AP。

移动手势进入屏幕边缘时持续滚屏：默认边缘宽度为屏幕短边的 6%，最高每秒 8 格，速度随接近边缘增加；方向根据当前相机朝向转换，斜向不加速。指针不动时仍继续滚屏和更新目标格；松开、取消、失焦、进入 UI 或屏幕外立即停止。空闲悬停不触发。

`< 90` / `90 >` 按钮绕当前相机中心射线与棋盘平面的交点旋转 −90° / +90°，耗时 0.3 秒，固定高度、俯角和 FOV；平移仅限制该中心在棋盘内，画面边缘允许显示棋盘外。中心竖直轴只通过 Scene Gizmos 显示。棋盘手势期间禁用旋转，旋转期间禁用新手势与结束回合。尚不包含缩放、自由俯仰、惯性或角色自动跟随。

输入仅记录手势与释放请求。CameraRig 在 LateUpdate 先平移/滚屏并 ManualUpdate Cinemachine，玩家控制器随后用本帧实际相机刷新拾取和提交；目标格不变时复用路径，相机变化不会重新构建移动范围。外部调用可以读取 `ViewCenter`、`IsRotating`、监听 `ViewChanged`，并通过 `TryRotate(-1/1)` 请求旋转。

`InputSystem_Actions` 中新增 Battle/Point 与 Battle/Press，不启用模板 Player 动作来控制战斗。`BattleInputReader` 封装设备与手势，`BattlePlayerController` 负责选择、平面拾取和能力请求；UI 使用 InputSystemUIInputModule，仅接入 Point / Click。替换模型时保留 Prefab 根节点和逻辑组件，将模型子节点重新赋给 UnitVisualController。

PlayMode 流程测试：`unity command "test run" --mode playmode --filter Wargame.PlayModeTests --filter_type assembly --async_tests`，启动后用 `unity command "test status"` 查询完成状态和结果。进入 PlayMode 会触发域重载，因此当前 Pipeline 版本不支持在同一个 HTTP 请求中同步等待 PlayMode 测试。测试覆盖 Prefab 出生、鼠标和触屏拖动、UI 取消、失焦、多指、表现中断、回合循环及射击目标选择与开火。

使用 `--json` 时，命令外层的 `success` 只代表命令请求成功；仍需检查内层测试结果的 `success`、`Summary` 或异步状态文件的完成结果，避免将测试启动失败或超时误判为通过。

### 单位浮动血条

`Assets/Prefabs/UI/UnitHealthBar.prefab` 使用固定基础尺寸的 `UnitHealthBarView`，由 Background、RedFill 和 Dividers 三个子节点组成。Dividers 用一个带 CanvasRenderer 的 `UnitHealthBarDividers` Graphic 绘制全部等分线，不为每格创建物体。RedFill 使用同目录的纯白像素 `HealthBarWhite.png` 作为 Sprite，并通过 Image 颜色显示矩形红色填充。预制体没有 Canvas，全部 Graphic 关闭 raycastTarget。

`UnitHealthBarDividers` 在水平 Overlay 血条上按最终屏幕像素绘制：把经过 CanvasScaler 和距离缩放的线宽统一取整为至少 1 像素，再对齐线条边缘。渲染前检测自身及父级变换，位置或缩放变化时重建刻度网格，避免亚像素导致同一血条内线条粗细不一；分格的逻辑位置保持等分，最终显示位置最多偏移半个像素。

底层 Health、MaxHealth 保留 float；显示总格数 `N = ceil(MaxHealth)`，红格数 `K = ceil(clamp(Health / MaxHealth, 0, 1) * N)`，红色宽度为 `K / N`。只显示完整格子，不做宽度插值：`7.5/10` 显示 8 格，`7/10` 显示 7 格，任何正生命值至少显示 1 格。小数最大生命值先按比例换算，例如 `2/7.5` 显示 8 格中的 3 格。无效最大生命值隐藏血条；显示取整不会回写生命属性、伤害或死亡状态。

`UnitHealthBarSettings` 配置 `BaseSize`（Canvas 逻辑尺寸，默认 96×10）、`WorldOffset`（世界竖直偏移，默认 2）、`NearDistance/FarDistance`（默认 10/30）和 `NearScale/FarScale`（默认 1.1/0.65）。距离缩放按 `lerp(NearScale, FarScale, clamp01((distance-NearDistance)/(FarDistance-NearDistance)))` 计算。尺寸必须为正，偏移非负，距离阈值和比例均有限并满足近距离小于远距离、近端比例大于远端比例且远端比例大于零。

Map1 的 `UnitHealthBarCanvas` 位于 `canvas` 根物体下，使用 Screen Space Overlay、sortingOrder -2；只读信息层排序为 -1、交互 HUD 为 0。CanvasScaler 与 HUD 一致（Scale With Screen Size、1280×720、Match Width Or Height 0.5）。该节点的 `BattleUnitHealthBars.healthCanvas` 引用自身 Canvas，`viewPrefab` 引用上述预制体，GameManager 的 `BattleSceneManager.healthBars` 显式引用此组件。血条 Canvas 不挂 GraphicRaycaster，不创建额外 EventSystem。

场景在生成单位前验证血条装配，初始化相机后调用 `healthBars.Initialize(this, cameraRig.OutputCamera)`。血条层按 `SpawnedUnits` 中非空且存活的单位维护唯一视图，包含双方和未选中单位；回合、AP 和能力可用性不影响资格。`scene.Changed` 协调单位集合，单位的 Health/MaxHealth 事件只标记待刷新，在呈现前读取最终快照。死亡或销毁立即解绑并隐藏对应视图；禁用血条层会释放全部绑定，重新启用从当前存活集合重建。重复 Initialize/Refresh 不重复建条。战斗清理和初始化异常先 Shutdown 血条层，再关闭相机、解除单位订阅并销毁单位，避免旧绑定残留。

`BattleUnitHealthBars` 使用执行顺序 +100 的 LateUpdate，晚于执行顺序 -100 的 `BattleCameraRig` 手动更新 Cinemachine；每帧投影 `unit.transform.position + Vector3.up * WorldOffset`，跟随移动动画的实际位置。Overlay 坐标通过 RectTransformUtility 转换，不直接将屏幕像素当作 anchoredPosition。血条始终屏幕水平；锚点在视口或相机裁剪范围外时隐藏，返回后显示最新生命值，不吸附屏幕边缘，也不检查墙体或射击 LOS。基础尺寸控制统一长度，Near/Far 参数控制近大远小的幅度，WorldOffset 用于替换模型后调整头顶高度。
