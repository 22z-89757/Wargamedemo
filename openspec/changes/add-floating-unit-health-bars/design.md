# 设计：单位浮动血条

## 背景

动机见 `proposal.md`，行为契约见 `specs/unit-health-bars/spec.md`。当前架构对方案有以下约束：

- `CharacterAttribute` 的生命属性为 float，提供 `AttributeChanged`，最大生命值变化可能连带约束当前生命值；不能假设生命值一定为整数。
- `BattleSceneManager` 负责生成双方单位、持有 `SpawnedUnits` 并发送 `Changed`；死亡对象仍可能留在列表中。其初始化会显式装配相机与 HUD，清理时解除单位绑定。
- `UnitVisualController` 用 DOTween 移动单位根 Transform，逻辑格可能先于动画到达终点。
- `BattleCameraRig` 的执行顺序为 -100，在 `LateUpdate` 手动更新 Cinemachine，并暴露 `OutputCamera`。
- `BattleUICanvas` 使用 Screen Space Overlay 和 1280×720 参考分辨率；输入系统将任意 `GraphicRaycaster` 命中视作 UI 阻挡。

## 目标与非目标

目标：使生命属性、单位生存状态和镜头投影各自提供数据，由独立表现层组合显示；保持既有战斗输入、模型移动及生命计算流程。

非目标：伤害飘字、数值文本、阵营配色、受击插值动画、遮挡淡出、战争迷雾，以及通用动态刷兵框架。本次不使用射击 LOS 控制血条，也不为每个单位创建 Canvas。

## 决策

### 1. 一个独立 Overlay Canvas 与可复用血条视图

在 `Map1.unity` 新增根级 `UnitHealthBarCanvas`，与 `BattleUICanvas` 分离，排序设为 -1，使现有排序为 0 的 HUD 始终覆盖血条。CanvasScaler 与 HUD 使用相同参考分辨率及匹配方式，以 Canvas 逻辑单位定义基础长度。

新增血条层组件（建议 `BattleUnitHealthBars`）管理单位到视图的映射；每个单位使用可复用 `UnitHealthBarView` 预制体。视图由深色背景、左侧起填的红色 Image、最上层分隔线构成；不包含嵌套 Canvas。分隔线使用单个自定义 uGUI Graphic 生成，避免每一点生命都增加 GameObject；仅在分格数、尺寸或样式变化时更新几何。

替代方案：每单位 World Space Canvas 能自然产生透视大小，但需额外朝向控制和尺度约束；放进既有 HUD 则不满足独立 Canvas 要求。集中 Overlay 层便于保证屏幕水平、HUD 排序和受限缩放。

### 2. 由场景入口显式装配及释放

`BattleSceneManager` 增加序列化血条层引用。在生成任何单位前验证独立 Canvas、预制体及尺寸/距离配置；配置错误沿用现有初始化失败路径，给出明确错误且不生成半初始化战斗。测试装配相应补齐引用。

在相机初始化后、战斗启动前初始化血条层，注入场景实例及 `cameraRig.OutputCamera`。以 `SpawnedUnits` 中非空且 `IsAlive` 的单位建立映射；监听 `Changed` 协调增删，过滤死亡留存项及 Unity 已销毁引用。重复初始化和刷新保持幂等，不以回合或 AP 判断血条资格。现有项目没有动态刷兵入口，本次不新增该系统。

每个绑定订阅自身单位的生命属性事件；死亡/销毁及集合刷新可触发移除，解绑必须可重复调用。场景 `Cleanup` 先关闭血条层并解除场景与单位订阅，再销毁单位和关闭相机；初始化异常及组件销毁也执行同一清理。禁用层时暂停并释放绑定，恢复启用时若战斗仍有效则按当前集合重建，避免漏接禁用期间变化。

替代方案：由 Unit 持有 UI 引用会将战斗属性耦合到具体 UI；全局查找单位也绕开了现有场景所有权，因此采用场景显式注入。

### 3. 使用最终属性快照更新填充与分格

分格数为 `ceil(MaxHealth)`，内部线位置为 `i / count`（`1 <= i < count`）。基础宽度不取决于分格数；整数生命值按一点一格，非整数最大生命值按向上取整等分，格数仅作视觉刻度，填充仍使用真实浮点比例。

