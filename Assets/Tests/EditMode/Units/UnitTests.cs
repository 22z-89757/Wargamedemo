using System.Collections.Generic;
using NUnit.Framework;
using Wargame.Units;

namespace Wargame.Tests
{
    /// <summary>验证单位的组件引用、唯一 ID、回合钩子顺序和死亡流程。</summary>
    public sealed class UnitTests : UnitTestBase
    {
        /// <summary>RequireComponent 自动添加四个组件，Unit 暴露的引用与物体上的组件一致。</summary>
        [Test]
        public void ExposesRequiredComponents()
        {
            Unit unit = CreateUnit("Soldier");
            Assert.That(unit.Tags, Is.SameAs(unit.GetComponent<TagComponent>()));
            Assert.That(unit.Attributes, Is.SameAs(unit.GetComponent<CharacterAttribute>()));
            Assert.That(unit.Buffs, Is.SameAs(unit.GetComponent<BuffComponent>()));
            Assert.That(unit.Abilities, Is.SameAs(unit.GetComponent<AbilityComponent>()));
            Assert.That(unit.DisplayName, Is.EqualTo("Soldier"));
            Assert.That(unit.IsAlive, Is.True);
            Assert.That(unit.IsTurnActive, Is.False);
        }

        /// <summary>每个单位获得不同的正数 ID，且多次读取不变。</summary>
        [Test]
        public void UnitIdsAreUnique()
        {
            var ids = new HashSet<int>();
            for (int i = 0; i < 5; i++)
            {
                Unit unit = CreateUnit("U" + i);
                Assert.That(unit.UnitId, Is.GreaterThan(0));
                Assert.That(unit.UnitId, Is.EqualTo(unit.UnitId));
                Assert.That(ids.Add(unit.UnitId), Is.True);
            }
        }

        /// <summary>回合开始按上限（含修饰）回满行动点。</summary>
        [Test]
        public void BeginTurnRefillsActionPointsToModifiedMax()
        {
            Unit unit = CreateUnit();
            unit.Attributes.SetBaseValue(AttributeType.ActionPoints, 0f);
            unit.Buffs.TryApply(new TestBuff("Haste").WithModifier(AttributeType.MaxActionPoints, ModifierOperation.Add, 1f));

            unit.BeginTurn();
            Assert.That(unit.IsTurnActive, Is.True);
            Assert.That(unit.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(3f));
        }

        /// <summary>顺序：行动点回满 → Buff 回合开始 → 冷却递减 → TurnStarted 事件；眩晕 Buff 可清空行动点。</summary>
        [Test]
        public void BeginTurnOrderAllowsStunToClearActionPoints()
        {
            Unit unit = CreateUnit();
            var log = new List<string>();
            var ability = new TestAbility("A").Cost(0).Cooldown(1);
            unit.Abilities.Grant(ability);
            unit.BeginTurn();
            unit.Abilities.TryActivate(ability, AbilityTarget.None, out _);
            unit.EndTurn();

            var stun = new TestBuff("Stun").WithTag(GameplayTag.Stunned);
            stun.TurnStarted = b =>
            {
                log.Add($"Buff ap={b.Owner.Attributes.GetValue(AttributeType.ActionPoints)} cd={ability.RemainingCooldown}");
                b.Owner.Attributes.SetBaseValue(AttributeType.ActionPoints, 0f);
            };
            unit.Buffs.TryApply(stun);
            unit.TurnStarted += u => log.Add($"Event cd={ability.RemainingCooldown}");

            unit.BeginTurn();
            Assert.That(log, Is.EqualTo(new[] { "Buff ap=2 cd=1", "Event cd=0" }));
            Assert.That(unit.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(0f));
            Assert.That(unit.Abilities.CanActivate(ability, AbilityTarget.None, out AbilityFailReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.BlockedByTag));
        }

        /// <summary>重复开始/结束回合被忽略；死亡单位不开始回合。</summary>
        [Test]
        public void TurnCallsAreIdempotentAndDeadUnitsSkip()
        {
            Unit unit = CreateUnit();
            int started = 0, ended = 0;
            unit.TurnStarted += _ => started++;
            unit.TurnEnded += _ => ended++;

            unit.EndTurn();
            unit.BeginTurn();
            unit.BeginTurn();
            unit.EndTurn();
            unit.EndTurn();
            Assert.That(started, Is.EqualTo(1));
            Assert.That(ended, Is.EqualTo(1));

            unit.Kill();
            unit.BeginTurn();
            Assert.That(unit.IsTurnActive, Is.False);
            Assert.That(started, Is.EqualTo(1));
        }

        /// <summary>生命归零时死亡一次：添加 Dead 标记并触发 Died；已死亡后不再重复。</summary>
        [Test]
        public void HealthReachingZeroKillsOnce()
        {
            Unit unit = CreateUnit();
            int died = 0;
            unit.Died += _ => died++;

            unit.Attributes.ApplyBaseDelta(AttributeType.Health, -4f);
            Assert.That(unit.IsAlive, Is.True);
            unit.Attributes.ApplyBaseDelta(AttributeType.Health, -20f);
            Assert.That(unit.IsAlive, Is.False);
            Assert.That(unit.Tags.HasTag(GameplayTag.Dead), Is.True);

            unit.Attributes.ApplyBaseDelta(AttributeType.Health, -1f);
            unit.Kill();
            Assert.That(died, Is.EqualTo(1));
        }

        /// <summary>回合内死亡的单位仍能正常结束回合并结算 Buff。</summary>
        [Test]
        public void UnitDyingDuringTurnStillEndsTurn()
        {
            Unit unit = CreateUnit();
            var buff = new TestBuff(duration: 2);
            unit.Buffs.TryApply(buff);
            unit.BeginTurn();
            unit.Kill();
            unit.EndTurn();
            Assert.That(unit.IsTurnActive, Is.False);
            Assert.That(buff.RemainingTurns, Is.EqualTo(1));
        }
    }
}
