# Design

## Context

参见 `proposal.md` 的动机。当前 `BattleCameraDirector.ShowResult` 同时创建 `BattleShotResultPopup`、保存其引用、在 `EndShot` 中取消跳字，并依赖跳字完成回调恢复攻击者中心战术镜头；`BattleShotResultPopup` 则在运行时创建旧 `TextMesh` 并用代码写死字号、颜色、排序和上浮参数。Map1 已由 `BattleSceneManager` 统一装配相机、HUD 和血条，但没有独立跳字服务。项目使用 Unity 6 的 `com.unity.ugui` 2.0.0，尚未发现项目内 TMP 字体资产或 TMP Prefab；运行时与测试 asmdef 也尚未引用 `Unity.TextMeshPro`。

既有 `combat-camera-presentation` 主规格已经约束目标上方世界空间结果、至少 2 秒停留、对象失效清理和完成后镜头恢复。本设计仅改变实现与资源装配，不新增 delta spec。

## Goals / Non-Goals

**Goals:**

- 让伤害跳字的字体、材质、字号、颜色、对齐、排序、持续时间和位移动画都可在 Prefab Inspector 中调整。
- 由一个战斗级管理器统一创建、跟踪和清理跳字实例，并为射击流程提供一次性完成通知。
- 让 `BattleCameraDirector` 只管理 Cinemachine 状态，不持有、创建、取消任何文字对象。
- 保持致死目标使用位置快照、跳字面向输出相机、使用 unscaled time、至少显示 2 秒以及完成后恢复攻击者中心战术镜头。

**Non-Goals:**

- 不引入对象池、批处理渲染、伤害数字合并、堆叠避让或多段伤害队列。
- 不改变 `ShootResult`、命中判定、伤害应用时机、中文结果文案或 HUD 的其他职责。
- 不改用 Screen Space Overlay，也不保证文字穿透场景遮挡；渲染深度行为继续由 TMP 材质和 Prefab 配置决定。

## Decisions

1. **使用 3D `TextMeshPro` 组件承载世界空间文字。** 新 Prefab `Assets/Prefabs/UI/BattleDamagePopup.prefab` 以普通 Transform 为根，挂载独立视图组件和 `TextMeshPro`，不增加 Canvas。相比 World Space Canvas + `TextMeshProUGUI`，3D TMP 更接近当前 `TextMesh` 的世界坐标与 MeshRenderer 行为，层级更少；相比继续运行时 `AddComponent`，Prefab 能完整保存字体、材质、对齐和渲染设置。运行时和 PlayMode 测试 asmdef 显式引用 `Unity.TextMeshPro`。

2. **Prefab 是全部视觉参数的来源。** 视图组件序列化 TMP 引用、命中/擦伤/未命中颜色、世界偏移、上浮距离、上浮时长和保持时长；保持时长校验为至少 2 秒。管理器只传入 `ShootResult` 的结果类型、格式化文本、位置快照、输出相机和完成回调，不在代码中覆盖字号、字体、材质或静态布局。项目若无可用 TMP 字体资产，则在项目内创建或导入一个明确引用的 `TMP_FontAsset`，避免依赖运行时默认资源。

3. **拆分为 `BattleDamagePopupManager` 与单实例视图。** 管理器作为 Map1 中显式装配的 MonoBehaviour，持有 Popup Prefab、初始化后的输出相机和活动视图集合。`ShowShotResult` 实例化一个 Prefab，登记实例并启动播放；实例完成后通知管理器，管理器从集合移除并销毁对象。`Shutdown`/禁用路径取消并销毁全部活动实例，且取消清理不触发射击完成回调。暂不抽象工厂或池接口，避免为尚未需要的复用机制增加状态分支。

