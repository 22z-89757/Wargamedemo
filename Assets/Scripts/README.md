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
5. 填写新地图名称，点击“烘焙地图数据”。输出自动保存到 `Assets/MapData`，并关联到当前管理器。
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
- 移动支付进入格子的代价，起点不计费。半障碍可以通行，代价至少为 1；全障碍的 `MoveCost` 固定为 -1，寻路不会将其计入路径或累计代价。
- `TrySetBlockState` 设置全障碍时同步把代价设为 -1；解除全障碍后默认恢复为 1，可再通过 `TrySetMoveCost` 调整。该代价接口不改变障碍状态，可通行格只接受正数，全障碍只接受 -1。
- 笔刷只修改代价时会保留全障碍的 -1；只修改障碍时会同步维护上述约定。旧地图和场景草稿读取时自动将全障碍代价统一为 -1，下次保存或烘焙写入文件。
- 四边掩体只表达本格防御效果，不改变通行，也不自动同步邻格。
- 可达集合不包含起点和单位占用格，但路径可以穿过单位占用格。移动力为 0 时集合为空。
- 返回结果为快照，数据变化后应重新搜索。负预算、非法起点和未初始化调用的异常约定见方法 XML 注释。

## 单位占用

为单位物体挂载 `Unit`，由调用方维护其生命周期和棋盘占用：

```csharp
grid.TryPlaceUnit(unit, new Vector2Int(1, 1));
grid.TryMoveUnit(unit, new Vector2Int(3, 1));
grid.TryRemoveUnit(unit);
```

这些方法只修改逻辑引用，不移动物体，也不计算或验证路径。`TryMoveUnit` 要求单位已经位于本棋盘，目标为空且可通行；原地转移视为成功。同一单位不能重复登记到本棋盘多个格子，失败的请求保持原状态。

正常销毁或移出单位前调用 `TryRemoveUnit`。若物体已经销毁，其 Unity 空引用会按未占用处理。单位跨棋盘转移由调用方协调。

## 验证

Unity Test Runner 的 EditMode 中运行 `Wargame.EditModeTests` 程序集，覆盖坐标、障碍、占用、加权范围、路径快照、草稿保存重载、笔刷与撤销、Prefab 覆盖、掩体烘焙、资产 GUID、资产撤销持久化及运行时副本隔离。
