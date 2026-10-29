using System;
using System.Collections.Generic;
using NUnit.Framework;
using Wargame.Battle;
using Wargame.Units;

namespace Wargame.Tests
{
    /// <summary>验证回合管理器的注册表、阵营轮转、轮数和单位回合钩子调用。</summary>
    public sealed class BattleTurnManagerTests : UnitTestBase
    {
        private BattleTurnManager manager;
        private Unit soldier;
        private Unit alien;
        private List<string> log;

        [SetUp]
        public void SetUp()
        {
            manager = CreateComponent<BattleTurnManager>("Battle");
            soldier = CreateUnit("Soldier", Faction.Player);
            alien = CreateUnit("Alien", Faction.Enemy);
            manager.RegisterUnit(soldier);
            manager.RegisterUnit(alien);
            log = new List<string>();
            manager.FactionTurnStarted += f => log.Add("Start:" + f);
            manager.FactionTurnEnded += f => log.Add("End:" + f);
        }

        /// <summary>开始战斗进入第 1 轮，只有第一个阵营的单位开始回合。</summary>
        [Test]
        public void StartBattleBeginsFirstFaction()
        {
            Assert.That(manager.State, Is.EqualTo(BattleState.NotStarted));
            Assert.That(manager.RoundNumber, Is.EqualTo(0));

            manager.StartBattle();
            Assert.That(manager.State, Is.EqualTo(BattleState.Running));
            Assert.That(manager.RoundNumber, Is.EqualTo(1));
            Assert.That(manager.CurrentFaction, Is.EqualTo(Faction.Player));
            Assert.That(soldier.IsTurnActive, Is.True);
            Assert.That(alien.IsTurnActive, Is.False);
            Assert.That(log, Is.EqualTo(new[] { "Start:Player" }));
            Assert.Throws<InvalidOperationException>(() => manager.StartBattle());
        }

        /// <summary>阵营依次轮转，最后一个阵营结束后轮数加一。</summary>
        [Test]
        public void EndCurrentTurnRotatesFactionsAndRounds()
        {
            manager.StartBattle();
            manager.EndCurrentTurn();
            Assert.That(manager.CurrentFaction, Is.EqualTo(Faction.Enemy));
            Assert.That(manager.RoundNumber, Is.EqualTo(1));
            Assert.That(soldier.IsTurnActive, Is.False);
            Assert.That(alien.IsTurnActive, Is.True);

            manager.EndCurrentTurn();
            Assert.That(manager.CurrentFaction, Is.EqualTo(Faction.Player));
            Assert.That(manager.RoundNumber, Is.EqualTo(2));
            Assert.That(log, Is.EqualTo(new[] { "Start:Player", "End:Player", "Start:Enemy", "End:Enemy", "Start:Player" }));
        }

        /// <summary>未开始或已结束时不能结束回合；EndBattle 结束当前阵营回合。</summary>
        [Test]
        public void StateGuards()
        {
            Assert.Throws<InvalidOperationException>(() => manager.EndCurrentTurn());
            manager.StartBattle();
            manager.EndBattle();
            Assert.That(manager.State, Is.EqualTo(BattleState.Ended));
            Assert.That(soldier.IsTurnActive, Is.False);
            Assert.Throws<InvalidOperationException>(() => manager.EndCurrentTurn());
            Assert.DoesNotThrow(() => manager.EndBattle());
        }

        /// <summary>注册表忽略重复和空引用，按阵营查询。</summary>
        [Test]
        public void RegistryAndFactionQuery()
        {
            Assert.That(manager.RegisterUnit(soldier), Is.False);
            Assert.That(manager.RegisterUnit(null), Is.False);
            Unit second = CreateUnit("Second", Faction.Player);
            Assert.That(manager.RegisterUnit(second), Is.True);

            Assert.That(manager.GetUnits(Faction.Player), Is.EqualTo(new[] { soldier, second }));
            Assert.That(manager.GetUnits(Faction.Neutral), Is.Empty);
            Assert.That(manager.UnregisterUnit(second), Is.True);
            Assert.That(manager.UnregisterUnit(second), Is.False);
            Assert.That(manager.Units.Count, Is.EqualTo(2));
        }

        /// <summary>死亡单位不开始回合；自定义阵营顺序生效且只能在开始前设置。</summary>
        [Test]
        public void DeadUnitsSkipAndCustomOrder()
        {
            Unit civilian = CreateUnit("Civilian", Faction.Neutral);
            manager.RegisterUnit(civilian);
            civilian.Kill();
            manager.SetFactionOrder(new[] { Faction.Neutral, Faction.Enemy });

            manager.StartBattle();
            Assert.That(manager.CurrentFaction, Is.EqualTo(Faction.Neutral));
            Assert.That(civilian.IsTurnActive, Is.False);
            manager.EndCurrentTurn();
            Assert.That(alien.IsTurnActive, Is.True);
            Assert.That(soldier.IsTurnActive, Is.False, "不在顺序中的阵营不行动。");
            Assert.Throws<InvalidOperationException>(() => manager.SetFactionOrder(new[] { Faction.Player }));
        }

        /// <summary>阵营顺序不能为空或重复。</summary>
        [Test]
        public void FactionOrderValidation()
        {
            Assert.Throws<ArgumentException>(() => manager.SetFactionOrder(new Faction[0]));
            Assert.Throws<ArgumentException>(() => manager.SetFactionOrder(new[] { Faction.Player, Faction.Player }));
            Assert.Throws<ArgumentNullException>(() => manager.SetFactionOrder(null));
        }

        /// <summary>
        /// 端到端：玩家回合内施加的 1 回合 Buff 覆盖整个敌方回合，在玩家下个回合结束时到期；
        /// 敌方回合施加给玩家的 1 回合眩晕覆盖玩家的下一个回合。
        /// </summary>
        [Test]
        public void BuffDurationsAcrossFactionTurns()
        {
            manager.StartBattle();
            var hunker = new TestBuff("Hunker", duration: 1).WithTag(GameplayTag.HunkerDown);
            soldier.Buffs.TryApply(hunker);
            manager.EndCurrentTurn();
            Assert.That(soldier.Tags.HasTag(GameplayTag.HunkerDown), Is.True, "敌方回合期间生效。");

            var stun = new TestBuff("Stun", duration: 1).WithTag(GameplayTag.Stunned);
            soldier.Buffs.TryApply(stun, alien);
            manager.EndCurrentTurn();
            Assert.That(soldier.Tags.HasTag(GameplayTag.Stunned), Is.True, "玩家下个回合仍被眩晕。");

            manager.EndCurrentTurn();
            Assert.That(soldier.Tags.HasTag(GameplayTag.Stunned), Is.False);
            Assert.That(soldier.Tags.HasTag(GameplayTag.HunkerDown), Is.False);
        }
    }
}
