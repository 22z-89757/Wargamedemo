# Tasks

## 1. LOS 核心

- [x] 1.1 定义最小只读 LOS 地图接口并实现无状态的 supercover 求值器；使用内存地图编写 EditMode 测试，验证畅通、同格、无效端点以及全障碍与半障碍的行为。
- [x] 1.2 增加 EditMode 测试，覆盖对角格角阻挡、畅通格角、反向端点以及水平、垂直和陡角度线段；验证求值结果不受遍历方向影响且保持确定性。

## 2. 运行时棋盘适配器

- [x] 2.1 为 `GridManager` 添加实时适配器，仅将 `FullBlocked` 视为不透明；通过集成测试验证运行时障碍修改会立即反映，且单位占用和掩体不会改变 LOS 结果。
- [x] 2.2 在 `Assets/Scripts/README.md` 中说明 LOS 查询接口及当前阻挡规则；核对示例与公开 API 一致，并运行 `Wargame.EditModeTests` 测试程序集。
