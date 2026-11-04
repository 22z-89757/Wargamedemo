# Tasks

## 1. 通用命中率修正边界

- [x] 1.1 定义只读 `HitChanceContext`、实际占格来向掩码、`HitChanceModifierCandidate`、聚合策略和 `IHitChanceModifierSource`，保持类型不引用 `ShootAbility`、UI 或棋盘服务；通过 Unity Editor 编译并用 `rg` 确认契约层不存在这些反向依赖。
- [x] 1.2 实现无状态 `HitChanceModifierResolver`，按活动来源顺序收集独立项并对同组“最强减值”候选稳定择优，组策略冲突明确失败；通过 Unity 编译和代码级输入矩阵检查确认独立项全保留、同值保留首项、较强负值原位替换。
- [x] 1.3 为 `HitChanceModifier`/`HitChanceBreakdown` 增加提交所需的结构化等价比较或不可变快照辅助，不以字符串公式参与比较；运行现有 `ShootRulesTests` 和命中率文案相关 EditMode 回归，确认最终值钳制与 UI 遍历顺序不变。

## 2. 掩体效果配置与创建

- [x] 2.1 新增 `CoverEffectSettings` 及不可变 Snapshot，将半/全掩体 20/40、有限非负校验和全掩体不低于半掩体的规则从 `ShootSettings` 迁出，同时更新 `GameSettings` 与 `GameSettings.asset`；通过场景配置校验和资产序列化检查确认 Map1 仍取得 20/40，距离曲线结果不变。
- [x] 2.2 实现持有配置快照的 `ICoverBuffFactory`，让 `CoverBuff` 在构造时获得方向、等级和不可变命中减值；通过 Unity 编译和静态依赖检查确认 Buff 不查询 GameSettings，工厂不依赖射击能力或 UI。
- [x] 2.3 将 `BattleSceneManager`、`UnitCoverController` 和相关临时 Unit 装配迁移为显式注入棋盘与同一 CoverBuff 工厂，保留预制体缺失控制器的初始化错误；打开 Map1 验证生成单位控制器均已初始化，移动及运行时掩体变化仍会正确替换/清理四向 Buff。

## 3. Buff 自主管理命中率效果

- [x] 3.1 让 `CoverBuff` 实现通用命中率修正来源：只对远程攻击、当前 Owner 作为目标且自身方向命中实际占格来向掩码时提供带稳定 ID/名称/减值的 `cover` 组候选；在 Map1 逐项检查四个正交来向、反方向无效和探头射线不改变来向。
- [x] 3.2 将目标活动 Buff 作为解析器的只读来源，验证斜向两个相邻方向只保留减值最高项、无对应 Buff 时无修正、未来独立候选可按顺序并存；通过确认界面的结构化公式观察最终项顺序和值，不新增测试专用生产接口。
- [x] 3.3 保持 `BuffComponent` 仅承担生命周期、叠层、标签和属性修饰，移除任何为命中率添加的专用分支；用 `rg` 检查 `BuffComponent` 不引用 HitChance/Shoot/Cover 聚合类型，并运行现有 Buff EditMode 回归。

## 4. 射击求值与具体 Buff 解耦

- [x] 4.1 引入统一不可变 `ShotEvaluation`，让预览和提交共用合法性、距离、射线起点、修正解析及 Breakdown 生成路径；运行现有 `ShootAbilityTests`，确认无掩体预览、0% 射击、输入变化拒绝、AP 原子提交和结果快照保持通过。
- [x] 4.2 在随机准备后重新求值并结构化比较相关属性、位置、射线起点和已解析修正序列，覆盖确认前 Buff 变化而不依赖具体 Cover 字段；通过运行时修改目标 CoverBuff 后提交的场景检查确认使用最新值或安全拒绝，不沿用旧预览。
- [x] 4.3 从 `ShootAbility` 和 `ShootRules` 删除 `UnitCoverController`、`CoverType`、半/全掩体配置字段、掩体 ID/中文文案及 `GetTargetCover` 依赖；用 `rg` 验证这些符号不再出现在能力与纯规则文件，并确认正式场景仍由 `BattleSceneManager` 校验控制器装配。
- [x] 4.4 更新射击/Buff 架构文档，说明效果来源、上下文、聚合组、配置快照和预览/提交职责边界；核对文档示例与现有 20/40、斜向取最高和 UI Breakdown 行为一致。

## 5. 集成验证

- [x] 5.1 运行相关既有 EditMode 与 PlayMode 回归，包括 ShootRules、ShootAbility、BattleFlow 和 BattleVisual，并在 Map1 验证半掩体、全掩体、斜向最高值、确认公式及最终随机命中率与重构前一致；记录任何非本变更既有失败，不重新引入已删除的掩体专项测试集。
- [x] 5.2 执行 Unity Editor 最终编译/状态检查、`openspec validate refactor-buff-owned-hit-chance-modifiers --strict` 和本变更文件的 `git diff --check`，确认无测试专用生产 API、无具体掩体反向依赖且规划与实现一致。

## Workflow follow-up

- 实现和评审完成后归档该纯重构变更；本变更没有 delta specs 需要同步。
