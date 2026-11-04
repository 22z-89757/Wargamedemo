using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Wargame.Battle;
using Wargame.Events;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class GameLifecyclePlayModeTests
    {
        private EventCenter events;
        private int startedCount;
        private int endedCount;
        private GameEndedEvent lastEnded;
        private BattleSceneManager battle;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            EventCenter.ResetForTests();
            events = EventCenter.Instance;
            startedCount = 0;
            endedCount = 0;
            events.Subscribe<GameStartedEvent>(_ => startedCount++);
            events.Subscribe<GameEndedEvent>(gameEnded =>
            {
                endedCount++;
                lastEnded = gameEnded;
            });
            yield return SceneManager.LoadSceneAsync("Map1", LoadSceneMode.Single);
            yield return null;
            battle = Object.FindFirstObjectByType<BattleSceneManager>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (battle != null)
            {
                var cleanup = SceneManager.CreateScene("GameLifecycleTestCleanup");
                Scene previous = SceneManager.GetActiveScene();
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(previous);
                yield return null;
            }
            EventCenter.ResetForTests();
        }

        [UnityTest]
        public IEnumerator DirectMapLoadInitializesWithoutPublishingStartRequest()
        {
            Assert.That(battle, Is.Not.Null);
            Assert.That(battle.IsInitialized, Is.True);
            Assert.That(battle.Turns.State, Is.EqualTo(BattleState.Running));
            Assert.That(startedCount, Is.EqualTo(0));

            Assert.That(battle.TryInitialize(out _), Is.True);
            Assert.That(startedCount, Is.EqualTo(0));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClearingBothFactionsEndsBattleAndPublishesDrawOnce()
        {
            Assert.That(battle, Is.Not.Null);
            foreach (Unit player in battle.SpawnedUnits.Where(unit => unit != null && unit.Faction == Faction.Player).ToArray())
                player.Kill();
            foreach (Unit enemy in battle.SpawnedUnits.Where(unit => unit != null && unit.Faction == Faction.Enemy).ToArray())
                enemy.Kill();

            Assert.That(battle.Turns.State, Is.EqualTo(BattleState.Ended));
            Assert.That(endedCount, Is.EqualTo(1));
            Assert.That(lastEnded.Outcome, Is.EqualTo(GameOutcome.Draw));
            Assert.That(battle.Turns.Units, Is.Empty);
            yield return null;
            Assert.That(endedCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ClearingOnlyEnemiesPublishesVictoryAfterEndBattle()
        {
            foreach (Unit enemy in battle.SpawnedUnits.Where(unit => unit != null && unit.Faction == Faction.Enemy).ToArray())
                enemy.Kill();

            Assert.That(battle.Turns.State, Is.EqualTo(BattleState.Ended));
            Assert.That(endedCount, Is.EqualTo(1));
            Assert.That(lastEnded.Outcome, Is.EqualTo(GameOutcome.Victory));
            yield return null;
            Assert.That(endedCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ClearingOnlyPlayersPublishesDefeatAfterEndBattle()
        {
            foreach (Unit player in battle.SpawnedUnits.Where(unit => unit != null && unit.Faction == Faction.Player).ToArray())
                player.Kill();

            Assert.That(battle.Turns.State, Is.EqualTo(BattleState.Ended));
            Assert.That(endedCount, Is.EqualTo(1));
            Assert.That(lastEnded.Outcome, Is.EqualTo(GameOutcome.Defeat));
            yield return null;
            Assert.That(endedCount, Is.EqualTo(1));
        }

    }
}
