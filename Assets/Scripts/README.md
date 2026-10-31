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

`GridLineOfSight` 在逻辑格子中心之间执行二维 supercover 查询。遍历器只依赖 `ILineOfSightMap` 的边界与阻挡查询；`GridLineOfSightMap` 将运行时 `GridManager` 适配为该接口，不依赖单位能力、战斗输入或物理碰撞体。地图不可用或任一坐标越界时返回 `false`；端点格不检查阻挡，同格查询在坐标有效时返回 `true`。

```csharp
var lineOfSightMap = new GridLineOfSightMap(grid);
bool visible = GridLineOfSight.HasLineOfSight(lineOfSightMap,
    new Vector2Int(1, 1), new Vector2Int(6, 4));
```

适配器只将 `FullBlocked` 视为阻挡；`HalfBlocked`、单位占用和方向掩体不影响查询。视线恰好穿过格角时会同时检查角点两侧相邻格，任一侧为全障碍都会阻挡。当前规则不计算高低差、部分掩体或单位轮廓，也不自动接入攻击或侦测系统。

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

也可以通过 Unity CLI 对运行中的编辑器执行测试：`unity command "test run" --mode editor --filter Wargame.EditModeTests --filter_type assembly`。

## 战斗场景与拖动移动

打开 `Assets/Scenes/Map1.unity` 即可运行。`GameManager` 上的 `BattleSceneManager` 在 Start 校验出生列表，动态生成 `Assets/Prefabs/Units/Player.prefab` 和 `Enemy.prefab`。默认玩家 `(1,1)`，敌人 `(18,18)`；在 Inspector 修改 Prefab、Coordinates、Yaw 或增加列表项。出生格不能重复、越界、被占用或属于 HalfBlocked / FullBlocked。Prefab 根节点需有 Unit、启用的 UnitVisualController，且有可拾取 Collider。

PC 仅使用鼠标左键，Android 使用主触点：从玩家单位按下，拖到蓝色或黄色空格，松开执行；单击只选中。拖到红色取消区域、其他 UI、棋盘外或非法格子松开均取消，不扣 AP。额外触点不接管手势，捕获手指抬起即结束，失焦或系统取消也会清除预览。

蓝色范围是 1AP，黄色范围是 2AP。路径按格子进入代价累计，每 AP 预算为 `floor(max(0, Mobility))`；默认 6，即代价 1～6 消耗 1AP，7～12 消耗 2AP。剩余 1AP 时只有蓝色范围。半障碍可作为路径中间格，友军可穿过，敌军不可穿过。预览只读，松开后重新验证才提交，不能使用旧预览绕过当前障碍、占用或 AP。

玩家的 `MoveAbility` 由场景管理器自动授予；可通过 `TryGetMovePlan(cell, out plan, out reason)` 查询只读路径、代价和 AP 费用，再通过 `unit.Abilities.TryActivate("Move", AbilityTarget.ForCell(cell), out reason)` 执行。目标相关费用通过 `GetActionPointCost` 查询，`TryCommit` 允许自定义能力拒绝提交，固定费用能力继续使用原有 Commit 钩子。

逻辑占用和 AP 先提交，`UnitVisualController` 再沿路径插值。它仅处理平移、模型转向、起伏及选中圈，模型引用、每秒格数（默认 4）、转向时间和起伏幅度均可配置。表现结束前持有独占能力和 Moving 标记，期间不能再发移动或结束回合。已提交移动被禁用、取消时对齐终点，不返还 AP；单位死亡或销毁会释放占用和回合注册。

点击 `END TURN` 结束玩家回合；敌方目前只占格、不行动，下帧直接返回玩家并补满 AP。AP 耗尽不会自动切回合。HUD 的状态、提示、取消区域、旋转和结束按钮使用 uGUI，按安全区域布局。

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

PlayMode 流程测试：`unity command "test run" --mode playmode --filter Wargame.PlayModeTests --filter_type assembly`，覆盖 Prefab 出生、鼠标和触屏拖动、UI 取消、失焦、多指、表现中断及回合循环。
