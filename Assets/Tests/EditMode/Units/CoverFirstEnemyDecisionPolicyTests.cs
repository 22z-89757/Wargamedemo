using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Wargame.Battle;
using Wargame.Battle.AI;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class CoverFirstEnemyDecisionPolicyTests : UnitTestBase
    {
        private GridManager grid;
        private Unit enemy;
        private MoveAbility move;
        private ShootAbility shoot;
        private CoverFirstEnemyDecisionPolicy policy;

        [SetUp]
        public void Setup()
        {
            grid = CreateComponent<GridManager>();
            grid.Initialize(20, 10);
            enemy = CreateUnit("Enemy", Faction.Enemy);
            move = new MoveAbility(grid);
            shoot = new ShootAbility(grid, new ShootSettings());
            enemy.Abilities.Grant(move);
            enemy.Abilities.Grant(shoot);
            enemy.BeginTurn();
            policy = new CoverFirstEnemyDecisionPolicy();
        }

        [Test]
        public void ContextAndPlanAreImmutableAndRepeatedDecisionDoesNotMutateBattle()
        {
            Unit player = PlacePlayer("Player", new Vector2Int(8, 2));
            PlaceEnemy(new Vector2Int(2, 2));
            grid.TrySetCover(new Vector2Int(2, 2), GridDirection.East, CoverType.HalfCover);
            var sourcePlayers = new List<Unit> { player };
            var sourceExcluded = new List<EnemyActionSignature>();
            var context = new EnemyDecisionContext(enemy, sourcePlayers, grid, sourceExcluded);
            sourcePlayers.Clear();
            int revision = grid.Revision;
            float ap = enemy.Attributes.GetValue(AttributeType.ActionPoints);
            Vector3 position = enemy.transform.position;

            EnemyActionPlan first = policy.Decide(context);
            sourceExcluded.Add(first.GetSignature());
            EnemyActionPlan second = policy.Decide(context);

            Assert.That(context.Players.Count, Is.EqualTo(1));
            Assert.That(first.Kind, Is.EqualTo(EnemyActionKind.Shoot));
            Assert.That(second.Kind, Is.EqualTo(first.Kind));
            Assert.That(second.Target, Is.SameAs(first.Target));
            Assert.That(typeof(EnemyActionPlan).GetProperties().All(property => !property.CanWrite), Is.True);
            Assert.That(typeof(EnemyDecisionContext).GetProperties().All(property => !property.CanWrite), Is.True);
            Assert.That(grid.Revision, Is.EqualTo(revision));
            Assert.That(enemy.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(ap));
            Assert.That(enemy.transform.position, Is.EqualTo(position));
            Assert.That(grid.TryGetUnitCoordinates(enemy, out Vector2Int occupied), Is.True);
            Assert.That(occupied, Is.EqualTo(new Vector2Int(2, 2)));
        }

        [Test]
        public void CurrentEffectiveCoverShootsWithoutMoving()
        {
            Unit player = PlacePlayer("Player", new Vector2Int(8, 2));
            PlaceEnemy(new Vector2Int(2, 2));
            grid.TrySetCover(new Vector2Int(2, 2), GridDirection.East, CoverType.HalfCover);

            EnemyActionPlan plan = Decide(player);

            Assert.That(plan.Kind, Is.EqualTo(EnemyActionKind.Shoot));
            Assert.That(plan.Target, Is.SameAs(player));
        }

        [Test]
        public void EffectiveReachableCoverIsChosenBeforeCurrentShot()
        {
            Unit player = PlacePlayer("Player", new Vector2Int(8, 2));
            PlaceEnemy(new Vector2Int(2, 2));
            Vector2Int covered = new Vector2Int(3, 2);
            grid.TrySetCover(covered, GridDirection.East, CoverType.FullCover);

            EnemyActionPlan plan = Decide(player);

            Assert.That(plan.Kind, Is.EqualTo(EnemyActionKind.Move));
            Assert.That(plan.Destination, Is.EqualTo(covered));
        }

        [Test]
        public void WrongDirectionCoverDoesNotReplaceCurrentLegalShot()
        {
            Unit player = PlacePlayer("Player", new Vector2Int(8, 2));
            PlaceEnemy(new Vector2Int(2, 2));
            grid.TrySetCover(new Vector2Int(3, 2), GridDirection.West, CoverType.FullCover);

            EnemyActionPlan plan = Decide(player);

            Assert.That(plan.Kind, Is.EqualTo(EnemyActionKind.Shoot));
            Assert.That(plan.Target, Is.SameAs(player));
        }

        [Test]
        public void CoveredCellWithoutLegalShotIsNotACoverCandidate()
        {
            PlaceEnemy(new Vector2Int(1, 1));
            Unit player = PlacePlayer("Player", new Vector2Int(6, 1));
            enemy.Attributes.SetBaseValue(AttributeType.Mobility, 1f);
            grid.TrySetBlockState(new Vector2Int(0, 1), BlockState.FullBlocked);
            grid.TrySetBlockState(new Vector2Int(2, 1), BlockState.FullBlocked);
            grid.TrySetBlockState(new Vector2Int(1, 0), BlockState.FullBlocked);
            for (int y = 0; y < grid.Height; y++)
                grid.TrySetBlockState(new Vector2Int(3, y), BlockState.FullBlocked);
            Vector2Int onlyReachable = new Vector2Int(1, 2);
            grid.TrySetCover(onlyReachable, GridDirection.East, CoverType.FullCover);

            EnemyActionPlan plan = Decide(player);

            Assert.That(plan.Kind, Is.EqualTo(EnemyActionKind.None));
        }

        [Test]
        public void OneActionPointCannotMoveForCoverWhenCurrentShotIsLegal()
        {
            Unit player = PlacePlayer("Player", new Vector2Int(8, 2));
            PlaceEnemy(new Vector2Int(2, 2));
            grid.TrySetCover(new Vector2Int(3, 2), GridDirection.East, CoverType.FullCover);
            enemy.Attributes.SetBaseValue(AttributeType.ActionPoints, 1f);

            EnemyActionPlan plan = Decide(player);

            Assert.That(plan.Kind, Is.EqualTo(EnemyActionKind.Shoot));
        }

        [Test]
        public void OccupiedCoveredCellIsExcluded()
        {
            Unit player = PlacePlayer("Player", new Vector2Int(8, 2));
            PlaceEnemy(new Vector2Int(2, 2));
            Vector2Int occupied = new Vector2Int(3, 2);
            Unit ally = CreateUnit("Ally", Faction.Enemy);
            Assert.That(grid.TryPlaceUnit(ally, occupied), Is.True);
            grid.TrySetCover(occupied, GridDirection.East, CoverType.FullCover);

            EnemyActionPlan plan = Decide(player);

            Assert.That(plan.Kind, Is.EqualTo(EnemyActionKind.Shoot));
            Assert.That(plan.Target, Is.SameAs(player));
        }

        [Test]
        public void FullCoverOutranksHigherChanceHalfCover()
        {
            Unit player = PlacePlayer("Player", new Vector2Int(12, 2));
            PlaceEnemy(new Vector2Int(2, 2));
            Vector2Int closerHalfCover = new Vector2Int(3, 2);
            Vector2Int fartherFullCover = new Vector2Int(2, 3);
            grid.TrySetCover(closerHalfCover, GridDirection.East, CoverType.HalfCover);
            grid.TrySetCover(fartherFullCover, GridDirection.East, CoverType.FullCover);

            EnemyActionPlan plan = Decide(player);

            Assert.That(plan.Kind, Is.EqualTo(EnemyActionKind.Move));
            Assert.That(plan.Destination, Is.EqualTo(fartherFullCover));
        }

        [Test]
        public void EqualCoverChoosesHigherFinalHitChanceTarget()
        {
            PlaceEnemy(new Vector2Int(5, 5));
            Unit defended = PlacePlayer("Defended", new Vector2Int(9, 5));
            Unit exposed = PlacePlayer("Exposed", new Vector2Int(9, 6));
            defended.Attributes.SetBaseValue(AttributeType.Defense, 40f);
            exposed.Attributes.SetBaseValue(AttributeType.Defense, 0f);
            grid.TrySetCover(new Vector2Int(5, 5), GridDirection.East, CoverType.HalfCover);

            EnemyActionPlan plan = Decide(defended, exposed);

            Assert.That(plan.Kind, Is.EqualTo(EnemyActionKind.Shoot));
            Assert.That(plan.Target, Is.SameAs(exposed));
            defended.Attributes.SetBaseValue(AttributeType.Defense, 0f);
            Assert.That(Decide(defended, exposed).Target, Is.SameAs(defended));
        }

        [Test]
        public void FullyTiedCoverCandidatesUseLowerCoordinatesDeterministically()
        {
            Unit player = PlacePlayer("Player", new Vector2Int(8, 2));
            PlaceEnemy(new Vector2Int(2, 2));
            Vector2Int lower = new Vector2Int(2, 1);
            Vector2Int upper = new Vector2Int(2, 3);
            grid.TrySetCover(lower, GridDirection.East, CoverType.HalfCover);
            grid.TrySetCover(upper, GridDirection.East, CoverType.HalfCover);

            EnemyActionPlan first = Decide(player);
            EnemyActionPlan second = Decide(player);

            Assert.That(first.Kind, Is.EqualTo(EnemyActionKind.Move));
            Assert.That(first.Destination, Is.EqualTo(lower));
            Assert.That(second.Destination, Is.EqualTo(lower));
        }

        [Test]
        public void ZeroPercentLegalShotIsStillChosen()
        {
            Unit player = PlacePlayer("Player", new Vector2Int(8, 2));
            PlaceEnemy(new Vector2Int(2, 2));
            enemy.Attributes.SetBaseValue(AttributeType.Aim, 0f);
            player.Attributes.SetBaseValue(AttributeType.Defense, 100f);

            EnemyActionPlan plan = Decide(player);

            Assert.That(plan.Kind, Is.EqualTo(EnemyActionKind.Shoot));
            Assert.That(plan.Target, Is.SameAs(player));
        }

        [Test]
        public void CurrentShotUsesHighestChanceThenRegistrationOrder()
        {
            PlaceEnemy(new Vector2Int(5, 5));
            Unit first = PlacePlayer("First", new Vector2Int(9, 5));
            Unit second = PlacePlayer("Second", new Vector2Int(1, 5));
            first.Attributes.SetBaseValue(AttributeType.Defense, 20f);
            second.Attributes.SetBaseValue(AttributeType.Defense, 0f);

            Assert.That(Decide(first, second).Target, Is.SameAs(second));
            first.Attributes.SetBaseValue(AttributeType.Defense, 0f);
            Assert.That(Decide(first, second).Target, Is.SameAs(first));
            Assert.That(Decide(second, first).Target, Is.SameAs(second));
        }

        [Test]
        public void NonCoverCellThatEstablishesShotIsPreferred()
        {
            PlaceEnemy(new Vector2Int(1, 1));
            Unit player = PlacePlayer("Player", new Vector2Int(8, 1));
            enemy.Attributes.SetBaseValue(AttributeType.Mobility, 1f);
            enemy.Attributes.SetBaseValue(AttributeType.SightRange, 6f);

            EnemyActionPlan plan = Decide(player);

            Assert.That(plan.Kind, Is.EqualTo(EnemyActionKind.Move));
            Assert.That(plan.Destination, Is.EqualTo(new Vector2Int(2, 1)));
        }

        [Test]
        public void ApproachCanAdvanceTwiceOneActionPointAtATime()
        {
            PlaceEnemy(new Vector2Int(1, 1));
            Unit player = PlacePlayer("Player", new Vector2Int(10, 1));
            enemy.Attributes.SetBaseValue(AttributeType.Mobility, 2f);
            enemy.Attributes.SetBaseValue(AttributeType.SightRange, 2f);

            EnemyActionPlan first = Decide(player);
            Assert.That(first.Kind, Is.EqualTo(EnemyActionKind.Move));
            Assert.That(first.Destination, Is.EqualTo(new Vector2Int(3, 1)));
            Assert.That(enemy.Abilities.TryActivate(move,
                AbilityTarget.ForCell(first.Destination), out _), Is.True);

            EnemyActionPlan second = Decide(player);
            Assert.That(second.Kind, Is.EqualTo(EnemyActionKind.Move));
            Assert.That(second.Destination, Is.EqualTo(new Vector2Int(5, 1)));
        }

        [Test]
        public void NoStrictDistanceImprovementReturnsNoneInsteadOfOscillating()
        {
            PlaceEnemy(new Vector2Int(1, 1));
            Unit player = PlacePlayer("Player", new Vector2Int(8, 1));
            enemy.Attributes.SetBaseValue(AttributeType.Mobility, 1f);
            enemy.Attributes.SetBaseValue(AttributeType.SightRange, 0f);
            grid.TrySetBlockState(new Vector2Int(2, 1), BlockState.FullBlocked);

            EnemyActionPlan plan = Decide(player);

            Assert.That(plan.Kind, Is.EqualTo(EnemyActionKind.None));
        }

        [Test]
        public void ExcludedFailedPlanSelectsAnotherStableCandidate()
        {
            Unit player = PlacePlayer("Player", new Vector2Int(8, 2));
            PlaceEnemy(new Vector2Int(2, 2));
            Vector2Int lower = new Vector2Int(2, 1);
            Vector2Int upper = new Vector2Int(2, 3);
            grid.TrySetCover(lower, GridDirection.East, CoverType.HalfCover);
            grid.TrySetCover(upper, GridDirection.East, CoverType.HalfCover);
            EnemyActionPlan first = Decide(player);
            var context = new EnemyDecisionContext(enemy, new[] { player }, grid,
                new[] { first.GetSignature() });

            EnemyActionPlan next = policy.Decide(context);

            Assert.That(next.Kind, Is.EqualTo(EnemyActionKind.Move));
            Assert.That(next.Destination, Is.EqualTo(upper));
        }

        private void PlaceEnemy(Vector2Int coordinates)
        {
            Assert.That(grid.TryPlaceUnit(enemy, coordinates), Is.True);
        }

        private Unit PlacePlayer(string name, Vector2Int coordinates)
        {
            Unit player = CreateUnit(name, Faction.Player);
            Assert.That(grid.TryPlaceUnit(player, coordinates), Is.True);
            return player;
        }

        private EnemyActionPlan Decide(params Unit[] players) =>
            policy.Decide(new EnemyDecisionContext(enemy, players, grid));
    }
}
