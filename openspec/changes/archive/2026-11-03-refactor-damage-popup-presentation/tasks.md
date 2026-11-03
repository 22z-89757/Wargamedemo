# Tasks

## 1. TMP 单实例视图与 Prefab

- [x] 1.1 为运行时和 PlayMode 测试程序集加入 `Unity.TextMeshPro` 引用，准备项目内显式 `TMP_FontAsset`，并创建 `Assets/Prefabs/UI/BattleDamagePopup.prefab`；验证 Prefab 使用 3D `TextMeshPro`、字体引用非空、没有 Canvas/旧 `TextMesh`，且所有视觉参数可在 Inspector 中调整。
- [x] 1.2 将旧跳字脚本重构为只负责单实例播放的 TMP 视图，支持结果文案、命中/擦伤/未命中样式、位置快照、面向相机、unscaled time 上浮与至少 2 秒保持；补充 PlayMode 测试验证世界高度可读、完成/取消只通知一次且销毁后无残留。

## 2. 统一管理器与场景生命周期

- [x] 2.1 实现 `BattleDamagePopupManager` 的显式 Prefab/相机初始化、活动实例集合、`ShowShotResult`、完成移除和幂等 `Shutdown`；补充测试验证可并存实例独立完成、重复展示直接实例化新对象、取消清理不触发完成回调且未引入对象池。
- [x] 2.2 将管理器接入 `BattleSceneManager` 与 Map1，增加启用状态、Prefab、TMP、字体和持续时间的明确配置校验，并按“控制器停止 → 跳字清理 → 相机释放”顺序关闭；补充场景装配测试验证缺失配置会报告错误、正常场景初始化后引用有效，且不创建或恢复 `BattleVisualAssetSetup`。

## 3. 相机解耦与射击流程接线

- [x] 3.1 从 `BattleCameraDirector` 删除输出相机、跳字引用、`ShowResult` 和文字取消逻辑，由 `BattlePlayerController` 调用跳字管理器并在完成回调中执行 `EndShot(true)`；更新相机/控制器测试，验证相机类不依赖文字类型、展示失败会立即安全恢复镜头且不会永久锁定输入。
- [x] 3.2 更新端到端射击测试，验证命中、擦伤、未命中和致死目标均使用 `ShootResult.TargetPosition` 展示 TMP 跳字，射击镜头保持到实际显示满 2 秒后回到攻击者中心，场景退出或取消不会遗留 TMP 实例或迟到回调。
- [x] 3.3 更新 `Assets/Scripts/README.md` 的伤害跳字装配和数据流说明，记录 Prefab 可调参数、管理器职责、相机解耦边界与当前直接 Instantiate/Destroy 策略；核对文档中的资源路径和 Inspector 字段与实现一致。

## 4. 集成验证

- [x] 4.1 运行 Unity 脚本编译、跳字/相机相关 PlayMode 测试和完整 PlayMode 套件，并执行 OpenSpec 严格校验与差异格式检查；在 Map1 分别验收命中、未命中和致死目标的可读性、朝向、两秒停留及镜头恢复，确认无编译错误或遗留实例。
