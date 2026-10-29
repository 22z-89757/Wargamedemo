using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Wargame.Units;

namespace Wargame.Tests
{
    /// <summary>验证能力的授予、激活检查、消耗提交、冷却、次数与结束流程。</summary>
    public sealed class AbilityComponentTests : UnitTestBase
    {
        private Unit unit;
        private AbilityComponent abilities;

        [SetUp]
        public void SetUp()
        {
            unit = CreateUnit("Caster");
            abilities = unit.Abilities;
            unit.BeginTurn();
        }

        private float Ap => unit.Attributes.GetValue(AttributeType.ActionPoints);

        /// <summary>授予设置 Owner/Component；同 Id 只能授予一次；已授予的实例不能给其他单位。</summary>
        [Test]
        public void GrantAndRemove()
        {
            var ability = new TestAbility("Shoot");
            var granted = new List<AbilityBase>();
            var removed = new List<AbilityBase>();
            abilities.AbilityGranted += granted.Add;
            abilities.AbilityRemoved += removed.Add;

            Assert.That(abilities.Grant(ability), Is.True);
            Assert.That(ability.Owner, Is.SameAs(unit));
            Assert.That(ability.Component, Is.SameAs(abilities));
            Assert.That(abilities.Grant(new TestAbility("Shoot")), Is.False);
            Assert.Throws<InvalidOperationException>(() => CreateUnit("Other").Abilities.Grant(ability));
            Assert.Throws<ArgumentNullException>(() => abilities.Grant(null));
            Assert.That(abilities.GetAbility("Shoot"), Is.SameAs(ability));
            Assert.That(abilities.GetAbility<TestAbility>(), Is.SameAs(ability));

            Assert.That(abilities.Remove(ability), Is.True);
            Assert.That(abilities.Remove(ability), Is.False);
            Assert.That(ability.Owner, Is.Null);
            Assert.That(abilities.GetAbility("Shoot"), Is.Null);
            Assert.That(granted, Is.EqualTo(new[] { ability }));
            Assert.That(removed, Is.EqualTo(new[] { ability }));
        }