属性事件仅标记对应视图数据待刷新，在本帧呈现前读取最新的 Health 与 MaxHealth 快照，避免最大生命值连带更新时使用过渡状态。Health 单独变化只更新红色比例；MaxHealth 改变时按需更新分格和比例。对无效 MaxHealth 隐藏视图，恢复有效值后恢复，不回写游戏属性。

替代方案：逐格点亮会丢失小数伤害信息；总宽度随生命容量增长则偏离固定长度要求。基础宽度、高度、竖直偏移、背景和分隔线样式保留 Inspector 编辑入口，不加入文本依赖。

### 4. 在相机更新之后逐帧投影

血条层使用晚于相机的 `LateUpdate`（建议执行顺序 +100）。头顶锚点为 `unit.transform.position + Vector3.up * worldOffset`，不读取逻辑格目标，也不跟随子模型的装饰性跳动。用当前输出摄像机投影，并通过 `RectTransformUtility.ScreenPointToLocalPointInRectangle` 转为 Overlay 根 RectTransform 坐标，传入空 UI 摄像机。

每帧刷新位置、可见性和缩放；不能只监听 `ViewChanged`，因为单位动画可在镜头静止时继续运行。视图中心对齐锚点并保持屏幕水平。投影深度处于相机 near/far clip 之间且视口坐标处于 [0,1] 才显示，不做边缘钳位或墙体遮挡检测；缺失有效相机时隐藏，避免错误投影。

替代方案：直接使用屏幕像素设置 anchoredPosition 会在 CanvasScaler 或窗口尺寸变化时错位；按逻辑格投影则会提前跳到移动终点。

### 5. 距离缩放采用有界线性插值

计算摄像机与锚点的欧氏距离 `d`：

```text
t = clamp01((d - nearDistance) / (farDistance - nearDistance))
scale = lerp(nearScale, farScale, t)
```

同一比例作用于血条整体（含背景和分隔线）。所有配置有限，基础尺寸为正、偏移非负，距离和比例满足规格的顺序约束；初始化拒绝非法配置。具体数值在 Map1 近中远视角中调整，验收依赖公式和可读性，不把任意美术数值写入规格。

替代方案：无界的反距离会使近处过大、远处不可读；限定两端并连续插值便于调试，且无需修改战斗摄像机。

### 6. 通过 UI 配置保证输入穿透

新 Canvas 不挂 `GraphicRaycaster`，所有血条 Graphic 的 `raycastTarget` 设为 false，不增加 EventSystem。配置验证应能识别会拦截输入的错误装配；不修改 `BattleInputReader.IsOverUI` 的既有判定。用实际鼠标/触摸测试证明血条位置没有 UI 命中，现有 HUD 仍能阻挡棋盘输入。

替代方案：在输入代码中排除特定血条名称会增加跨层耦合，且更难保证所有手势路径一致。

## 风险与取舍

- 属性事件可能成批触发且死亡项仍留在场景列表 → 以有效存活集合协调视图，以当前属性快照呈现，解绑幂等。
- 相机手动更新与 Canvas 缩放导致位置延迟或偏移 → 明确执行顺序并在移动、旋转、不同分辨率下验证同帧投影。
- 较高 MaxHealth 在固定宽度下刻度密集 → 分隔线使用单个 Graphic，避免逐格对象；当前默认 10 点生命值进行视觉验收，极端容量的可读性仍受显示分辨率限制。
- Overlay 会显示墙后血条 → 这是本次默认规则；不引入 LOS、物理射线或新的敌人可见性系统。
- `Map1.unity`、现有测试与说明文件已有其他修改 → 实施只增量装配本功能，保留既有 LOS 和用户编辑。

## 接入与验证

1. 新增视图、血条层及预制体，增量装配 Map1 的独立 Canvas 和场景引用；更新脚本说明。
2. EditMode 覆盖分格、浮点比例、缩放边界和非法配置；PlayMode 覆盖双方单位、生命变化、死亡/销毁、重复刷新、场景重建、实际移动跟随、镜头变化及输入穿透。
3. 通过 Unity CLI 显式触发脚本重编译并确认完成，再运行现有 EditMode 和 PlayMode 程序集，确认新增用例被发现，保留既有战斗流程回归验证。
4. 在 Map1 运行态检查近远尺寸、镜头旋转、移动中头顶位置、HUD 重叠和至少两种分辨率并留存截图。若需回退，仅撤销本变更新增的 UI 装配、脚本及引用，保留其他工作区修改。
