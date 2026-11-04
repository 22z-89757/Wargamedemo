# Design

## Context

项目使用 Unity 6000.3.27f1，运行时代码统一位于 `Wargame.Runtime` 程序集中。现有系统主要通过拥有者对象上的 `Action` 事件协作，没有跨场景事件基础设施。`BattleSceneManager.TryInitialize` 在完成装配后调用 `BattleTurnManager.StartBattle`，而单位死亡由 `HandleUnitDied`/`ReleaseUnit` 清理；`Cleanup` 中调用 `EndBattle` 只表示场景拆卸，不代表胜负。

`BeginScene` 和 `End` 当前仅有相机与灯光，`Map1` 已包含完整战斗对象。Build Settings 目前只启用 `Map1`。现有多项 PlayMode 测试会直接加载 `Map1`，因此新事件设施不能强制所有开发与测试入口都先经过开始场景。

## Goals / Non-Goals

**Goals:**

- 提供编译期受约束的事件订阅与发布 API，并使事件中心在三段场景流程中保持唯一。
- 把“开始新局请求”“待启动状态消费”“正常结算”“场景清理”定义为不同阶段。
- 在结算事件发布后可靠地跨场景保留该局结果，而不要求 End 场景提前存在监听者。
- 保持现有局部对象事件不变，只让真正跨系统、跨场景的游戏生命周期进入全局总线。
- 保持直接加载 `Map1` 的开发和自动化测试入口可用。

**Non-Goals:**

- 不把现有单位、属性、能力、输入和 UI 的全部局部事件迁移到全局事件中心。
- 不实现多关卡选择、存档、暂停、重开、联网同步或复杂计分。
- 不把场景卸载、编辑器停止运行或初始化失败解释为玩家胜负。
- 不在事件中心中实现任意事件的历史队列或通用 replay 机制。

## Decisions

### 1. 事件类型同时作为事件标识和参数契约

`EventCenter` 对外提供 `Subscribe<TEvent>(Action<TEvent>)`、`Unsubscribe<TEvent>(Action<TEvent>)` 和 `Publish<TEvent>(TEvent)`。内部以 `Type` 作为字典键并保存委托集合，调用方不再传入字符串。发布时复制监听快照，以保证回调在分发过程中订阅或退订不会改变当前轮次。

同一事件类型与同一委托的重复订阅按幂等处理。这比复刻 `UnityAction` 加法语义更适合全局总线，可降低场景控制器重复启用造成的重复跳转风险。

备选方案是继续使用字符串加泛型参数，但同一字符串仍可能与不同参数类型组合并在运行时失败；另一个备选是仅声明两个静态 C# 事件，它足够简单，却不能满足用户要求的可扩展全局事件总线。

### 2. EventCenter 使用持久 MonoBehaviour 单例，但保留直接场景入口

`Assets/Scripts/EventCenter/EventCenter.cs` 定义 `MonoBehaviour` 单例。`BeginScene` 的根层级放置名为 `EventCenter` 的对象，并在其上配置事件中心和流程协调组件；首个有效实例在 `Awake` 中调用 `DontDestroyOnLoad`，重复实例销毁自身。

`Instance` 在没有场景实例时允许创建等价的运行时对象，从而兼容直接加载 `Map1` 的 PlayMode 测试和开发入口。使用 `RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)` 清除静态引用，避免编辑器运行会话或测试之间残留实例。普通场景切换不清空监听；场景组件必须在 `OnEnable`/`OnDisable` 或 `Initialize`/`Dispose` 中成对订阅与退订。

备选方案是只允许从 `BeginScene` 启动，但这会破坏项目现有大量直接加载 `Map1` 的测试和调试方式。

### 3. 生命周期事件和结果类型分别建文件

在 `Assets/Scripts/EventCenter/` 下采用 `Wargame.Events` 命名空间，至少包含：

- `EventCenter.cs`：强类型总线与持久实例。
- `GameStartedEvent.cs`：无额外数据的不可变标记事件。
- `GameEndedEvent.cs`：携带不可变 `GameOutcome` 的结算事件。
- `GameOutcome.cs`：`Victory`、`Defeat`、`Draw` 三种玩家视角结果。
- `GameFlowController.cs`：跨场景流程、当前结算结果及防重复切换状态。
- `BeginSceneController.cs`、`EndSceneController.cs`：各自场景 UI 的薄控制器。

这样每个强类型事件均为独立文件，也让总线保持领域无关。

### 4. GameFlowController 保存跨场景状态，事件总线不回放历史

持久对象上的 `GameFlowController` 订阅 `GameEndedEvent`。收到事件时先保存 `LastResult`，再异步加载 `End`；End 场景控制器加载后直接读取当前流程状态并渲染结果。返回开始界面时清除 `LastResult` 并异步加载 `BeginScene`。

开始按钮发布 `GameStartedEvent`。持久的 `GameFlowController` 订阅该事件，清除旧结果、设置单局待启动标记、设置切换防重入标记并加载 `Map1`。`BattleSceneManager.TryInitialize` 在完成初始化后向流程控制器消费待启动标记；事件中心不保存或回放事件历史。直接加载 `Map1` 时不存在待启动标记，但既有开发和测试初始化流程仍照常工作。

