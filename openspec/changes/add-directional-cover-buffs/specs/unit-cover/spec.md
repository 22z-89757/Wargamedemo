# Spec Delta

## Purpose

为单位提供与当前棋盘格四边环境一致的方向性掩体状态，使射击等下游系统能够读取稳定、可观察且随移动和地图变化更新的半掩体或全掩体保护。

## ADDED Requirements

### Requirement: 单位必须维护四个方向的掩体 Buff
系统 SHALL 为已登记在棋盘上的存活单位维护北、东、南、西四个方向的掩体状态；每个方向按当前格对应边界保存无掩体、半掩体或全掩体，并以半掩体或全掩体 CoverBuff 表示有效保护。同一方向最多存在一个 CoverBuff，不同方向的 CoverBuff SHALL 能同时存在。

#### Scenario: 当前格同时提供多向掩体
- **WHEN** 单位所在格的北边为半掩体、东边为全掩体且其余两边无掩体
- **THEN** 单位同时拥有北向半掩体和东向全掩体 CoverBuff，南向与西向没有 CoverBuff

#### Scenario: 同一方向只保留当前等级
- **WHEN** 单位所在格某方向的掩体从半掩体变为全掩体
- **THEN** 该方向的半掩体 CoverBuff 被全掩体 CoverBuff 替换，不叠加两种等级

#### Scenario: 无掩体单位
- **WHEN** 单位所在格四边均为无掩体
- **THEN** 单位不拥有任何环境 CoverBuff

### Requirement: 掩体 Buff 必须随运行时环境即时同步
系统 SHALL 在单位首次登记、占格移动、当前格掩体数据改变以及单位离开棋盘时重新同步环境 CoverBuff；同步结果 SHALL 只反映最新棋盘状态，不依赖 Transform 插值位置、回合倒计时或手工刷新。

#### Scenario: 单位移动到不同掩体格
- **WHEN** 单位从具有西向半掩体的格子移动到具有南向全掩体的格子
- **THEN** 移动占格提交后移除西向半掩体 CoverBuff，并添加南向全掩体 CoverBuff

#### Scenario: 当前格掩体在运行时改变
- **WHEN** 单位未移动，但其当前格的一个方向从全掩体改为无掩体
- **THEN** 对应方向的全掩体 CoverBuff 被移除，其他方向状态保持不变

#### Scenario: 单位离开棋盘
- **WHEN** 单位死亡、被移除或不再登记于棋盘
- **THEN** 环境产生的 CoverBuff 全部移除，不保留过期保护

### Requirement: 环境 CoverBuff 不得作为普通时效 Buff 结算
系统 SHALL 将环境 CoverBuff 视为由棋盘环境拥有的持续状态；它们不叠层、不按单位回合自然到期，也不得修改通行、LOS 或单位基础 Defense，只有环境同步能够决定其存在和等级。

#### Scenario: 跨回合保持原地掩体
- **WHEN** 单位保持在同一格且格子掩体未变化，单位经历一个或多个回合开始与结束
- **THEN** 四向 CoverBuff 状态保持不变，不因持续时间结算而消失或叠层

#### Scenario: 掩体不直接修改基础属性
- **WHEN** 单位获得半掩体或全掩体 CoverBuff
- **THEN** 单位 Defense 的基础值和通用属性值不变，由需要方向信息的射击计算读取对应 CoverBuff
