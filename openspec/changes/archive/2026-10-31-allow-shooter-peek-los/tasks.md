# Tasks

## 1. 射击探头视线

- [x] 1.1 在射击目标合法性检查中尝试攻击者当前格及周围 8 个相邻格作为 LOS 起点，跳过越界和 `FullBlocked` 的相邻格；验证当前格与相邻格中任一畅通射线可满足 LOS，且 SightRange 仍按单位实际格距计算。
- [x] 1.2 扩展 `ShootAbilityTests`，覆盖当前射线被挡但邻格可达、全部候选被挡、候选越界/全障碍、目标超出实际格距 SightRange，以及探头查询不改变双方坐标和棋盘占格；运行 EditMode 测试确认通过。

## 2. 射击流程集成验证

- [x] 2.1 扩展战斗流程测试，验证探头可见目标进入目标列表、预览有效并可通过开火按钮流程提交，同时保持单位实际位置不变；运行 `Wargame.PlayModeTests` 确认通过。
- [x] 2.2 运行 `Wargame.EditModeTests` 与 `Wargame.PlayModeTests`，检查射击 LOS 变更没有破坏既有 LOS、移动、AP 与开火流程。