备选方案是让总线永久保留并向新监听者重放最后一条消息，但它会把一次性命令事件和可查询状态混在一起，并增加所有事件的过期规则。专用流程状态更明确。

### 5. BattleSceneManager 是结算事件的发布边界

`BattleSceneManager` 已经掌握战斗装配、单位注册、死亡清理和场景清理，因而负责把局内状态转换为全局生命周期事件：

1. `TryInitialize` 完成控制器初始化并调用 `turns.StartBattle()`。
2. 确认状态为 `Running` 后消费流程控制器的待启动标记，再执行结算检查，以覆盖空阵营配置；战斗管理器不发布 `GameStartedEvent`。
3. 单位死亡时先完成棋盘移除、回合注册表注销和事件解绑，再统计玩家及敌方存活单位。
4. 若任一阵营存活数为零，使用一次性守卫锁定结果，调用 `turns.EndBattle()`，确认状态为 `Ended` 后发布 `GameEndedEvent`。
5. `Cleanup` 仍可调用 `EndBattle()` 做资源收尾，但绝不在该路径发布结算事件。

胜负采用玩家视角：仅敌方为零是 `Victory`，仅玩家为零是 `Defeat`，双方均为零是 `Draw`。发布前设置守卫，确保事件回调引发场景加载和随后的对象销毁时不会产生第二次结算。

备选方案是让 `BattleTurnManager` 直接发布全局事件，但它只负责回合状态且目前不拥有场景装配和胜负语义，会把基础回合模型与全局导航耦合。

### 6. 场景 UI 保持薄层和显式引用

`BeginScene` 增加 Canvas、EventSystem、标题、开始按钮和 `BeginSceneController`；控制器只把按钮意图交给 `GameFlowController`，切换期间禁用按钮。`End` 增加 Canvas、EventSystem、结算文本、返回按钮和 `EndSceneController`；控制器从流程状态读取结果并映射为中文文案，没有结果时显示安全提示。

UI 使用项目已引用的 Unity UI 与 TextMeshPro，序列化引用必须在场景中显式配置并可由 EditMode 场景验证测试检查。`Map1` 不复制持久 EventCenter 对象。

### 7. 构建场景顺序固定为 BeginScene、Map1、End

`ProjectSettings/EditorBuildSettings.asset` 中启用三处场景，索引 0 为 `Assets/Scenes/BeginScene.unity`，索引 1 为 `Assets/Scenes/Map1.unity`，索引 2 为 `Assets/Scenes/End.unity`。运行时代码优先使用场景名称常量，测试同时验证名称和构建配置，避免索引变化产生隐式导航错误。

### 8. 验证分为纯事件测试、场景结构测试和端到端流程测试

- EditMode：验证类型隔离、重复订阅幂等、退订、发布快照及显式清理。
- EditMode 场景验证：检查 Begin/End UI 引用、EventCenter 根对象及 Build Settings 顺序。
- PlayMode：验证 EventCenter 跨切换保持唯一、开始事件只在战斗成功运行后发布、胜负/平局结果只发布一次、End 文案正确、返回后结果清空。
- 现有直接加载 `Map1` 的 PlayMode 测试必须继续通过，测试清理不得触发 `GameEndedEvent`。

## Risks / Trade-offs

- [持久委托引用已销毁场景对象，造成内存泄漏或回调异常] -> 所有场景监听者采用成对订阅/退订，并用 PlayMode 往返测试验证。
- [结束事件回调立即触发场景加载，打断死亡处理栈] -> 流程控制器保存结果后使用单一异步切换协程，发布者先完成注销和状态转换再发布。
- [运行时惰性创建与 BeginScene 预置对象形成重复实例] -> `Awake` 统一执行首实例保留、后到实例销毁，组件初始化必须可重复调用。
- [现有测试清理调用 `EndBattle` 被误判为结算] -> 结算发布只存在于明确的存活阵营检查路径，不挂接一般 `EndBattle` 或 `OnDestroy`。
- [空敌方列表导致进入战斗后立即胜利] -> 初始化完成后执行同一胜负检查，使配置行为确定；场景验证仍应确保正式 `Map1` 配置双方单位。
- [当前结果只保存在内存，应用重启后丢失] -> 这是单局场景流的预期取舍；持久存档不在本次范围。

## Migration Plan

1. 先添加事件类型、事件中心和流程控制器及其 EditMode 测试，不改变现有战斗行为。
2. 在 Begin/End 场景配置持久对象与 UI，再更新 Build Settings，并验证可独立打开各场景。
3. 最后接入 `BattleSceneManager` 的开始发布、胜负检查和结束发布，运行现有 EditMode/PlayMode 回归测试及新增端到端测试。
4. 若需回滚，先移除 `BattleSceneManager` 的全局事件集成和三场景导航，再还原 Build Settings；新增脚本与空闲场景 UI 可随后安全移除，不影响现有局部事件模型。
