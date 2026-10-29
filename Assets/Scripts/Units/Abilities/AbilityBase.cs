using System;
using System.Collections.Generic;

namespace Wargame.Units
{
    /// <summary>
    /// 所有能力的基类，参照 GAS 的 CanActivate → Commit → Activate → End 流程。
    /// 子类在构造函数中配置消耗、冷却和标记条件，实现 <see cref="OnActivate"/>，
    /// 并在效果（含动画、协程）结束后调用 <see cref="EndAbility"/>。
    /// 冷却在承受者回合开始时减一：冷却 2 的能力在第 1 回合使用后，第 3 回合才能再用。
    /// </summary>
    public abstract class AbilityBase
    {
        /// <summary>无限使用次数。</summary>
        public const int UnlimitedCharges = -1;

        private readonly List<GameplayTag> requiredTags = new List<GameplayTag>();
        private readonly List<GameplayTag> blockedTags = new List<GameplayTag>
        {
            GameplayTag.Dead, GameplayTag.Unconscious, GameplayTag.Stunned
        };

        /// <summary>配置能力身份。</summary>
        /// <param name="abilityId">能力标识，不能为空。</param>
        /// <param name="targetType">需要的目标类型。</param>
        /// <exception cref="ArgumentException">AbilityId 为空。</exception>
        protected AbilityBase(string abilityId, AbilityTargetType targetType = AbilityTargetType.None)
        {
            if (string.IsNullOrWhiteSpace(abilityId))
                throw new ArgumentException("AbilityId 不能为空。", nameof(abilityId));
            AbilityId = abilityId;
            DisplayName = abilityId;
            TargetType = targetType;
        }

        /// <summary>能力标识。</summary>
        public string AbilityId { get; }

        /// <summary>显示名称，默认等于 AbilityId。</summary>
        public string DisplayName { get; protected set; }

        /// <summary>需要的目标类型。</summary>
        public AbilityTargetType TargetType { get; }

        private int actionPointCost = 1;
        /// <summary>行动点消耗，默认 1，不能为负。</summary>
        public int ActionPointCost
        {
            get => actionPointCost;
            protected set => actionPointCost = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "行动点消耗不能为负。");
        }

        /// <summary>提交后是否清空剩余行动点，例如 XCOM 中开枪结束回合。</summary>
        public bool EndsTurn { get; protected set; }

