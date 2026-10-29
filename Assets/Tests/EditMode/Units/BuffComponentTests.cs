using System;
using System.Collections.Generic;
using NUnit.Framework;
using Wargame.Units;

namespace Wargame.Tests
{
    /// <summary>验证 Buff 的施加、标记与修饰挂载、叠加策略、持续时间和移除。</summary>
    public sealed class BuffComponentTests : UnitTestBase
    {
        private Unit unit;
        private BuffComponent buffs;

        [SetUp]
        public void SetUp()
        {
            unit = CreateUnit("Target");
            buffs = unit.Buffs;
        }

        /// <summary>施加时挂载标记和修饰，移除时全部撤销，Owner/Source 随之设置和清空。</summary>
        [Test]
        public void ApplyAndRemoveMountsTagsAndModifiers()
        {
            Unit source = CreateUnit("Source");
            var buff = new TestBuff("Aimed").WithTag(GameplayTag.Revealed)
                .WithModifier(AttributeType.Aim, ModifierOperation.Add, 10f);
            var applied = new List<BuffApplyResult>();
            var removed = new List<BuffRemoveReason>();
            buffs.BuffApplied += (_, result) => applied.Add(result);
            buffs.BuffRemoved += (_, reason) => removed.Add(reason);

            Assert.That(buffs.TryApply(buff, source), Is.EqualTo(BuffApplyResult.Applied));
            Assert.That(buff.Owner, Is.SameAs(unit));
            Assert.That(buff.Source, Is.SameAs(source));
            Assert.That(buff.IsActive, Is.True);
            Assert.That(unit.Tags.HasTag(GameplayTag.Revealed), Is.True);
            Assert.That(unit.Attributes.GetValue(AttributeType.Aim), Is.EqualTo(75f));
            Assert.That(buffs.HasBuff("Aimed"), Is.True);
            Assert.That(buffs.GetBuff<TestBuff>(), Is.SameAs(buff));

            Assert.That(buffs.Remove(buff), Is.True);
            Assert.That(buffs.Remove(buff), Is.False);
            Assert.That(buff.Owner, Is.Null);
            Assert.That(buff.Source, Is.Null);
            Assert.That(unit.Tags.HasTag(GameplayTag.Revealed), Is.False);
            Assert.That(unit.Attributes.GetValue(AttributeType.Aim), Is.EqualTo(65f));
            Assert.That(buff.Log, Is.EqualTo(new[] { "Applied", "Removed:Dispelled" }));
            Assert.That(applied, Is.EqualTo(new[] { BuffApplyResult.Applied }));
            Assert.That(removed, Is.EqualTo(new[] { BuffRemoveReason.Dispelled }));
        }

        /// <summary>回合外施加：每次承受者回合结束减一，归零时以 Expired 移除。</summary>
        [Test]
        public void DurationTicksAtOwnerTurnEnd()
        {
            var buff = new TestBuff(duration: 2).WithTag(GameplayTag.Burning);
            buffs.TryApply(buff);
            Assert.That(buff.RemainingTurns, Is.EqualTo(2));

            unit.BeginTurn();
            Assert.That(buff.RemainingTurns, Is.EqualTo(2), "回合开始不倒计时。");
            unit.EndTurn();
            Assert.That(buff.RemainingTurns, Is.EqualTo(1));
            Assert.That(buff.IsActive, Is.True);

            unit.BeginTurn();
            unit.EndTurn();
            Assert.That(buff.IsActive, Is.False);
            Assert.That(unit.Tags.HasTag(GameplayTag.Burning), Is.False);
            Assert.That(buff.Log[buff.Log.Count - 1], Is.EqualTo("Removed:Expired"));
        }

        /// <summary>在承受者回合内施加的持续 1 回合 Buff，跳过当回合结算，持续到下个己方回合结束。</summary>
        [Test]
        public void BuffAppliedDuringOwnTurnSkipsThatTurnsTick()
        {
            unit.BeginTurn();
            var buff = new TestBuff("HunkerDown", duration: 1).WithTag(GameplayTag.HunkerDown);
            buffs.TryApply(buff);
            unit.EndTurn();
            Assert.That(buff.IsActive, Is.True, "敌方回合期间仍然生效。");
            Assert.That(buff.RemainingTurns, Is.EqualTo(1));

            unit.BeginTurn();
            unit.EndTurn();
            Assert.That(buff.IsActive, Is.False);
        }

