# Tasks

## 1. 导航依赖与路径基础

- [x] 1.1 在 `Wargame.Runtime.asmdef` 增加 AI Navigation 程序集引用，并验证 Unity 脚本重新编译后无程序集引用错误
- [x] 1.2 为移动视觉抽取可测试的导航路径计算边界，使用端点采样和完整路径校验返回世界空间 corners，并通过 EditMode 测试覆盖完整路径、无法采样和非完整路径
- [x] 1.3 修改 `UnitVisualController` 以沿导航 corners 连续移动和转向，同时保留 bob、完成回调、取消清理和终点吸附，并通过 EditMode/PlayMode 测试验证完成、取消及导航失败均只回调一次且最终对齐目标
- [x] 1.4 调整 `MoveAbility` 只向视觉层提供已提交起终点而不使用格路径驱动动画，并运行 `MoveAbilityTests` 验证可达性、AP 成本、占格和移动状态语义不变

## 2. 导航场景与障碍资源

- [x] 2.1 为 `HalfBlocked.prefab` 和 `FullBlocked.prefab` 根物体增加 `isKinematic=true`、`useGravity=false` 的 Rigidbody，保留 Collider，并通过 Prefab/PlayMode 验证障碍不会因重力或碰撞移动
- [x] 2.2 在 `Map1` 配置覆盖棋盘地面与障碍 Collider 的 `NavMeshSurface` 并生成导航数据，验证代表性起终点能绕开两类障碍取得完整路径
- [x] 2.3 增加场景或 PlayMode 导航验证，覆盖直线路径、半障碍绕行、全障碍绕行和无完整路径兜底，确认视觉路径不会改变棋盘移动合法性与 AP

## 3. 射击开火解与数据模型

- [x] 3.1 将射击 LOS 查询改为返回稳定的开火解，优先当前格并按既有正交偏移顺序选择首个探头格，通过 `ShootAbilityTests` 覆盖当前格、单探头、多探头、越界、全障碍和对角格排除
- [x] 3.2 扩展 `ShootPreview`、pending 提交数据和 `ShootResult` 记录实际射线起点，同时保持距离使用逻辑坐标，并通过测试验证预览与提交一致、目标移动后重新选择、探头不改变距离
- [x] 3.3 重构射击提交以固定随机结果和伤害但延迟应用，加入只应用一次的状态保护，并通过 EditMode 测试验证 AP 原子提交、视觉完成前能力保持激活、重复激活被拒绝和重复回调不重复伤害

## 4. 射击视觉组件与子弹

- [x] 4.1 新增 `UnitShootVisualController`，实现原位开火、探头出位、朝向、子弹飞行、归位、取消和禁用清理，并通过 PlayMode 测试验证阶段顺序、最终位置及完成回调次数
- [x] 4.2 新建无判伤职责的子弹预制体和 muzzle 发射点配置，验证命中、擦伤和未命中均各生成一枚子弹且完成后不遗留实例
- [x] 4.3 将 `ShootAbility` 注入并调用射击视觉组件，在子弹到达时应用已提交结果、全部视觉完成后结束能力，并通过测试验证到达前取消不伤害、到达后取消不回滚且不重复伤害
- [x] 4.4 覆盖目标在子弹飞行期间死亡、销毁或禁用的异常路径，验证攻击者仍能归位、能力结束且不抛出 MissingReferenceException

## 5. Unit 预制体与战斗装配

- [x] 5.1 为 `Player.prefab` 和 `Enemy.prefab` 增加启用的射击视觉组件、视觉根和 muzzle 引用，并验证两个 Prefab 的配置校验均通过
- [x] 5.2 修改 `BattleSceneManager` 在授予 `ShootAbility` 时注入射击视觉组件，并对缺失组件、muzzle 或子弹预制体给出明确初始化错误；通过初始化测试覆盖有效和无效 Prefab
- [x] 5.3 保持当前只为玩家自动授予射击能力，但增加敌人手动授予/测试装配路径，验证敌人使用同一射击视觉接口完成普通和探头射击

## 6. 集成验证

- [x] 6.1 运行全部 EditMode 测试，确认移动、射击、LOS、UI、回合和场景校验测试通过且无新增 `#REF` 类资源引用错误
- [x] 6.2 运行相关 PlayMode 测试并在 `Map1` 实测玩家连续绕障移动、普通射击、探头射击、子弹判伤、归位和动作锁，确认 Console 无异常
- [x] 6.3 在测试中为敌人授予 `ShootAbility` 并执行普通/探头射击，确认与玩家共用相同流程且棋盘占格始终保持原位
- [x] 6.4 执行 Unity 项目完整脚本编译和资源导入验证；若 Pipeline 连续 2 分钟无可观察进展，按项目约定汇报阶段与诊断，最终确认编译成功或记录阻塞证据
- [x] 6.5 运行 `openspec validate add-navigation-movement-and-shoot-visuals --strict`，确认提案、规格、设计与任务均通过严格校验

## Workflow follow-up

- 实现与评审完成后归档该变更，并将三个增量规格同步到主规格。