        private int cooldownTurns;
        /// <summary>使用后的冷却回合数，0 表示无冷却。</summary>
        public int CooldownTurns
        {
            get => cooldownTurns;
            protected set => cooldownTurns = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "冷却不能为负。");
        }

        /// <summary>剩余冷却回合，0 表示可用。</summary>
        public int RemainingCooldown { get; internal set; }

        private int maxCharges = UnlimitedCharges;
        /// <summary>使用次数上限，<see cref="UnlimitedCharges"/> 表示无限。设置时同步重置剩余次数。</summary>
        public int MaxCharges
        {
            get => maxCharges;
            protected set
            {
                if (value < UnlimitedCharges || value == 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "使用次数必须为正数或 UnlimitedCharges。");
                maxCharges = value;
                RemainingCharges = value;
            }
        }

        /// <summary>剩余使用次数，无限时恒为 <see cref="UnlimitedCharges"/>。</summary>
        public int RemainingCharges { get; protected set; } = UnlimitedCharges;

        /// <summary>是否允许在非己方回合激活，例如警戒触发的反应射击。</summary>
        public bool CanActivateOutOfTurn { get; protected set; }

        /// <summary>是否为独占能力：同一单位同时只能有一个独占能力处于激活中。默认 true。</summary>
        public bool IsExclusive { get; protected set; } = true;

        /// <summary>激活所需的标记，必须全部拥有。</summary>
        public IReadOnlyList<GameplayTag> RequiredTags => requiredTags;

        /// <summary>拥有任一标记时无法激活，默认包含死亡、昏迷、眩晕。</summary>
        public IReadOnlyList<GameplayTag> BlockedTags => blockedTags;

        /// <summary>持有者，未授予时为空。</summary>
        public Unit Owner { get; internal set; }

        /// <summary>所属能力组件，未授予时为空。</summary>
        public AbilityComponent Component { get; internal set; }

        /// <summary>是否已激活且尚未结束。</summary>
        public bool IsActive { get; internal set; }

        /// <summary>本次激活的目标，未激活时为默认值。</summary>
        public AbilityTarget CurrentTarget { get; internal set; }

        /// <summary>添加必需标记。</summary>
        protected void AddRequiredTag(GameplayTag tag) => requiredTags.Add(tag);

        /// <summary>添加阻挡标记。</summary>
        protected void AddBlockedTag(GameplayTag tag)
        {
            if (!blockedTags.Contains(tag))
                blockedTags.Add(tag);
        }

        /// <summary>移除阻挡标记，例如允许眩晕时使用的能力移除 Stunned。</summary>
        protected void RemoveBlockedTag(GameplayTag tag) => blockedTags.Remove(tag);

        /// <summary>
        /// 检查能力自身的激活条件：标记、冷却、次数、行动点和目标。
        /// 回合、独占等组件级条件由 <see cref="AbilityComponent.CanActivate"/> 先行检查。
        /// 子类重写时先调用 base，再追加自定义条件并返回 <see cref="AbilityFailReason.Custom"/>。
        /// </summary>
        public virtual bool CanActivate(AbilityTarget target, out AbilityFailReason reason)
        {
            if (Owner == null)
            {
                reason = AbilityFailReason.NotGranted;
                return false;
            }

            TagComponent tags = Owner.Tags;
            if (tags.HasAny(blockedTags))
                reason = AbilityFailReason.BlockedByTag;
            else if (!tags.HasAll(requiredTags))
                reason = AbilityFailReason.MissingRequiredTag;
            else if (RemainingCooldown > 0)
                reason = AbilityFailReason.OnCooldown;
            else if (maxCharges != UnlimitedCharges && RemainingCharges <= 0)
                reason = AbilityFailReason.NoCharges;
            else if (!CanPayCost(target))
                reason = AbilityFailReason.NotEnoughActionPoints;
            else if (!IsValidTarget(target))
                reason = AbilityFailReason.InvalidTarget;
            else
                reason = AbilityFailReason.None;
            return reason == AbilityFailReason.None;
        }

        /// <summary>
        /// 目标是否有效。默认只检查类型：Self 必须是持有者，Unit 必须是存活单位，None/Cell 只要求类型一致。
        /// 子类可追加射程、视线等条件。
        /// </summary>
        public virtual bool IsValidTarget(AbilityTarget target)
        {
            switch (TargetType)
            {
                case AbilityTargetType.None:
                    return true;
                case AbilityTargetType.Self:
                    return target.Type == AbilityTargetType.Self && target.Unit == Owner;
                case AbilityTargetType.Unit:
                    return target.Type == AbilityTargetType.Unit && target.Unit != null && target.Unit.IsAlive;
                case AbilityTargetType.Cell:
                    return target.Type == AbilityTargetType.Cell;
                default:
                    return false;
            }
        }

        /// <summary>行动点是否足够；消耗为 0 时总是足够。</summary>
        protected virtual bool CanPayCost()
        {
            if (ActionPointCost == 0)
                return true;
            return Owner.Attributes.TryGetValue(AttributeType.ActionPoints, out float ap) && ap >= ActionPointCost;
        }

        /// <summary>查询指定目标的费用；固定费用能力无需重写。</summary>
        public virtual int GetActionPointCost(AbilityTarget target) => ActionPointCost;

        protected virtual bool CanPayCost(AbilityTarget target)
        {
            int cost = GetActionPointCost(target);
            return cost == ActionPointCost ? CanPayCost() :
                cost == 0 || (Owner.Attributes.TryGetValue(AttributeType.ActionPoints, out float ap) && ap >= cost);
        }

        /// <summary>可失败的提交钩子；返回 false 时不得提交任何消耗或效果。</summary>
        protected virtual bool TryCommit(AbilityTarget target, out AbilityFailReason reason)
        {
            int cost = GetActionPointCost(target);
            if (cost == ActionPointCost) Commit();
            else CommitCost(cost);
            reason = AbilityFailReason.None;
            return true;
        }

        /// <summary>提交消耗：扣行动点、扣次数、进入冷却；EndsTurn 时清空剩余行动点。</summary>
        protected virtual void Commit()
        {
            CommitCost(ActionPointCost);
        }

        /// <summary>按已验证的本次费用提交，供目标相关费用能力复用。</summary>
        protected void CommitCost(int cost)
        {
            CharacterAttribute attributes = Owner.Attributes;
            if (attributes.HasAttribute(AttributeType.ActionPoints))
            {
                if (cost > 0)
                    attributes.ApplyBaseDelta(AttributeType.ActionPoints, -cost);
                if (EndsTurn)
                    attributes.SetBaseValue(AttributeType.ActionPoints, 0f);
            }
            if (maxCharges != UnlimitedCharges)
                RemainingCharges--;
            RemainingCooldown = CooldownTurns;
        }

        /// <summary>执行能力效果。完成后必须调用 <see cref="EndAbility"/>，可以延迟到动画结束。</summary>
        protected abstract void OnActivate(AbilityTarget target);

        /// <summary>结束本次激活，未激活时忽略。</summary>
        /// <param name="cancelled">是否被取消；取消不返还已提交的消耗。</param>
        protected void EndAbility(bool cancelled = false)
        {
            if (IsActive && Component != null)
                Component.NotifyEnded(this, cancelled);
        }

        /// <summary>激活结束后调用。</summary>
        protected virtual void OnEnded(bool cancelled) { }

        /// <summary>授予给单位后调用。</summary>
        protected virtual void OnGranted() { }

        /// <summary>从单位移除前调用，此时 Owner 仍可访问。</summary>
        protected virtual void OnRemoved() { }

        /// <summary>持有者回合开始、冷却递减之后调用。</summary>
        protected virtual void OnOwnerTurnStarted() { }

        // 钩子保持 protected，子类在任何程序集中都用 protected override 重写；组件经以下入口调用。
        internal bool InvokeTryCommit(AbilityTarget target, out AbilityFailReason reason) => TryCommit(target, out reason);
        internal void InvokeActivate(AbilityTarget target) => OnActivate(target);
        internal void InvokeEnded(bool cancelled) => OnEnded(cancelled);
        internal void InvokeGranted() => OnGranted();
        internal void InvokeRemoved() => OnRemoved();
        internal void InvokeOwnerTurnStarted() => OnOwnerTurnStarted();

        /// <summary>重置使用次数，例如任务开始或补给时调用。</summary>
        public void RestoreCharges()
        {
            RemainingCharges = maxCharges;
        }
    }
}
