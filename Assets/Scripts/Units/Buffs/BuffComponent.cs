using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wargame.Units
{
    /// <summary>
    /// 单位的 Buff 管理器：按叠加策略施加 Buff，自动挂载/撤销授予标记与属性修饰，
    /// 并在承受者回合开始/结束时驱动 Buff 钩子和倒计时。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuffComponent : MonoBehaviour
    {
        private readonly List<BuffBase> buffs = new List<BuffBase>();
        private Unit owner;

        /// <summary>Buff 作为新实例生效、被刷新或叠层后触发。</summary>
        public event Action<BuffBase, BuffApplyResult> BuffApplied;

        /// <summary>Buff 被移除后触发。</summary>
        public event Action<BuffBase, BuffRemoveReason> BuffRemoved;

        /// <summary>当前生效的全部 Buff，按施加顺序排列。</summary>
        public IReadOnlyList<BuffBase> Buffs => buffs;

        private Unit Owner
        {
            get
            {
                if (owner == null)
                    owner = GetComponent<Unit>();
                if (owner == null)
                    throw new InvalidOperationException($"{name} 缺少 Unit 组件，BuffComponent 无法工作。");
                return owner;
            }
        }

        /// <summary>按叠加策略施加 Buff。</summary>
        /// <param name="buff">未生效的新 Buff 实例。</param>
        /// <param name="source">施加者，可为空。</param>
        /// <exception cref="ArgumentNullException">Buff 为空。</exception>
        /// <exception cref="InvalidOperationException">Buff 已经挂在某个单位上。</exception>
        public BuffApplyResult TryApply(BuffBase buff, Unit source = null)
        {
            if (buff == null)
                throw new ArgumentNullException(nameof(buff));
            if (buff.IsActive)
                throw new InvalidOperationException($"Buff {buff.BuffId} 已经生效，不能重复施加同一实例。");

            Unit target = Owner;
            if (!target.IsAlive && !buff.CanApplyToDead)
                return BuffApplyResult.Rejected;
            if (target.Tags.HasAny(buff.BlockedByTags))
                return BuffApplyResult.Rejected;
            if (!buff.CanApplyTo(target, source))
                return BuffApplyResult.Rejected;

            BuffBase existing = GetBuff(buff.BuffId);
            if (existing == null || buff.StackPolicy == BuffStackPolicy.Independent)
            {
                Activate(buff, source);
                return Notify(buff, BuffApplyResult.Applied);
            }

            switch (buff.StackPolicy)
            {
                case BuffStackPolicy.Unique:
                    return BuffApplyResult.Rejected;

                case BuffStackPolicy.Refresh:
                    ResetDuration(existing);
                    existing.InvokeRefreshed();
                    return Notify(existing, BuffApplyResult.Refreshed);

                case BuffStackPolicy.Replace:
                    RemoveInternal(existing, BuffRemoveReason.Replaced);
                    Activate(buff, source);
                    return Notify(buff, BuffApplyResult.Replaced);

                case BuffStackPolicy.Stack:
                    ResetDuration(existing);
                    int previous = existing.CurrentStacks;
                    if (previous >= existing.MaxStacks)
                    {
                        existing.InvokeRefreshed();
                        return Notify(existing, BuffApplyResult.Refreshed);
                    }
                    existing.CurrentStacks = previous + 1;
                    ApplyModifiers(existing);
                    existing.InvokeStacked(previous);
                    return Notify(existing, BuffApplyResult.Stacked);

                default:
                    throw new ArgumentOutOfRangeException(nameof(buff), buff.StackPolicy, "未知的叠加策略。");
            }
        }

        /// <summary>移除指定 Buff 实例。</summary>
        /// <returns>Buff 原本是否挂在本单位上。</returns>
        public bool Remove(BuffBase buff, BuffRemoveReason reason = BuffRemoveReason.Dispelled)
        {
            if (buff == null || !buffs.Contains(buff))
                return false;
            RemoveInternal(buff, reason);
            return true;
        }

        /// <summary>移除全部指定 BuffId 的实例。</summary>
        /// <returns>移除的数量。</returns>
        public int RemoveById(string buffId, BuffRemoveReason reason = BuffRemoveReason.Dispelled)
        {
            return RemoveWhere(b => b.BuffId == buffId, reason);
        }

        /// <summary>移除全部授予了指定标记的 Buff，用于驱散燃烧、解除眩晕等。</summary>
        /// <returns>移除的数量。</returns>
        public int RemoveWithGrantedTag(GameplayTag tag, BuffRemoveReason reason = BuffRemoveReason.Dispelled)
        {
            return RemoveWhere(b => Contains(b.GrantedTags, tag), reason);
        }

        /// <summary>是否拥有指定 BuffId 的 Buff。</summary>
        public bool HasBuff(string buffId) => GetBuff(buffId) != null;

        /// <summary>获取最早施加的指定 BuffId 实例，不存在时返回空。</summary>
        public BuffBase GetBuff(string buffId)
        {
            foreach (BuffBase buff in buffs)
                if (buff.BuffId == buffId)
                    return buff;
            return null;
        }

        /// <summary>获取最早施加的指定类型实例，不存在时返回空。</summary>
        public T GetBuff<T>() where T : BuffBase
        {
            foreach (BuffBase buff in buffs)
                if (buff is T typed)
                    return typed;
            return null;
        }

        /// <summary>承受者回合开始：依次调用 Buff 的回合开始钩子。</summary>
        internal void HandleOwnerTurnStarted()
        {
            foreach (BuffBase buff in buffs.ToArray())
                if (buffs.Contains(buff))
                    buff.InvokeOwnerTurnStarted();
        }

        /// <summary>承受者回合结束：调用钩子后倒计时，归零的 Buff 以 Expired 移除。</summary>
        internal void HandleOwnerTurnEnded()
        {
            foreach (BuffBase buff in buffs.ToArray())
            {
                if (!buffs.Contains(buff))
                    continue;
                buff.InvokeOwnerTurnEnded();
                if (!buffs.Contains(buff) || buff.IsPermanent)
                    continue;
                if (buff.SkipNextTurnEndTick)
                {
                    buff.SkipNextTurnEndTick = false;
                    continue;
                }
                buff.RemainingTurns--;
                if (buff.RemainingTurns <= 0)
                    RemoveInternal(buff, BuffRemoveReason.Expired);
            }
        }

        private void Activate(BuffBase buff, Unit source)
        {
            Unit target = Owner;
            buff.Owner = target;
            buff.Source = source;
            buff.CurrentStacks = 1;
            ResetDuration(buff);
            buffs.Add(buff);
            foreach (GameplayTag tag in buff.GrantedTags)
                target.Tags.AddTag(tag);
            ApplyModifiers(buff);
            buff.InvokeApplied();
        }

        /// <summary>重置持续时间；在承受者回合内施加或刷新时跳过当回合的倒计时。</summary>
        private void ResetDuration(BuffBase buff)
        {
            buff.RemainingTurns = buff.DurationTurns;
            buff.SkipNextTurnEndTick = !buff.IsPermanent && Owner.IsTurnActive;
        }

        /// <summary>按当前层数重新挂载修饰；单位没有的属性会被跳过。</summary>
        private void ApplyModifiers(BuffBase buff)
        {
            CharacterAttribute attributes = Owner.Attributes;
            attributes.RemoveModifiersFromSource(buff);
            foreach (AttributeModifierSpec spec in buff.Modifiers)
                if (attributes.HasAttribute(spec.Attribute))
                    attributes.AddModifier(spec.CreateModifier(buff.CurrentStacks, buff));
        }

        private void RemoveInternal(BuffBase buff, BuffRemoveReason reason)
        {
            Unit target = Owner;
            buffs.Remove(buff);
            foreach (GameplayTag tag in buff.GrantedTags)
                target.Tags.RemoveTag(tag);
            target.Attributes.RemoveModifiersFromSource(buff);
            buff.InvokeRemoved(reason);
            buff.Owner = null;
            buff.Source = null;
            buff.SkipNextTurnEndTick = false;
            BuffRemoved?.Invoke(buff, reason);
        }

        private int RemoveWhere(Predicate<BuffBase> match, BuffRemoveReason reason)
        {
            int removed = 0;
            foreach (BuffBase buff in buffs.ToArray())
            {
                if (!buffs.Contains(buff) || !match(buff))
                    continue;
                RemoveInternal(buff, reason);
                removed++;
            }
            return removed;
        }

        private BuffApplyResult Notify(BuffBase buff, BuffApplyResult result)
        {
            BuffApplied?.Invoke(buff, result);
            return result;
        }

        private static bool Contains(IReadOnlyList<GameplayTag> tags, GameplayTag tag)
        {
            for (int i = 0; i < tags.Count; i++)
                if (tags[i] == tag)
                    return true;
            return false;
        }
    }
}