        /// <summary>永久 Buff 不倒计时。</summary>
        [Test]
        public void PermanentBuffNeverExpires()
        {
            var buff = new TestBuff(duration: BuffBase.Permanent);
            buffs.TryApply(buff);
            for (int i = 0; i < 5; i++)
            {
                unit.BeginTurn();
                unit.EndTurn();
            }
            Assert.That(buff.IsActive, Is.True);
            Assert.That(buff.IsPermanent, Is.True);
            Assert.That(buff.RemainingTurns, Is.EqualTo(BuffBase.Permanent));
        }

        /// <summary>Unique：已存在时拒绝。</summary>
        [Test]
        public void UniquePolicyRejectsDuplicate()
        {
            var first = new TestBuff("U", policy: BuffStackPolicy.Unique);
            var second = new TestBuff("U", policy: BuffStackPolicy.Unique);
            Assert.That(buffs.TryApply(first), Is.EqualTo(BuffApplyResult.Applied));
            Assert.That(buffs.TryApply(second), Is.EqualTo(BuffApplyResult.Rejected));
            Assert.That(buffs.Buffs, Is.EqualTo(new[] { first }));
            Assert.That(second.IsActive, Is.False);
        }

        /// <summary>Refresh：保留旧实例并重置持续时间。</summary>
        [Test]
        public void RefreshPolicyResetsDurationOnExistingInstance()
        {
            var first = new TestBuff("R", duration: 3);
            buffs.TryApply(first);
            unit.BeginTurn();
            unit.EndTurn();
            Assert.That(first.RemainingTurns, Is.EqualTo(2));

            var second = new TestBuff("R", duration: 3);
            Assert.That(buffs.TryApply(second), Is.EqualTo(BuffApplyResult.Refreshed));
            Assert.That(first.RemainingTurns, Is.EqualTo(3));
            Assert.That(second.IsActive, Is.False);
            Assert.That(buffs.Buffs.Count, Is.EqualTo(1));
            Assert.That(first.Log, Is.EqualTo(new[] { "Applied", "TurnStarted", "TurnEnded", "Refreshed" }));
        }

        /// <summary>Replace：旧实例以 Replaced 移除，新实例生效，标记计数不残留。</summary>
        [Test]
        public void ReplacePolicySwapsInstance()
        {
            var first = new TestBuff("P", policy: BuffStackPolicy.Replace).WithTag(GameplayTag.Acid);
            var second = new TestBuff("P", policy: BuffStackPolicy.Replace).WithTag(GameplayTag.Acid);
            buffs.TryApply(first);

            Assert.That(buffs.TryApply(second), Is.EqualTo(BuffApplyResult.Replaced));
            Assert.That(first.IsActive, Is.False);
            Assert.That(first.Log, Is.EqualTo(new[] { "Applied", "Removed:Replaced" }));
            Assert.That(buffs.Buffs, Is.EqualTo(new[] { second }));
            Assert.That(unit.Tags.GetTagCount(GameplayTag.Acid), Is.EqualTo(1));
        }

        /// <summary>Stack：层数递增到上限，修饰随层数缩放并刷新持续时间；满层后只刷新。</summary>
        [Test]
        public void StackPolicyScalesModifiersUpToMax()
        {
            TestBuff Make() => new TestBuff("S", duration: 2, policy: BuffStackPolicy.Stack, maxStacks: 3)
                .WithModifier(AttributeType.Aim, ModifierOperation.Add, -5f);
            var first = Make();
            buffs.TryApply(first);
            unit.BeginTurn();
            unit.EndTurn();
            Assert.That(first.RemainingTurns, Is.EqualTo(1));

            Assert.That(buffs.TryApply(Make()), Is.EqualTo(BuffApplyResult.Stacked));
            Assert.That(first.CurrentStacks, Is.EqualTo(2));
            Assert.That(first.RemainingTurns, Is.EqualTo(2));
            Assert.That(unit.Attributes.GetValue(AttributeType.Aim), Is.EqualTo(55f));

            Assert.That(buffs.TryApply(Make()), Is.EqualTo(BuffApplyResult.Stacked));
            Assert.That(buffs.TryApply(Make()), Is.EqualTo(BuffApplyResult.Refreshed));
            Assert.That(first.CurrentStacks, Is.EqualTo(3));
            Assert.That(unit.Attributes.GetValue(AttributeType.Aim), Is.EqualTo(50f));
            Assert.That(unit.Attributes.GetModifiers(AttributeType.Aim).Count, Is.EqualTo(1), "重挂修饰不能残留旧层。");
            Assert.That(first.Log, Does.Contain("Stacked:1->2").And.Contain("Stacked:2->3"));

            buffs.Remove(first);
            Assert.That(unit.Attributes.GetValue(AttributeType.Aim), Is.EqualTo(65f));
        }

