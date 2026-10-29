using System;
using System.Collections.Generic;

namespace Wargame.Units
{
    /// <summary>
    /// 所有 Buff 的基类。每次施加使用一个新实例，实例保存剩余回合和层数等运行时状态。
    /// 授予标记和属性修饰由 <see cref="BuffComponent"/> 自动挂载和撤销，子类只需在构造函数里配置，
    /// 并按需重写回合钩子实现持续伤害等效果。
    /// 持续时间在承受者自己的回合结束时减一；在承受者回合内施加的 Buff 跳过当回合的结算，
    /// 因此持续 1 回合的 Buff 总能覆盖承受者完整的一个己方回合或一轮敌方回合。
    /// </summary>
    public abstract class BuffBase
    {
        /// <summary>永久 Buff 的持续时间。</summary>
        public const int Permanent = -1;

        private readonly List<GameplayTag> grantedTags = new List<GameplayTag>();
        private readonly List<GameplayTag> blockedByTags = new List<GameplayTag>();
        private readonly List<AttributeModifierSpec> modifiers = new List<AttributeModifierSpec>();

        /// <summary>配置 Buff 的身份、持续时间和叠加规则。</summary>
        /// <param name="buffId">同类判定依据，不能为空。</param>
        /// <param name="durationTurns">持续回合数，必须为正数或 <see cref="Permanent"/>。</param>
        /// <param name="stackPolicy">已有同类 Buff 时的处理方式。</param>
        /// <param name="maxStacks">层数上限，至少为 1。</param>
        /// <exception cref="ArgumentException">BuffId 为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">持续时间或层数上限无效。</exception>
        protected BuffBase(string buffId, int durationTurns, BuffStackPolicy stackPolicy = BuffStackPolicy.Refresh, int maxStacks = 1)
        {
            if (string.IsNullOrWhiteSpace(buffId))
                throw new ArgumentException("BuffId 不能为空。", nameof(buffId));
            if (durationTurns == 0 || durationTurns < Permanent)
                throw new ArgumentOutOfRangeException(nameof(durationTurns), "持续时间必须为正数或 Permanent。");
            if (maxStacks < 1)
                throw new ArgumentOutOfRangeException(nameof(maxStacks), "层数上限至少为 1。");

            BuffId = buffId;
            DisplayName = buffId;
            DurationTurns = durationTurns;
            RemainingTurns = durationTurns;
            StackPolicy = stackPolicy;
            MaxStacks = maxStacks;
        }

        /// <summary>同类判定依据。</summary>
        public string BuffId { get; }

        /// <summary>显示名称，默认等于 BuffId。</summary>
        public string DisplayName { get; protected set; }

        /// <summary>完整持续回合数，<see cref="Permanent"/> 表示永久。</summary>
        public int DurationTurns { get; }

        /// <summary>剩余回合数，永久 Buff 恒为 <see cref="Permanent"/>。</summary>
        public int RemainingTurns { get; internal set; }

        /// <summary>是否永久。</summary>
        public bool IsPermanent => DurationTurns == Permanent;

        /// <summary>叠加策略。</summary>
        public BuffStackPolicy StackPolicy { get; }

        /// <summary>层数上限。</summary>
        public int MaxStacks { get; }

        /// <summary>当前层数。</summary>
        public int CurrentStacks { get; internal set; } = 1;

        /// <summary>生效期间授予承受者的标记。</summary>
        public IReadOnlyList<GameplayTag> GrantedTags => grantedTags;

        /// <summary>承受者拥有其中任一标记时无法施加，例如燃烧被火焰免疫阻挡。</summary>
        public IReadOnlyList<GameplayTag> BlockedByTags => blockedByTags;

        /// <summary>单层属性修饰配置。</summary>
        public IReadOnlyList<AttributeModifierSpec> Modifiers => modifiers;

        /// <summary>承受者，未生效时为空。</summary>
        public Unit Owner { get; internal set; }

        /// <summary>施加者，可为空（环境效果等）。</summary>
        public Unit Source { get; internal set; }

        /// <summary>是否正挂在某个单位上。</summary>
        public bool IsActive => Owner != null;

        /// <summary>为 true 时本回合结束跳过一次倒计时，由 BuffComponent 维护。</summary>
        internal bool SkipNextTurnEndTick { get; set; }

        /// <summary>添加授予标记，只能在生效前调用。</summary>
        protected void AddGrantedTag(GameplayTag tag)
        {
            EnsureConfigurable();
            grantedTags.Add(tag);
        }

        /// <summary>添加阻挡标记，只能在生效前调用。</summary>
        protected void AddBlockedByTag(GameplayTag tag)
        {
            EnsureConfigurable();
            blockedByTags.Add(tag);
        }

        /// <summary>添加单层属性修饰，只能在生效前调用。</summary>
        protected void AddModifier(AttributeType attribute, ModifierOperation operation, float magnitude)
        {
            EnsureConfigurable();
            modifiers.Add(new AttributeModifierSpec(attribute, operation, magnitude));
        }

        /// <summary>额外的施加条件；BlockedByTags 和死亡检查之后调用。</summary>
        /// <param name="target">目标单位。</param>
        /// <param name="source">施加者，可为空。</param>
        public virtual bool CanApplyTo(Unit target, Unit source) => true;

        /// <summary>是否允许施加给已死亡的单位，复活类 Buff 重写为 true。</summary>
        public virtual bool CanApplyToDead => false;

        /// <summary>作为新实例生效后调用，此时标记和修饰已挂载。</summary>
        protected virtual void OnApplied() { }

        /// <summary>移除时调用，此时标记和修饰已撤销，Owner 仍可访问。</summary>
        protected virtual void OnRemoved(BuffRemoveReason reason) { }

        /// <summary>同类 Buff 刷新了本实例的持续时间后调用。</summary>
        protected virtual void OnRefreshed() { }

        /// <summary>层数变化后调用，此时修饰已按新层数重新挂载。</summary>
        protected virtual void OnStacked(int previousStacks) { }

        /// <summary>承受者回合开始时调用，适合实现燃烧、中毒等持续伤害。</summary>
        protected virtual void OnOwnerTurnStarted() { }

        /// <summary>承受者回合结束、倒计时之前调用。</summary>
        protected virtual void OnOwnerTurnEnded() { }

        // 钩子保持 protected，子类在任何程序集中都用 protected override 重写；组件经以下入口调用。
        internal void InvokeApplied() => OnApplied();
        internal void InvokeRemoved(BuffRemoveReason reason) => OnRemoved(reason);
        internal void InvokeRefreshed() => OnRefreshed();
        internal void InvokeStacked(int previousStacks) => OnStacked(previousStacks);
        internal void InvokeOwnerTurnStarted() => OnOwnerTurnStarted();
        internal void InvokeOwnerTurnEnded() => OnOwnerTurnEnded();

        /// <summary>从承受者身上移除自己，可在任意钩子中调用。</summary>
        protected void RemoveSelf(BuffRemoveReason reason = BuffRemoveReason.Dispelled)
        {
            if (Owner != null)
                Owner.Buffs.Remove(this, reason);
        }

        private void EnsureConfigurable()
        {
            if (IsActive)
                throw new InvalidOperationException("Buff 生效后不能修改配置。");
        }
    }
}