4. **射击流程控制器协调跳字完成与镜头恢复。** `BattlePlayerController.OnSelectedAbilityEnded` 继续更新 HUD，然后调用管理器展示结果；管理器完成回调中再调用 `BattleCameraDirector.EndShot(true)`。若管理器缺失、配置无效或拒绝展示，控制器必须立即结束射击镜头，避免输入和镜头永久锁定。这样相机协调器不再包含 `BattleShotResultPopup`、输出相机或文字生命周期引用，但“跳字显示满 2 秒后恢复镜头”的既有时序仍由控制层明确串联。

5. **场景装配和生命周期由 `BattleSceneManager` 统一管理。** Map1 的 GameManager 显式引用 `BattleDamagePopupManager`；初始化时在相机就绪后传入 `cameraRig.OutputCamera`，并校验管理器启用、Prefab 启用、视图/TMP/字体引用有效及持续时间合法。清理顺序先停止玩家输入与结果回调，再 Shutdown 跳字管理器，最后释放相机，防止销毁过程中的回调访问已释放镜头。不会恢复已删除的 `BattleVisualAssetSetup`，也不使用运行时查找补装资源。

6. **保留快照与并发安全。** 跳字始终使用 `ShootResult.TargetPosition`，不依赖可能已经销毁的目标 Transform。管理器用活动集合而非单一字段跟踪实例，因此未来即使出现多个来源也能独立完成和清理；当前独占射击流程仍只创建一个结果实例。每个实例保证完成或取消只结算一次，重复 Shutdown/Cancel 幂等。

7. **测试从具体旧组件迁移到职责与资源验证。** 现有测试不再断言 `TextMesh` 字号，而应验证 Prefab 实例使用 `TextMeshPro`、文字与颜色来自结果、世界高度可读、面向相机、至少 2 秒后完成一次。集成测试继续验证射击相机在跳字期间保持、致死目标销毁后仍显示快照、完成后回到攻击者中心，以及场景清理不遗留实例。新增装配测试验证 Map1 引用 Prefab/字体且 `BattleCameraDirector` 的测试无需创建文字对象。

## Risks / Trade-offs

- [项目尚无 TMP Essential Resources，Prefab 字体引用可能为空或不同机器回退不一致] → 将实际 `TMP_FontAsset` 纳入项目并由 Prefab 显式引用，初始化时拒绝空字体配置。
- [TMP 世界单位字号与旧 TextMesh 尺寸不同，首次替换可能过大或过小] → 以当前一格 1 世界单位和现有大于 0.2 世界单位的可读高度测试为基线，在 Prefab 中调参并做 Map1 视觉验收。
- [控制器同时协调相机和跳字完成，仍存在流程级依赖] → 依赖保持单向：控制器只调用两个独立服务；相机服务不认识跳字类型，跳字服务不修改相机优先级。
- [清理顺序错误可能让完成回调在场景退出时恢复已释放镜头] → 管理器取消路径不执行完成回调，并在 `BattlePlayerController.Shutdown` 后、相机释放前清理活动实例。
- [不使用对象池会产生实例化和销毁开销] → 当前射击为串行低频事件，先保持实现简单；通过管理器集中入口保留未来引入池而不影响调用方的空间。

## Migration Plan

1. 加入 `Unity.TextMeshPro` asmdef 引用和项目内 TMP 字体资产，创建并校验世界空间跳字 Prefab。
2. 将旧弹字脚本改造成只负责单实例 TMP 播放的视图，新增管理器并接入 Map1/BattleSceneManager 生命周期。
3. 把结果展示调用移到控制层，删除 `BattleCameraDirector` 中全部文字字段、创建与取消逻辑。
4. 更新测试和 README，运行脚本编译、相关 PlayMode 测试及完整 PlayMode 回归，在 Map1 验收命中、擦伤、未命中和致死目标。
5. 若需要回滚，恢复旧 `BattleShotResultPopup.Create` 与 `BattleCameraDirector.ShowResult` 接线并移除 Map1 管理器引用；射击规则和数据结构无需回滚。
