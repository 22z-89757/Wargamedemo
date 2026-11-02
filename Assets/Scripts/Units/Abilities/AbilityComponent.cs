using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wargame.Units
{
    /// <summary>
    /// 单位的能力管理器：授予/移除能力，检查并激活能力，回合开始时递减冷却。
    /// 同一单位同时只能有一个独占能力处于激活中；非独占能力（如反应射击）不受限制。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AbilityComponent : MonoBehaviour
    {
        private readonly List<AbilityBase> abilities = new List<AbilityBase>();
        private Unit owner;

        /// <summary>能力授予后触发。</summary>
        public event Action<AbilityBase> AbilityGranted;

        /// <summary>能力移除后触发。</summary>
        public event Action<AbilityBase> AbilityRemoved;

        /// <summary>能力提交消耗、开始执行前触发。</summary>
        public event Action<AbilityBase, AbilityTarget> AbilityActivated;

        /// <summary>能力结束后触发，第二个参数表示是否被取消。</summary>
        public event Action<AbilityBase, bool> AbilityEnded;

        /// <summary>已授予的全部能力。</summary>
        public IReadOnlyList<AbilityBase> Abilities => abilities;

        /// <summary>当前激活中的独占能力，没有时为空。</summary>
        public AbilityBase ActiveExclusiveAbility { get; private set; }

        private Unit Owner
        {
            get
            {
                if (owner == null)
                    owner = GetComponent<Unit>();
                if (owner == null)
                    throw new InvalidOperationException($"{name} 缺少 Unit 组件，AbilityComponent 无法工作。");
                return owner;
            }
        }

        /// <summary>授予能力；同一 AbilityId 只能授予一次。</summary>
        /// <returns>是否授予成功；已存在同 AbilityId 时返回 false。</returns>
        /// <exception cref="ArgumentNullException">能力为空。</exception>
        /// <exception cref="InvalidOperationException">能力实例已授予给其他单位。</exception>
        public bool Grant(AbilityBase ability)
        {
            if (ability == null)
                throw new ArgumentNullException(nameof(ability));
            if (ability.Owner != null)
                throw new InvalidOperationException($"能力 {ability.AbilityId} 已授予给其他单位。");
            if (GetAbility(ability.AbilityId) != null)
                return false;

            ability.Owner = Owner;
            ability.Component = this;
            abilities.Add(ability);
            ability.InvokeGranted();
            AbilityGranted?.Invoke(ability);
            return true;
        }

        /// <summary>移除能力；激活中的能力会先被取消。</summary>
        /// <returns>能力原本是否授予给本组件。</returns>
        public bool Remove(AbilityBase ability)
        {
            if (ability == null || !abilities.Contains(ability))
                return false;
            if (ability.IsActive)
                NotifyEnded(ability, true);
            ability.InvokeRemoved();
            abilities.Remove(ability);
            ability.Owner = null;
            ability.Component = null;
            AbilityRemoved?.Invoke(ability);
            return true;
        }

        /// <summary>按 AbilityId 查找能力，不存在时返回空。</summary>
        public AbilityBase GetAbility(string abilityId)
        {
            foreach (AbilityBase ability in abilities)
                if (ability.AbilityId == abilityId)
                    return ability;
            return null;
        }

        /// <summary>按类型查找第一个能力，不存在时返回空。</summary>
        public T GetAbility<T>() where T : AbilityBase
        {
            foreach (AbilityBase ability in abilities)
                if (ability is T typed)
                    return typed;
            return null;
        }

        /// <summary>检查能力能否激活：先检查授予、回合、独占，再调用能力自身的 CanActivate。</summary>
        public bool CanActivate(AbilityBase ability, AbilityTarget target, out AbilityFailReason reason)
        {
            return CheckAvailability(ability, out reason) && ability.CanActivate(target, out reason);
        }

        /// <summary>检查是否能进入目标选择，不激活能力；最终提交仍须 CanActivate。</summary>
        public bool CanPrepareTarget(AbilityBase ability, out AbilityFailReason reason) =>
            CheckAvailability(ability, out reason) && ability.CanPrepareTarget(out reason);

        private bool CheckAvailability(AbilityBase ability, out AbilityFailReason reason)
        {
            if (ability == null || !abilities.Contains(ability))
                reason = AbilityFailReason.NotGranted;
            else if (!Owner.IsTurnActive && !ability.CanActivateOutOfTurn)
                reason = AbilityFailReason.NotYourTurn;
            else if (ability.IsActive)
                reason = AbilityFailReason.AlreadyActive;
            else if (ability.IsExclusive && ActiveExclusiveAbility != null)
                reason = AbilityFailReason.AnotherAbilityActive;
            else
            {
                reason = AbilityFailReason.None;
                return true;
            }
            return false;
        }

        /// <summary>检查后提交消耗并执行能力。</summary>
        /// <returns>是否成功激活。</returns>
        public bool TryActivate(AbilityBase ability, AbilityTarget target, out AbilityFailReason reason)
        {
            if (!CanActivate(ability, target, out reason))
                return false;

            // 提交中的属性/棋盘事件可能重入激活或取消；提前持有独占锁。
            ability.IsActive = true;
            ability.CurrentTarget = target;
            if (ability.IsExclusive)
                ActiveExclusiveAbility = ability;
            try
            {
                if (!ability.InvokeTryCommit(target, out reason))
                {
                    ability.IsActive = false;
                    ability.CurrentTarget = default;
                    if (ActiveExclusiveAbility == ability) ActiveExclusiveAbility = null;
                    return false;
                }
                if (ability.IsActive) AbilityActivated?.Invoke(ability, target);
                if (ability.IsActive) ability.InvokeActivate(target);
            }
            catch
            {
                NotifyEnded(ability, true);
                throw;
            }
            return true;
        }

        /// <summary>按 AbilityId 激活能力。</summary>
        public bool TryActivate(string abilityId, AbilityTarget target, out AbilityFailReason reason)
        {
            return TryActivate(GetAbility(abilityId), target, out reason);
        }

        /// <summary>取消全部激活中的能力，已提交的消耗不返还。</summary>
        public void CancelAll()
        {
            foreach (AbilityBase ability in abilities.ToArray())
                if (ability.IsActive)
                    NotifyEnded(ability, true);
        }

        /// <summary>由 <see cref="AbilityBase.EndAbility"/> 调用，结束一次激活。</summary>
        internal void NotifyEnded(AbilityBase ability, bool cancelled)
        {
            if (!ability.IsActive)
                return;
            ability.IsActive = false;
            ability.CurrentTarget = default;
            if (ActiveExclusiveAbility == ability)
                ActiveExclusiveAbility = null;
            ability.InvokeEnded(cancelled);
            AbilityEnded?.Invoke(ability, cancelled);
        }

        /// <summary>持有者回合开始：递减冷却并调用能力钩子。</summary>
        internal void HandleOwnerTurnStarted()
        {
            foreach (AbilityBase ability in abilities.ToArray())
            {
                if (ability.RemainingCooldown > 0)
                    ability.RemainingCooldown--;
                ability.InvokeOwnerTurnStarted();
            }
        }
    }
}
