# Proposal

## Why

当前射击 LOS 只检查射击者与目标当前格之间的一条射线，射击者被墙角或障碍挡住时，即使相邻位置存在可观察目标的角度，也无法开火。允许从射击者周围相邻格进行逻辑 LOS 探测，可表现墙后探头射击，同时不移动单位或改变其棋盘占位。

## What Changes

- 修改射击目标合法性：在保持射击者与目标实际位置不变的前提下，检查射击者当前格周围 8 个相邻格到目标当前格的视线。
- 只要至少一条候选 LOS 可达，目标即满足射击视线条件；所有候选均被阻挡或无效时仍拒绝射击。
- 保持 SightRange、距离计算、目标/攻击者状态、阵营、棋盘登记、AP 与结算规则不变。
- 保持通用 LOS 查询的端点及阻挡语义；探头候选属于射击规则，不扩展到移动或其他 LOS 使用场景。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `ranged-attack`：修改目标可射击所需的 LOS 条件，允许使用攻击者周围 8 个相邻格作为候选射线起点，任一候选可达即可。

## Impact

- 运行时：`Assets/Scripts/Units/Abilities/Dic/ShootAbility.cs` 中的目标视线合法性检查；复用 `GridLineOfSight` 与现有棋盘地图适配器。
- 测试：扩展射击 EditMode 测试，覆盖被墙遮挡但邻格有通路、所有邻格均被遮挡、棋盘边界候选及不改变单位占位等行为。
- 不修改 `GridLineOfSight` 的 supercover 算法、移动规则、射击距离/命中率公式或 HUD 交互。