        /// <summary>Independent：多个实例并存、各自计时，共享的标记按引用计数保留。</summary>
        [Test]
        public void IndependentPolicyKeepsSeparateInstances()
        {
            var shortBuff = new TestBuff("I", duration: 1, policy: BuffStackPolicy.Independent).WithTag(GameplayTag.Bleeding);
            var longBuff = new TestBuff("I", duration: 2, policy: BuffStackPolicy.Independent).WithTag(GameplayTag.Bleeding);
            Assert.That(buffs.TryApply(shortBuff), Is.EqualTo(BuffApplyResult.Applied));
            Assert.That(buffs.TryApply(longBuff), Is.EqualTo(BuffApplyResult.Applied));
            Assert.That(buffs.GetBuff("I"), Is.SameAs(shortBuff));

            unit.BeginTurn();
            unit.EndTurn();
            Assert.That(shortBuff.IsActive, Is.False);
            Assert.That(longBuff.IsActive, Is.True);
            Assert.That(unit.Tags.HasTag(GameplayTag.Bleeding), Is.True);

            unit.BeginTurn();
            unit.EndTurn();
            Assert.That(unit.Tags.HasTag(GameplayTag.Bleeding), Is.False);
        }

        /// <summary>免疫标记、自定义条件和死亡目标会拒绝施加；复活类 Buff 可施加给死亡单位。</summary>
        [Test]
        public void BlockedTagsConditionsAndDeathRejectApply()
        {
            unit.Tags.AddTag(GameplayTag.ImmuneFire);
            Assert.That(buffs.TryApply(new TestBuff("Fire").BlockedBy(GameplayTag.ImmuneFire)), Is.EqualTo(BuffApplyResult.Rejected));

            var conditional = new TestBuff("Cond") { ApplyCondition = (target, source) => source != null };
            Assert.That(buffs.TryApply(conditional), Is.EqualTo(BuffApplyResult.Rejected));
            Assert.That(buffs.TryApply(conditional, CreateUnit("Source")), Is.EqualTo(BuffApplyResult.Applied));

            unit.Kill();
            Assert.That(buffs.TryApply(new TestBuff("Late")), Is.EqualTo(BuffApplyResult.Rejected));
            Assert.That(buffs.TryApply(new TestBuff("Revive") { AllowDead = true }), Is.EqualTo(BuffApplyResult.Applied));
        }

        /// <summary>空参数和重复施加同一实例会抛出异常。</summary>
        [Test]
        public void InvalidApplyThrows()
        {
            var buff = new TestBuff();
            buffs.TryApply(buff);
            Assert.Throws<ArgumentNullException>(() => buffs.TryApply(null));
            Assert.Throws<InvalidOperationException>(() => buffs.TryApply(buff));
            Assert.Throws<InvalidOperationException>(() => CreateUnit("Other").Buffs.TryApply(buff));
            Assert.Throws<InvalidOperationException>(() => buff.WithTag(GameplayTag.Bound), "生效后不能修改配置。");
        }

        /// <summary>移除过的实例可以再次施加，状态重新初始化。</summary>
        [Test]
        public void RemovedInstanceCanBeReapplied()
        {
            var buff = new TestBuff(duration: 3, policy: BuffStackPolicy.Stack, maxStacks: 2);
            buffs.TryApply(buff);
            buffs.TryApply(new TestBuff(duration: 3, policy: BuffStackPolicy.Stack, maxStacks: 2));
            unit.BeginTurn();
            unit.EndTurn();
            buffs.Remove(buff);

            Assert.That(buffs.TryApply(buff), Is.EqualTo(BuffApplyResult.Applied));
            Assert.That(buff.CurrentStacks, Is.EqualTo(1));
            Assert.That(buff.RemainingTurns, Is.EqualTo(3));
        }

