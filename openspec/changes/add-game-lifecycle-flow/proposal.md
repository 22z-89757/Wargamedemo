# Proposal

## Why

项目目前只有对象内部的 C# 事件，缺少可跨场景使用的游戏生命周期通知；同时 `BeginScene` 与 `End` 场景尚无可操作 UI，Build Settings 也只包含战斗场景，无法形成“开始界面 -> 战斗 -> 结算界面”的完整流程。需要建立持久化、强类型的事件入口，并让开始与结算流程拥有明确且可验证的行为。

## What Changes

- 新增跨场景存活的 `EventCenter`，以事件类型作为标识提供订阅、退订和发布能力，并处理重复实例及静态状态重置。
- 为游戏真正开始和正常结算分别建立独立文件中的 `GameStartedEvent`、`GameEndedEvent` 强类型事件；结算事件携带可供 UI 展示的胜负结果。
- 在 `BeginScene` 根层级配置名为 `EventCenter` 的持久对象，并提供标题与“开始游戏”按钮；按钮进入 `Map1`，战斗成功初始化后才发布开始事件。
- 在战斗中加入最小胜负判定：任一对战阵营没有存活单位后只结算一次，发布结束事件并进入 `End` 场景；场景卸载或初始化失败不视为正常结算。
- 在 `End` 场景提供简单结算 UI，展示胜利或失败结果，并提供返回开始场景的按钮。
- 将 `BeginScene`、`Map1`、`End` 按流程加入 Build Settings，并补充事件总线及跨场景流程测试。

## Capabilities

### New Capabilities

- `typed-event-bus`: 提供按事件类型隔离、可跨场景存活且可安全订阅和发布的全局事件通道。
- `game-lifecycle-flow`: 定义开始界面、战斗初始化、胜负结算、结算界面及返回开始界面的完整场景流程。

### Modified Capabilities

无。

## Impact

- 新增 `Assets/Scripts/EventCenter/` 下的事件中心、两个生命周期事件及相关 UI/流程脚本，每个主要类型单独分文件。
- 修改 `BattleSceneManager` 的生命周期集成和胜负判定，但保留 `BattleTurnManager` 作为战斗运行状态来源。
- 修改 `Assets/Scenes/BeginScene.unity`、`Assets/Scenes/End.unity`、`Assets/Scenes/Map1.unity` 以及 `ProjectSettings/EditorBuildSettings.asset`。
- 扩充 EditMode 与 PlayMode 测试；不引入新的第三方依赖。
