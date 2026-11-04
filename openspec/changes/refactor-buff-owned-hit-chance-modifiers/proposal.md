# Proposal

## Why

当前方向性掩体的命中率效果由 `ShootAbility` 识别 `UnitCoverController`、`CoverType` 和半/全掩体配置后拼装修正项，导致射击能力同时承担目标编排与具体 Buff 业务规则。需要把效果声明和适用条件归还给对应 Buff，并以通用收集与聚合边界解耦能力、Buff、公式和 UI，同时保持现有玩法结果不变。

## What Changes

- 引入通用的命中率修正来源契约和只读攻击上下文，使 Buff 等来源能够提供结构化修正候选，而不让射击能力识别具体效果类型。
- 让 `CoverBuff` 持有自身命中率减值，并根据攻击类型、目标身份和实际占格来向决定是否提供修正候选。
- 引入独立的命中率修正解析器，统一收集候选、处理互斥组和叠加策略；同次斜向攻击的两个相邻掩体候选仍只采用减值最高的一项。
- 将半掩体/全掩体减值从射击距离配置中拆分为不可变的掩体效果配置，并通过显式装配创建完整的 `CoverBuff`，继续使用 `GameSettings` 中的 20/40 默认值。
- 简化 `ShootAbility` 和 `ShootRules`：前者只构造攻击事实并请求修正解析，后者只执行纯命中率计算；二者不再依赖 `UnitCoverController`、`CoverType` 或掩体文案/ID。
- 保持现有方向判定、LOS、目标合法性、预览/提交一致性、结构化 Breakdown、UI 公式和最终命中率不变。

## Capabilities

### New Capabilities

无。本变更不引入新的外部可观察能力。

### Modified Capabilities

无。本变更是内部职责与依赖方向重构，现有 `ranged-attack`、`ranged-attack-targeting-ui` 和方向性掩体行为保持不变，因此通过 `skip_specs: true` 明确不创建 delta spec。

## Impact

- 影响 `CoverBuff`、`UnitCoverController`、`BuffComponent` 的只读效果访问边界，以及射击上下文、命中率修正解析、`ShootAbility` 和 `ShootRules`。
- 调整 `GameSettings` 下射击/掩体配置的归属、校验、不可变快照与 `BattleSceneManager` 显式注入链路；现有序列化资产数值保持 20/40。
- 需要迁移现有临时 Unit 与场景装配路径，确保 Buff 创建时具有完整配置，并维持预览和提交对最新 Buff 状态的重新求值。
- UI 继续只消费现有 `HitChanceBreakdown`，无需认识 Buff 或重新计算规则。
