using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Wargame.Units;
using Object = UnityEngine.Object;

namespace Wargame.Tests
{
    /// <summary>单位相关测试的基类：创建带默认属性的单位，并在用例结束后销毁。</summary>
    public abstract class UnitTestBase
    {
        private readonly List<GameObject> created = new List<GameObject>();

        /// <summary>销毁本用例创建的全部物体。</summary>
        [TearDown]
        public void DestroyCreatedObjects()
        {
            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);
            created.Clear();
        }

        /// <summary>创建挂有 Unit 及其依赖组件的单位，属性使用组件默认值（生命 10、行动点 2）。</summary>
        protected Unit CreateUnit(string name = "Unit", Faction faction = Faction.Player)
        {
            var go = new GameObject(name);
            created.Add(go);
            var unit = go.AddComponent<Unit>();
            unit.Faction = faction;
            return unit;
        }

        /// <summary>创建只挂一个组件的物体。</summary>
        protected T CreateComponent<T>(string name = "Component") where T : Component
        {
            var go = new GameObject(name);
            created.Add(go);
            return go.AddComponent<T>();
        }

        /// <summary>登记由测试自行创建的物体，统一销毁。</summary>
        protected void Track(GameObject go) => created.Add(go);
    }

    /// <summary>可配置的测试 Buff，记录生命周期回调。</summary>
    internal sealed class TestBuff : BuffBase
    {
        public readonly List<string> Log = new List<string>();
        public Action<TestBuff> TurnStarted;
        public Action<TestBuff> TurnEnded;
        public Func<Unit, Unit, bool> ApplyCondition;
        public bool AllowDead;

        public TestBuff(string id = "Test", int duration = 2, BuffStackPolicy policy = BuffStackPolicy.Refresh, int maxStacks = 1)
            : base(id, duration, policy, maxStacks)
        {
        }

        public TestBuff WithTag(GameplayTag tag)
        {
            AddGrantedTag(tag);
            return this;
        }

        public TestBuff BlockedBy(GameplayTag tag)
        {
            AddBlockedByTag(tag);
            return this;
        }

        public TestBuff WithModifier(AttributeType attribute, ModifierOperation operation, float magnitude)
        {
            AddModifier(attribute, operation, magnitude);
            return this;
        }

        public void RemoveMe() => RemoveSelf();

        public override bool CanApplyTo(Unit target, Unit source) => ApplyCondition == null || ApplyCondition(target, source);

        public override bool CanApplyToDead => AllowDead;

        protected override void OnApplied() => Log.Add("Applied");

        protected override void OnRemoved(BuffRemoveReason reason) => Log.Add("Removed:" + reason);

        protected override void OnRefreshed() => Log.Add("Refreshed");

        protected override void OnStacked(int previousStacks) => Log.Add($"Stacked:{previousStacks}->{CurrentStacks}");

        protected override void OnOwnerTurnStarted()
        {
            Log.Add("TurnStarted");
            TurnStarted?.Invoke(this);
        }

        protected override void OnOwnerTurnEnded()
        {
            Log.Add("TurnEnded");
            TurnEnded?.Invoke(this);
        }
    }

    /// <summary>可配置的测试能力，记录激活与结束。</summary>
    internal sealed class TestAbility : AbilityBase
    {
        public bool AutoEnd = true;
        public int ActivateCount;
        public AbilityTarget LastTarget;
        public bool? EndedCancelled;
        public Func<bool> CustomCondition;
        public Action OnActivated;
        public int TurnStartedCount;

        public TestAbility(string id = "Test", AbilityTargetType targetType = AbilityTargetType.None) : base(id, targetType)
        {
        }

        public TestAbility Cost(int ap)
        {
            ActionPointCost = ap;
            return this;
        }

        public TestAbility Cooldown(int turns)
        {
            CooldownTurns = turns;
            return this;
        }

        public TestAbility Charges(int charges)
        {
            MaxCharges = charges;
            return this;
        }

        public TestAbility EndingTurn()
        {
            EndsTurn = true;
            return this;
        }

        public TestAbility OutOfTurn()
        {
            CanActivateOutOfTurn = true;
            return this;
        }

        public TestAbility NonExclusive()
        {
            IsExclusive = false;
            return this;
        }

        public TestAbility Require(GameplayTag tag)
        {
            AddRequiredTag(tag);
            return this;
        }

        public TestAbility Block(GameplayTag tag)
        {
            AddBlockedTag(tag);
            return this;
        }

        public TestAbility Unblock(GameplayTag tag)
        {
            RemoveBlockedTag(tag);
            return this;
        }

        public void Finish(bool cancelled = false) => EndAbility(cancelled);

        public override bool CanActivate(AbilityTarget target, out AbilityFailReason reason)
        {
            if (!base.CanActivate(target, out reason))
                return false;
            if (CustomCondition != null && !CustomCondition())
            {
                reason = AbilityFailReason.Custom;
                return false;
            }
            return true;
        }

        protected override void OnActivate(AbilityTarget target)
        {
            ActivateCount++;
            LastTarget = target;
            OnActivated?.Invoke();
            if (AutoEnd)
                EndAbility();
        }

        protected override void OnEnded(bool cancelled) => EndedCancelled = cancelled;

        protected override void OnOwnerTurnStarted() => TurnStartedCount++;
    }
}