        /// <summary>按 Id 和按授予标记批量移除。</summary>
        [Test]
        public void RemoveByIdAndByGrantedTag()
        {
            buffs.TryApply(new TestBuff("A", policy: BuffStackPolicy.Independent));
            buffs.TryApply(new TestBuff("A", policy: BuffStackPolicy.Independent));
            buffs.TryApply(new TestBuff("Stun").WithTag(GameplayTag.Stunned));
            buffs.TryApply(new TestBuff("Other"));

            Assert.That(buffs.RemoveById("A"), Is.EqualTo(2));
            Assert.That(buffs.RemoveWithGrantedTag(GameplayTag.Stunned), Is.EqualTo(1));
            Assert.That(buffs.RemoveById("Missing"), Is.EqualTo(0));
            Assert.That(unit.Tags.HasTag(GameplayTag.Stunned), Is.False);
            Assert.That(buffs.Buffs.Count, Is.EqualTo(1));
            Assert.That(buffs.GetBuff("Other"), Is.Not.Null);
        }

        /// <summary>钩子中移除自己或其他 Buff 不会破坏遍历，被移除的 Buff 不再收到后续回调。</summary>
        [Test]
        public void RemovalDuringTurnHooksIsSafe()
        {
            var victim = new TestBuff("Victim");
            var killer = new TestBuff("Killer") { TurnStarted = b => b.Owner.Buffs.Remove(victim) };
            var self = new TestBuff("Self") { TurnEnded = b => b.RemoveMe() };
            buffs.TryApply(killer);
            buffs.TryApply(victim);
            buffs.TryApply(self);

            unit.BeginTurn();
            Assert.That(victim.IsActive, Is.False);
            Assert.That(victim.Log, Does.Not.Contain("TurnStarted"));

            unit.EndTurn();
            Assert.That(self.IsActive, Is.False);
            Assert.That(self.Log, Is.EqualTo(new[] { "Applied", "TurnStarted", "TurnEnded", "Removed:Dispelled" }));
            Assert.That(killer.RemainingTurns, Is.EqualTo(1));
        }

        /// <summary>持续伤害在回合开始扣血，致死后单位获得 Dead 标记。</summary>
        [Test]
        public void DamageOverTimeCanKill()
        {
            unit.Attributes.SetBaseValue(AttributeType.Health, 3f);
            var burn = new TestBuff("Burn", duration: 3).WithTag(GameplayTag.Burning);
            burn.TurnStarted = b => b.Owner.Attributes.ApplyBaseDelta(AttributeType.Health, -2f);
            buffs.TryApply(burn);

            unit.BeginTurn();
            Assert.That(unit.Attributes.GetValue(AttributeType.Health), Is.EqualTo(1f));
            unit.EndTurn();
            unit.BeginTurn();
            Assert.That(unit.IsAlive, Is.False);
            unit.EndTurn();
            Assert.That(unit.IsTurnActive, Is.False);
        }

        /// <summary>构造参数校验。</summary>
        [Test]
        public void ConstructorValidatesArguments()
        {
            Assert.Throws<ArgumentException>(() => new TestBuff(" "));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TestBuff(duration: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TestBuff(duration: -2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TestBuff(maxStacks: 0));
        }

        /// <summary>修饰的属性不存在时跳过，不影响其他效果。</summary>
        [Test]
        public void ModifierOnMissingAttributeIsSkipped()
        {
            Unit bare = CreateUnit("Bare");
            var serialized = new UnityEditor.SerializedObject(bare.GetComponent<CharacterAttribute>());
            serialized.FindProperty("defaultValues").arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var buff = new TestBuff().WithTag(GameplayTag.Revealed).WithModifier(AttributeType.Aim, ModifierOperation.Add, 5f);
            Assert.That(bare.Buffs.TryApply(buff), Is.EqualTo(BuffApplyResult.Applied));
            Assert.That(bare.Tags.HasTag(GameplayTag.Revealed), Is.True);
        }
    }
}