        /// <summary>成功激活：扣行动点、执行效果、结束后清理状态，事件顺序正确。</summary>
        [Test]
        public void ActivateCommitsCostAndEnds()
        {
            var ability = new TestAbility("Move").Cost(1);
            abilities.Grant(ability);
            var log = new List<string>();
            abilities.AbilityActivated += (a, _) => log.Add("Activated");
            abilities.AbilityEnded += (a, cancelled) => log.Add("Ended:" + cancelled);
            ability.OnActivated = () => log.Add("Effect");

            Assert.That(abilities.TryActivate("Move", AbilityTarget.None, out AbilityFailReason reason), Is.True);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.None));
            Assert.That(Ap, Is.EqualTo(1f));
            Assert.That(ability.ActivateCount, Is.EqualTo(1));
            Assert.That(ability.IsActive, Is.False);
            Assert.That(ability.EndedCancelled, Is.False);
            Assert.That(abilities.ActiveExclusiveAbility, Is.Null);
            Assert.That(log, Is.EqualTo(new[] { "Activated", "Effect", "Ended:False" }));
        }

        /// <summary>行动点不足时拒绝且不扣除；消耗为 0 时总能支付。</summary>
        [Test]
        public void NotEnoughActionPoints()
        {
            var expensive = new TestAbility("Big").Cost(3);
            var free = new TestAbility("Free").Cost(0);
            abilities.Grant(expensive);
            abilities.Grant(free);

            Assert.That(abilities.TryActivate(expensive, AbilityTarget.None, out AbilityFailReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.NotEnoughActionPoints));
            Assert.That(expensive.ActivateCount, Is.EqualTo(0));
            Assert.That(Ap, Is.EqualTo(2f));

            unit.Attributes.SetBaseValue(AttributeType.ActionPoints, 0f);
            Assert.That(abilities.TryActivate(free, AbilityTarget.None, out _), Is.True);
        }

        /// <summary>EndsTurn 在扣除消耗后清空剩余行动点。</summary>
        [Test]
        public void EndsTurnClearsRemainingActionPoints()
        {
            var shoot = new TestAbility("Shoot").Cost(1).EndingTurn();
            abilities.Grant(shoot);
            abilities.TryActivate(shoot, AbilityTarget.None, out _);
            Assert.That(Ap, Is.EqualTo(0f));
        }

        /// <summary>冷却在回合开始减一：冷却 2 的能力第 1 回合使用后，第 3 回合才能再用。</summary>
        [Test]
        public void CooldownTicksAtOwnerTurnStart()
        {
            var grenade = new TestAbility("Grenade").Cost(0).Cooldown(2);
            abilities.Grant(grenade);
            Assert.That(abilities.TryActivate(grenade, AbilityTarget.None, out _), Is.True);
            Assert.That(grenade.RemainingCooldown, Is.EqualTo(2));
            Assert.That(abilities.TryActivate(grenade, AbilityTarget.None, out AbilityFailReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.OnCooldown));

            unit.EndTurn();
            unit.BeginTurn();
            Assert.That(grenade.RemainingCooldown, Is.EqualTo(1));
            Assert.That(abilities.CanActivate(grenade, AbilityTarget.None, out _), Is.False);

            unit.EndTurn();
            unit.BeginTurn();
            Assert.That(grenade.RemainingCooldown, Is.EqualTo(0));
            Assert.That(abilities.TryActivate(grenade, AbilityTarget.None, out _), Is.True);
            Assert.That(grenade.TurnStartedCount, Is.EqualTo(2));
        }

        /// <summary>次数耗尽后拒绝，RestoreCharges 恢复；无限次数不受影响。</summary>
        [Test]
        public void ChargesAreConsumedAndRestored()
        {
            var medkit = new TestAbility("Medkit").Cost(0).Charges(2);
            abilities.Grant(medkit);
            Assert.That(abilities.TryActivate(medkit, AbilityTarget.None, out _), Is.True);
            Assert.That(abilities.TryActivate(medkit, AbilityTarget.None, out _), Is.True);
            Assert.That(medkit.RemainingCharges, Is.EqualTo(0));
            Assert.That(abilities.TryActivate(medkit, AbilityTarget.None, out AbilityFailReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.NoCharges));

            medkit.RestoreCharges();
            Assert.That(medkit.RemainingCharges, Is.EqualTo(2));

            var unlimited = new TestAbility("Unlimited").Cost(0);
            abilities.Grant(unlimited);
            for (int i = 0; i < 5; i++)
                Assert.That(abilities.TryActivate(unlimited, AbilityTarget.None, out _), Is.True);
            Assert.That(unlimited.RemainingCharges, Is.EqualTo(AbilityBase.UnlimitedCharges));
        }

        /// <summary>默认阻挡死亡、昏迷、眩晕；可追加阻挡、移除阻挡和要求必需标记。</summary>
        [Test]
        public void TagRequirementsAndBlocks()
        {
            var shoot = new TestAbility("Shoot").Cost(0).Block(GameplayTag.Suppressed);
            var stunProof = new TestAbility("Struggle").Cost(0).Unblock(GameplayTag.Stunned);
            var reload = new TestAbility("Overwatch").Cost(0).Require(GameplayTag.InCover);
            abilities.Grant(shoot);
            abilities.Grant(stunProof);
            abilities.Grant(reload);
            AbilityFailReason reason;

            unit.Tags.AddTag(GameplayTag.Stunned);
            Assert.That(abilities.CanActivate(shoot, AbilityTarget.None, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.BlockedByTag));
            Assert.That(abilities.CanActivate(stunProof, AbilityTarget.None, out _), Is.True);
            unit.Tags.RemoveTag(GameplayTag.Stunned);

            unit.Tags.AddTag(GameplayTag.Suppressed);
            Assert.That(abilities.CanActivate(shoot, AbilityTarget.None, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.BlockedByTag));
            unit.Tags.RemoveTag(GameplayTag.Suppressed);
            Assert.That(abilities.CanActivate(shoot, AbilityTarget.None, out _), Is.True);

            Assert.That(abilities.CanActivate(reload, AbilityTarget.None, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.MissingRequiredTag));
            unit.Tags.AddTag(GameplayTag.InCover);
            Assert.That(abilities.CanActivate(reload, AbilityTarget.None, out _), Is.True);

            unit.Kill();
            Assert.That(abilities.CanActivate(stunProof, AbilityTarget.None, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.BlockedByTag));
        }

        /// <summary>非己方回合只能激活允许回合外使用的能力。</summary>
        [Test]
        public void OutOfTurnActivation()
        {
            var shoot = new TestAbility("Shoot").Cost(0);
            var reaction = new TestAbility("Reaction").Cost(0).OutOfTurn();
            abilities.Grant(shoot);
            abilities.Grant(reaction);
            unit.EndTurn();

            Assert.That(abilities.TryActivate(shoot, AbilityTarget.None, out AbilityFailReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.NotYourTurn));
            Assert.That(abilities.TryActivate(reaction, AbilityTarget.None, out _), Is.True);
        }

        /// <summary>未授予的能力和空能力返回 NotGranted。</summary>
        [Test]
        public void NotGrantedAbilityFails()
        {
            AbilityFailReason reason;
            Assert.That(abilities.TryActivate(new TestAbility("Loose"), AbilityTarget.None, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.NotGranted));
            Assert.That(abilities.TryActivate("Missing", AbilityTarget.None, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.NotGranted));
            Assert.That(new TestAbility("Loose").CanActivate(AbilityTarget.None, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.NotGranted));
        }

        /// <summary>延迟结束的能力保持激活；独占能力互斥，非独占能力不受限制；取消不返还消耗。</summary>
        [Test]
        public void ExclusiveAbilitiesAndDelayedEnd()
        {
            var move = new TestAbility("Move").Cost(1);
            move.AutoEnd = false;
            var shoot = new TestAbility("Shoot").Cost(0);
            var reaction = new TestAbility("Reaction").Cost(0).NonExclusive();
            abilities.Grant(move);
            abilities.Grant(shoot);
            abilities.Grant(reaction);
            AbilityFailReason reason;

            Assert.That(abilities.TryActivate(move, AbilityTarget.None, out _), Is.True);
            Assert.That(move.IsActive, Is.True);
            Assert.That(abilities.ActiveExclusiveAbility, Is.SameAs(move));

            Assert.That(abilities.TryActivate(move, AbilityTarget.None, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.AlreadyActive));
            Assert.That(abilities.TryActivate(shoot, AbilityTarget.None, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.AnotherAbilityActive));
            Assert.That(abilities.TryActivate(reaction, AbilityTarget.None, out _), Is.True);

            move.Finish(cancelled: true);
            Assert.That(move.IsActive, Is.False);
            Assert.That(move.EndedCancelled, Is.True);
            Assert.That(Ap, Is.EqualTo(1f), "取消不返还消耗。");
            Assert.That(abilities.TryActivate(shoot, AbilityTarget.None, out _), Is.True);

            move.Finish();
            Assert.That(move.EndedCancelled, Is.True, "未激活时调用 EndAbility 被忽略。");
        }

        /// <summary>移除激活中的能力和单位死亡都会取消激活。</summary>
        [Test]
        public void RemoveAndKillCancelActiveAbilities()
        {
            var channel = new TestAbility("Channel").Cost(0);
            channel.AutoEnd = false;
            var other = new TestAbility("Other").Cost(0);
            other.AutoEnd = false;
            abilities.Grant(channel);
            abilities.Grant(other);

            abilities.TryActivate(channel, AbilityTarget.None, out _);
            abilities.Remove(channel);
            Assert.That(channel.IsActive, Is.False);
            Assert.That(channel.EndedCancelled, Is.True);
            Assert.That(abilities.ActiveExclusiveAbility, Is.Null);

            abilities.TryActivate(other, AbilityTarget.None, out _);
            unit.Kill();
            Assert.That(other.IsActive, Is.False);
            Assert.That(other.EndedCancelled, Is.True);
        }

        /// <summary>默认目标校验：Self 必须是持有者，Unit 必须存活，Cell 要求类型一致。</summary>
        [Test]
        public void TargetValidation()
        {
            Unit enemy = CreateUnit("Enemy", Faction.Enemy);
            var self = new TestAbility("Hunker", AbilityTargetType.Self).Cost(0);
            var shoot = new TestAbility("Shoot", AbilityTargetType.Unit).Cost(0);
            var throwAt = new TestAbility("Throw", AbilityTargetType.Cell).Cost(0);
            abilities.Grant(self);
            abilities.Grant(shoot);
            abilities.Grant(throwAt);
            AbilityFailReason reason;

            Assert.That(abilities.CanActivate(self, AbilityTarget.Self(enemy), out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.InvalidTarget));
            Assert.That(abilities.CanActivate(self, AbilityTarget.Self(unit), out _), Is.True);

            Assert.That(abilities.CanActivate(shoot, AbilityTarget.None, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.InvalidTarget));
            Assert.That(abilities.CanActivate(shoot, AbilityTarget.ForUnit(null), out _), Is.False);
            Assert.That(abilities.TryActivate(shoot, AbilityTarget.ForUnit(enemy), out _), Is.True);
            Assert.That(shoot.LastTarget.Unit, Is.SameAs(enemy));
            enemy.Kill();
            Assert.That(abilities.CanActivate(shoot, AbilityTarget.ForUnit(enemy), out _), Is.False);

            Assert.That(abilities.CanActivate(throwAt, AbilityTarget.ForUnit(enemy), out _), Is.False);
            Assert.That(abilities.TryActivate(throwAt, AbilityTarget.ForCell(new Vector2Int(3, 4)), out _), Is.True);
            Assert.That(throwAt.LastTarget.Cell, Is.EqualTo(new Vector2Int(3, 4)));
        }

        /// <summary>子类重写 CanActivate 追加的条件返回 Custom，失败时不提交消耗。</summary>
        [Test]
        public void CustomConditionIsChecked()
        {
            bool allowed = false;
            var ability = new TestAbility("Custom").Cost(1);
            ability.CustomCondition = () => allowed;
            abilities.Grant(ability);

            Assert.That(abilities.TryActivate(ability, AbilityTarget.None, out AbilityFailReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.Custom));
            Assert.That(Ap, Is.EqualTo(2f));
            allowed = true;
            Assert.That(abilities.TryActivate(ability, AbilityTarget.None, out _), Is.True);
        }

        /// <summary>配置参数校验。</summary>
        [Test]
        public void ConfigurationValidation()
        {
            Assert.Throws<ArgumentException>(() => new TestAbility(""));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TestAbility().Cost(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TestAbility().Cooldown(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TestAbility().Charges(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TestAbility().Charges(-2));
        }

        private sealed class TargetPricedAbility : AbilityBase
        {
            public bool Reject;
            public TargetPricedAbility() : base("TargetPriced") { }
            public override int GetActionPointCost(AbilityTarget target) => 2;
            protected override bool TryCommit(AbilityTarget target, out AbilityFailReason reason)
            {
                if (!Reject) return base.TryCommit(target, out reason);
                reason = AbilityFailReason.InvalidTarget;
                return false;
            }
            protected override void OnActivate(AbilityTarget target) => EndAbility();
        }

        [Test]
        public void DynamicCostsAndFailedCommitsPreserveActivationState()
        {
            var ability = new TargetPricedAbility { Reject = true };
            abilities.Grant(ability);
            int activated = 0;
            abilities.AbilityActivated += (_, __) => activated++;
            Assert.That(abilities.TryActivate(ability, AbilityTarget.None, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.InvalidTarget));
            Assert.That(Ap, Is.EqualTo(2));
            Assert.That(ability.IsActive, Is.False);
            Assert.That(abilities.ActiveExclusiveAbility, Is.Null);
            Assert.That(activated, Is.Zero);
            ability.Reject = false;
            Assert.That(abilities.TryActivate(ability, AbilityTarget.None, out _), Is.True);
            Assert.That(Ap, Is.Zero);
            Assert.That(activated, Is.EqualTo(1));
        }
    }
}
