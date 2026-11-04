# Tasks

## 1. 强类型事件基础设施

- [ ] 1.1 在 `Assets/Scripts/EventCenter/` 中分别创建 `GameStartedEvent.cs`、`GameEndedEvent.cs` 和 `GameOutcome.cs`，使用 `Wargame.Events` 命名空间与不可变数据，并通过 Unity 脚本编译确认每个类型可被 `Wargame.Runtime` 引用。
- [ ] 1.2 实现 `EventCenter.cs` 的强类型订阅、幂等退订、监听快照发布、显式清理、`DontDestroyOnLoad` 单例、重复实例处理和缺省实例创建，并通过新增 EditMode 测试验证类型隔离、重复订阅、退订和分发期间修改监听。
- [ ] 1.3 增加运行会话静态重置及测试清理入口，并通过连续创建/销毁实例的 EditMode 测试确认静态实例和监听不会跨测试泄漏。

## 2. 持久游戏流程与开始场景

- [ ] 2.1 实现 `GameFlowController.cs`，订阅 `GameEndedEvent`、保存当前结算结果、防止重复场景切换并提供开始新游戏和返回开始场景操作；通过 EditMode 测试验证结果保存、清除和重复请求防护。
- [ ] 2.2 实现 `BeginSceneController.cs`，在 `BeginScene` 中配置根对象 `EventCenter`、流程组件、Canvas、EventSystem、中文标题和“开始游戏”按钮，并通过场景验证测试确认序列化引用完整、根对象命名正确且按钮可调用流程控制器。
- [ ] 2.3 增加 Begin 场景 PlayMode 测试，验证一次点击只加载一次 `Map1`、切换后事件中心实例保持唯一，并验证从持久实例返回 Begin 时场景内重复实例不会造成重复分发。

## 3. 战斗开始与一次性胜负结算

- [ ] 3.1 在 `BattleSceneManager.TryInitialize` 成功进入 `BattleState.Running` 且首个阵营回合开始后发布一次 `GameStartedEvent`，并通过 EditMode/PlayMode 测试验证成功、初始化失败和重复初始化三条路径的发布次数。
- [ ] 3.2 在单位完成棋盘及回合注册表清理后统计双方存活单位，实现胜利、失败、平局判定和一次性结算守卫；验证先调用 `EndBattle` 再发布携带正确 `GameOutcome` 的 `GameEndedEvent`。
- [ ] 3.3 扩充战斗生命周期测试，覆盖双方三种结算结果、连续死亡/后续清理不重复发布、空阵营初始化的确定结果，以及普通 `Cleanup`/场景卸载不发布结算事件。
- [ ] 3.4 运行现有 `BattleTurnManagerTests`、`BattleSceneManagerValidationTests`、`BattleFlowPlayModeTests` 和 `EnemyTurnPlayModeTests`，确认直接加载 `Map1`、回合结束和测试场景清理行为没有回归。

## 4. 结算场景 UI

- [ ] 4.1 实现 `EndSceneController.cs`，把 `Victory`、`Defeat`、`Draw` 和无数据状态映射为简体中文文案，并通过 EditMode 测试验证每种结果及返回按钮行为。
- [ ] 4.2 在 `End` 场景配置 Canvas、EventSystem、结算文本和“返回开始界面”按钮，并通过场景验证测试确认引用完整、按钮清除本局结果后请求加载 `BeginScene`。
- [ ] 4.3 增加 End 场景 PlayMode 测试，验证结束事件先保存结果再进入场景、胜负/平局文案正确、直接加载 End 显示安全提示且仍可返回。

## 5. 构建配置与端到端验证

- [ ] 5.1 将 `BeginScene`、`Map1`、`End` 依次设为 Build Settings 中索引 0、1、2 的启用场景，并通过 EditMode 配置测试确认场景路径、顺序和默认入口。
- [ ] 5.2 增加 Begin -> Map1 -> End -> Begin 的 PlayMode 端到端测试，验证开始和结束事件各发布一次、结算结果跨场景可读、返回后结果清空且全过程只有一个有效 EventCenter。
- [ ] 5.3 完成 Unity 脚本重载并运行完整 EditMode 与相关 PlayMode 测试集，确认无编译错误、无场景引用缺失、无新增未处理异常，并记录验证结果。
