# Spec Delta

## Purpose

提供可复用的棋盘视线查询，让游戏系统能够判断两个格子之间是否可见，而不依赖战斗输入、单位能力、场景碰撞体或物理查询。

## ADDED Requirements

### Requirement: 查询格子之间的视线
系统 SHALL 提供确定性查询，报告两个棋盘内格子之间是否存在未受阻挡的二维格线。

#### Scenario: 两个格子之间畅通可见
- **WHEN** 查询两个棋盘内的格子，且所有经过的中间格都不阻挡视线
- **THEN** 查询报告两格之间可见

#### Scenario: 格子到自身可见
- **WHEN** 从棋盘内的某格查询到该格自身
- **THEN** 查询报告可见

#### Scenario: 无效地图坐标不可见
- **WHEN** 任一端点位于地图外，或地图数据不可用
- **THEN** 查询报告不可见

### Requirement: 全障碍阻挡视线
系统 SHALL 将路径上的 `FullBlocked` 格视为不透明；对于本查询，`HalfBlocked` 格、单位占用和方向掩体 SHALL 视为不阻挡视线。

#### Scenario: 端点之间的全障碍阻挡视线
- **WHEN** 视线经过中间的 `FullBlocked` 格
- **THEN** 查询报告不可见

#### Scenario: 端点之间的半障碍不阻挡视线
- **WHEN** 视线经过中间的 `HalfBlocked` 格，且没有全障碍阻挡
- **THEN** 查询报告可见

#### Scenario: 单位占用和掩体不阻挡视线
- **WHEN** 中间格有单位占用或方向掩体，但其状态不是 `FullBlocked`
- **THEN** 这些属性不改变视线查询结果

### Requirement: 格角穿越采用保守判定
当视线恰好经过格子共享的角点时，系统 SHALL 检查该角点两侧相邻的格子；任一格为 `FullBlocked` 时 SHALL 阻挡视线。

#### Scenario: 对角格角处的全障碍阻挡视线
- **WHEN** 视线恰好经过格角，且任一侧相邻格为 `FullBlocked`
- **THEN** 查询报告不可见

#### Scenario: 对角格角畅通时允许视线通过
- **WHEN** 视线恰好经过格角，且两侧相邻格都不阻挡视线
- **THEN** 若之后没有遇到其他阻挡，查询继续并报告可见
