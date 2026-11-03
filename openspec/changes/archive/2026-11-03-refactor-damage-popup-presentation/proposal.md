# Proposal

## Why

当前伤害跳字由 `BattleCameraDirector` 直接创建和持有运行时 `TextMesh`，文字表现、生命周期与镜头恢复逻辑耦合，且字号、颜色、材质等视觉参数散落在代码中，不便于在 Unity Inspector 中迭代。需要在保持既有射击结果与镜头时序不变的前提下，将跳字改为可视化配置的世界空间 TMP Prefab，并集中管理其实例与清理。

## What Changes

- 使用世界空间 TextMeshPro 组件替换运行时动态添加的旧 `TextMesh`，通过独立 Prefab 配置字体、字号、颜色、对齐、材质、排序和动画参数。
- 新增统一的伤害跳字管理器，负责显式 Prefab 引用、实例创建、活动实例跟踪、完成通知和场景退出清理。
- 从 `BattleCameraDirector` 中移除跳字创建、引用与销毁职责；射击流程控制器协调“展示结果完成”与“恢复攻击者中心战术镜头”，相机系统仅管理镜头状态。
- 保留目标位置快照、命中/擦伤/未命中文案、至少 2 秒显示、面向当前输出相机以及结束后恢复镜头的既有行为。
- 暂不引入对象池；每次展示直接实例化 Prefab，完成或取消时销毁实例。
- 增加场景装配校验、Prefab/字体资产验证、PlayMode 测试和使用文档，确保视觉参数可直接在 Prefab 中调整。

## Capabilities

### New Capabilities

无。本次不引入新的外部行为能力。

### Modified Capabilities

无。既有 `combat-camera-presentation` 已覆盖目标上方世界空间结果提示、两秒停留、失效清理和镜头恢复；本次只重构实现与资源装配。

## Impact

- 受影响代码：`BattleShotResultPopup`、`BattleCameraDirector`、`BattlePlayerController`、`BattleSceneManager` 及相关 PlayMode 测试。
- 新增代码/资源：独立伤害跳字管理器、世界空间 TMP 跳字 Prefab，以及项目内可序列化引用的 TMP 字体资源（若当前项目尚未提供）。
- 装配影响：Map1 需显式引用管理器和跳字 Prefab；初始化与场景清理流程需纳入管理器。
- 依赖影响：复用 Unity 6 `com.unity.ugui` 中提供的 TextMeshPro；运行时与测试 asmdef 需要显式引用 `Unity.TextMeshPro`，不新增第三方包。
- 兼容性：不改变射击判定、伤害、能力锁、结果文案、显示时长或镜头切换规则。
