using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Wargame.Battle;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class EnemyTurnPlayModeTests
    {
        private static readonly Vector2Int EnemyStart = new Vector2Int(2, 2);
        private static readonly Vector2Int EastPlayerCell = new Vector2Int(5, 2);
        private static readonly Vector2Int NorthPlayerCell = new Vector2Int(2, 5);

        private BattleSceneManager battle;
        private Unit enemy;

        [UnitySetUp]
        public IEnumerator LoadBattle()
        {
            yield return SceneManager.LoadSceneAsync("Map1", LoadSceneMode.Single);
            yield return SettleFrame();
            battle = Object.FindFirstObjectByType<BattleSceneManager>();
            Assert.That(battle, Is.Not.Null);
            Assert.That(battle.IsInitialized, Is.True);
            enemy = battle.SpawnedUnits.First(unit => unit != null && unit.Faction == Faction.Enemy);
        }

        [UnityTest]
        public IEnumerator EffectiveDirectionalCoverIsReachedBeforeOneExactShot()
        {
            IReadOnlyList<Unit> players = PrepareControlledBoard(1);
            Unit target = players[0];
            PlaceUnit(enemy, EnemyStart);
            PlaceUnit(target, EastPlayerCell);
            var coverCell = EnemyStart + Vector2Int.right;
            Assert.That(battle.Grid.TrySetCover(
                coverCell, GridDirection.East, CoverType.FullCover), Is.True);
            ConfigureCombatants(target, 2);
            ShootAbility shoot = ReplaceEnemyShoot(new SequenceShootRandom(0f, 0.99f));

            var actions = new List<string>();
            var apSettlements = new List<float>();
            var healthSettlements = new List<float>();
            bool movementWasActiveWhenShotStarted = false;
            UnitMovementVisual movement = enemy.GetComponent<UnitMovementVisual>();
            enemy.Abilities.AbilityActivated += (ability, _) =>
            {
                actions.Add(ability.AbilityId);
                if (ability is ShootAbility) movementWasActiveWhenShotStarted = movement.IsMoving;
            };
            TrackAttribute(enemy, AttributeType.ActionPoints, apSettlements);
            TrackAttribute(target, AttributeType.Health, healthSettlements);

            Assert.That(battle.TryEndPlayerTurn(), Is.True);
            Assert.That(enemy.Abilities.ActiveExclusiveAbility, Is.TypeOf<MoveAbility>());
            yield return WaitForPlayerTurn();

            Assert.That(actions, Is.EqualTo(new[] { "Move", "Shoot" }));
            Assert.That(movementWasActiveWhenShotStarted, Is.False,
                "移动视觉结束后才允许提交射击。");
            Assert.That(battle.Grid.TryGetUnitCoordinates(enemy, out Vector2Int finalCell), Is.True);
            Assert.That(finalCell, Is.EqualTo(coverCell));
            Assert.That(battle.Grid.TryGetCell(finalCell, out GridCell occupiedCell), Is.True);
            Assert.That(CoverDirectionRules.GetStrongestCover(
                occupiedCell, EastPlayerCell), Is.EqualTo(CoverType.FullCover));
            Assert.That(shoot.LastResult.Target, Is.SameAs(target));
            Assert.That(shoot.LastResult.Hit, Is.True);
            Assert.That(apSettlements, Is.EqualTo(new[] { 1f, 0f }));
            Assert.That(healthSettlements, Is.EqualTo(new[] { 6f }));
            Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(6f));
        }

        [UnityTest]
        public IEnumerator NoCoverShootsHighestFinalHitChanceTargetOnce()
        {
            IReadOnlyList<Unit> players = PrepareControlledBoard(2);
            Unit highChance = players[0];
            Unit lowChance = players[1];
            PlaceUnit(enemy, EnemyStart);
            PlaceUnit(highChance, EastPlayerCell);
            PlaceUnit(lowChance, NorthPlayerCell);
            ConfigureCombatants(highChance, 1);
            lowChance.Attributes.SetBaseValue(AttributeType.Health, 10f);
            lowChance.Attributes.SetBaseValue(AttributeType.Defense, 40f);
            lowChance.Attributes.SetBaseValue(AttributeType.Dodge, 0f);
            lowChance.Attributes.SetBaseValue(AttributeType.Armor, 0f);
            highChance.Attributes.SetBaseValue(AttributeType.Defense, 0f);
            enemy.Attributes.SetBaseValue(AttributeType.Aim, 80f);
            ShootAbility shoot = ReplaceEnemyShoot(new SequenceShootRandom(0f, 0.99f));
            Assert.That(shoot.GetPreview(highChance).HitChance,
                Is.GreaterThan(shoot.GetPreview(lowChance).HitChance));

            var actions = new List<string>();
            var apSettlements = new List<float>();
            var highHealthSettlements = new List<float>();
            var lowHealthSettlements = new List<float>();
            enemy.Abilities.AbilityActivated += (ability, _) => actions.Add(ability.AbilityId);
            TrackAttribute(enemy, AttributeType.ActionPoints, apSettlements);
            TrackAttribute(highChance, AttributeType.Health, highHealthSettlements);
            TrackAttribute(lowChance, AttributeType.Health, lowHealthSettlements);

            Assert.That(battle.TryEndPlayerTurn(), Is.True);
            Assert.That(enemy.Abilities.ActiveExclusiveAbility, Is.TypeOf<ShootAbility>());
            yield return WaitForPlayerTurn();

            Assert.That(actions, Is.EqualTo(new[] { "Shoot" }));
            Assert.That(shoot.LastResult.Target, Is.SameAs(highChance));
            Assert.That(apSettlements, Is.EqualTo(new[] { 0f }));
            Assert.That(highHealthSettlements, Is.EqualTo(new[] { 6f }));
            Assert.That(lowHealthSettlements, Is.Empty);
            Assert.That(highChance.Attributes.GetValue(AttributeType.Health), Is.EqualTo(6f));
            Assert.That(lowChance.Attributes.GetValue(AttributeType.Health), Is.EqualTo(10f));
        }

        [UnityTest]
        public IEnumerator CurrentDirectionalCoverAllowsTwoExactShotsWithTwoAp()
        {
            IReadOnlyList<Unit> players = PrepareControlledBoard(1);
            Unit target = players[0];
            PlaceUnit(enemy, EnemyStart);
            PlaceUnit(target, EastPlayerCell);
            Assert.That(battle.Grid.TrySetCover(
                EnemyStart, GridDirection.East, CoverType.FullCover), Is.True);
            ConfigureCombatants(target, 2);
            ShootAbility shoot = ReplaceEnemyShoot(
                new SequenceShootRandom(0f, 0.99f, 0f, 0.99f));

            var actions = new List<string>();
            var apSettlements = new List<float>();
            var healthSettlements = new List<float>();
            enemy.Abilities.AbilityActivated += (ability, _) => actions.Add(ability.AbilityId);
            TrackAttribute(enemy, AttributeType.ActionPoints, apSettlements);
            TrackAttribute(target, AttributeType.Health, healthSettlements);

            Assert.That(battle.TryEndPlayerTurn(), Is.True);
            Assert.That(enemy.Abilities.ActiveExclusiveAbility, Is.TypeOf<ShootAbility>());
            yield return WaitForPlayerTurn();

            Assert.That(actions, Is.EqualTo(new[] { "Shoot", "Shoot" }));
            Assert.That(shoot.LastResult.Target, Is.SameAs(target));
            Assert.That(apSettlements, Is.EqualTo(new[] { 1f, 0f }));
            Assert.That(healthSettlements, Is.EqualTo(new[] { 6f, 2f }));
            Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(2f));
            Assert.That(battle.Grid.TryGetUnitCoordinates(enemy, out Vector2Int finalCell), Is.True);
            Assert.That(finalCell, Is.EqualTo(EnemyStart));
        }

        [UnityTearDown]
        public IEnumerator CleanupBattle()
        {
            Scene cleanup = SceneManager.CreateScene("EnemyTurnPlayModeCleanup");
            Scene previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(cleanup);
            yield return SceneManager.UnloadSceneAsync(previous);
            yield return SettleFrame();
        }

        private IReadOnlyList<Unit> PrepareControlledBoard(int playerCount)
        {
            foreach (Unit otherEnemy in battle.SpawnedUnits.Where(unit =>
                         unit != null && unit.Faction == Faction.Enemy && unit != enemy))
            {
                Assert.That(battle.Turns.UnregisterUnit(otherEnemy), Is.True);
                Assert.That(battle.Grid.TryRemoveUnit(otherEnemy), Is.True);
                otherEnemy.gameObject.SetActive(false);
            }

            List<Unit> players = battle.SpawnedUnits
                .Where(unit => unit != null && unit.Faction == Faction.Player)
                .ToList();
            Assert.That(players.Count, Is.GreaterThanOrEqualTo(playerCount));
            for (int i = playerCount; i < players.Count; i++)
            {
                Assert.That(battle.Turns.UnregisterUnit(players[i]), Is.True);
                Assert.That(battle.Grid.TryRemoveUnit(players[i]), Is.True);
                players[i].gameObject.SetActive(false);
            }

            for (int x = 0; x <= 6; x++)
            for (int y = 0; y <= 6; y++)
            {
                var coordinates = new Vector2Int(x, y);
                Assert.That(battle.Grid.TrySetBlockState(
                    coordinates, BlockState.Unblocked), Is.True);
                Assert.That(battle.Grid.TrySetCover(
                    coordinates, GridDirection.North, CoverType.NoCover), Is.True);
                Assert.That(battle.Grid.TrySetCover(
                    coordinates, GridDirection.East, CoverType.NoCover), Is.True);
                Assert.That(battle.Grid.TrySetCover(
                    coordinates, GridDirection.South, CoverType.NoCover), Is.True);
                Assert.That(battle.Grid.TrySetCover(
                    coordinates, GridDirection.West, CoverType.NoCover), Is.True);
            }

            enemy.Attributes.SetBaseValue(AttributeType.Mobility, 1f);
            return players.Take(playerCount).ToList().AsReadOnly();
        }

        private void ConfigureCombatants(Unit target, float enemyActionPoints)
        {
            enemy.Attributes.SetBaseValue(AttributeType.MaxActionPoints, enemyActionPoints);
            enemy.Attributes.SetBaseValue(AttributeType.Aim, 100f);
            target.Attributes.SetBaseValue(AttributeType.Health, 10f);
            target.Attributes.SetBaseValue(AttributeType.Defense, 0f);
            target.Attributes.SetBaseValue(AttributeType.Dodge, 0f);
            target.Attributes.SetBaseValue(AttributeType.Armor, 0f);
            Physics.SyncTransforms();
        }

        private ShootAbility ReplaceEnemyShoot(IShootRandomSource randomSource)
        {
            ShootAbility previous = enemy.Abilities.GetAbility<ShootAbility>();
            Assert.That(previous, Is.Not.Null);
            Assert.That(enemy.Abilities.Remove(previous), Is.True);
            var replacement = new ShootAbility(battle.Grid, new ShootSettings(),
                enemy.GetComponent<UnitShootVisualController>(), randomSource);
            Assert.That(enemy.Abilities.Grant(replacement), Is.True);
            return replacement;
        }

        private void PlaceUnit(Unit unit, Vector2Int coordinates)
        {
            Assert.That(battle.Grid.TryMoveUnit(unit, coordinates), Is.True);
            unit.transform.position = battle.Grid.GridToWorld(coordinates);
        }

        private static void TrackAttribute(Unit unit, AttributeType attribute, ICollection<float> values)
        {
            unit.Attributes.AttributeChanged += (changed, _, value) =>
            {
                if (changed == attribute) values.Add(value);
            };
        }

        private IEnumerator WaitForPlayerTurn()
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (battle.Turns.CurrentFaction == Faction.Enemy &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(battle.Turns.CurrentFaction, Is.EqualTo(Faction.Player),
                "受控敌方回合必须在超时前安全收敛。");
            Assert.That(battle.HasActiveAction, Is.False);
        }

        private static IEnumerator SettleFrame()
        {
            yield return null;
            yield return null;
        }

        private sealed class SequenceShootRandom : IShootRandomSource
        {
            private readonly Queue<float> values;

            public SequenceShootRandom(params float[] sequence)
            {
                values = new Queue<float>(sequence);
            }

            public float NextValue() => values.Count > 0 ? values.Dequeue() : 0.99f;
        }
    }
}
