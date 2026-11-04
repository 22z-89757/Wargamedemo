# Tasks

## 1. 射击配置与命中率明细基础

- [x] 1.1 扩展 `ShootSettings` 的半掩体/全掩体命中减值、校验与不可变快照，更新 `GameSettings.asset` 默认值为 20/40，并通过 `ShootSettingsTests` 与场景配置校验测试验证有限非负值、全掩体不低于半掩体及自定义配置。
- [x] 1.2 新增不可变命中率 Breakdown 与具名 Modifier 数据模型，将既有基础命中率计算迁移到统一 `ShootRules` 入口，并通过 `ShootRulesTests` 验证无修正、半/全掩体、0–100 钳制、有序多修正项及只读快照。

## 2. Unit 方向性 CoverBuff

- [x] 2.1 实现携带方向和 HalfCover/FullCover 等级的永久环境 `CoverBuff`，确保同方向替换、不同方向并存、`InCover` 引用计数正确且不修改 Defense，并以新增 EditMode 测试覆盖施加、替换、移除和跨回合保持。
- [x] 2.2 实现 `UnitCoverController` 的 GridManager 显式初始化、Changed 订阅、四向差量同步与离棋清理，并以新增 EditMode 测试验证出生、移动、运行时 `TrySetCover`、无变化事件、禁用/销毁和离开棋盘场景。
- [x] 2.3 在 `BattleSceneManager` 装配与校验 `UnitCoverController`，更新 Player/Enemy 预制体并补充 `BattleSceneManagerValidationTests`，验证缺失组件会给出明确错误、Map1 生成单位均连接当前棋盘且既有测试临时 Unit 可显式初始化。

## 3. 方向性掩体射击结算

- [x] 3.1 实现基于攻击者与目标实际占格的来袭方向解析及目标 Cover 查询，正交读取单边、斜向取两边最高等级且忽略 `FiringOrigin`，并通过聚焦 EditMode 测试覆盖四个正交方向、四个斜向组合、反向无效和探头射线。
- [x] 3.2 将 Cover 查询和统一 Breakdown 接入 `ShootAbility` 的预览、确认提交与结果快照，通过 `ShootAbilityTests` 验证默认/自定义减值、0% 仍可射击、预览后掩体变化重新结算、无掩体回归以及预览与提交最终值一致。
- [x] 3.3 更新射击相关 README/API 注释，说明 CoverBuff 不改变 LOS、Defense 或射程，以及掩体方向按双方实际占格计算；核对文档示例与对应 EditMode 测试结果一致。

## 4. 确认界面的可扩展公式展示

- [x] 4.1 将结构化 Breakdown 传入 `ShootPreview`、`TargetUiState` 和战斗 UI 状态，新增通用中文公式 formatter，并通过 `BattleUiModelTests`/文案测试验证基础值、正负具名项、最终值及未来新增项无需专用分支。
- [x] 4.2 调整 `BattleHUD`：目标列表及非确认位置只显示 FinalChance，所选目标的射击确认区域显示完整公式；通过 EditMode/PlayMode UI 测试验证半掩体、全掩体、无掩体、动态刷新与取消/确认流程均不由 UI 重算规则。
- [x] 4.3 更新战斗 UI 文档与场景/预制体序列化引用（如确认公式需要新的 Text 字段），并用场景装配校验确认只读/交互 Canvas 分层和现有输入阻挡规则未被破坏。

## 5. 集成验收

- [x] 5.1 在 `BattleFlowPlayModeTests` 增加真实移动后获得/失去掩体、正交与斜向射击、确认公式和最终结算的端到端场景，并验证配置 20/40 时显示值与随机命中判定一致。
- [x] 5.2 运行相关聚焦 EditMode、完整 EditMode 和完整 PlayMode 测试，记录非本变更既有失败；再执行 `openspec validate add-directional-cover-buffs --strict` 与本变更文件的 `git diff --check`，将命令和结果写入变更验收记录。

## Workflow follow-up

- 实现和评审完成后同步 delta specs，并归档此 OpenSpec 变更。
