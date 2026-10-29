using NUnit.Framework;
using UnityEngine;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class MoveAbilityTests : UnitTestBase
    {
        private GridManager grid;
        private Unit unit;
        private MoveAbility move;

        [SetUp]
        public void Setup()
        {
            grid = CreateComponent<GridManager>();
            if (!grid.IsInitialized) grid.Initialize(20, 1);
            unit = CreateUnit();
            Assert.That(grid.TryPlaceUnit(unit, Vector2Int.zero), Is.True);
            move = new MoveAbility(grid);
            unit.Abilities.Grant(move);
            unit.BeginTurn();
        }

        [TestCase(6, 1, true)]
        [TestCase(7, 2, true)]
        [TestCase(12, 2, true)]
        [TestCase(13, 0, false)]
        public void CostBoundariesAndPreviewArePure(int x, int cost, bool valid)
        {
            Assert.That(move.TryGetMovePlan(new Vector2Int(x, 0), out MovePlan plan, out _), Is.EqualTo(valid));
            if (valid)
            {
                Assert.That(plan.ActionPointCost, Is.EqualTo(cost));
                Assert.That(plan.TotalCost, Is.EqualTo(x));
                Assert.That(plan.Path.Count, Is.EqualTo(x + 1));
            }
            Assert.That(unit.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Assert.That(grid.TryGetUnitCoordinates(unit, out var start), Is.True);
            Assert.That(start, Is.EqualTo(Vector2Int.zero));
        }

        [Test]
        public void OneApCannotDashAndMobilityChangesRange()
        {
            unit.Attributes.SetBaseValue(AttributeType.ActionPoints, 1);
            Assert.That(move.TryGetMovePlan(new Vector2Int(7, 0), out _, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.NotEnoughActionPoints));
            Assert.That(move.GetReachableCells().ReachableCosts.Count, Is.EqualTo(6));
            unit.Attributes.SetBaseValue(AttributeType.Mobility, 3.9f);
            Assert.That(move.BudgetPerActionPoint, Is.EqualTo(3));
            Assert.That(move.TryGetMovePlan(new Vector2Int(4, 0), out _, out _), Is.False);
            unit.Attributes.SetBaseValue(AttributeType.Mobility, 0);
            Assert.That(move.TryGetMovePlan(Vector2Int.right, out _, out _), Is.False);
        }

        [Test]
        public void CommitMovesOnceAndDeductsOnce()
        {
            Assert.That(unit.Abilities.TryActivate(move, AbilityTarget.ForCell(new Vector2Int(7, 0)), out _), Is.True);
            Assert.That(unit.Attributes.GetValue(AttributeType.ActionPoints), Is.Zero);
            Assert.That(grid.TryGetUnitCoordinates(unit, out var cell), Is.True);
            Assert.That(cell, Is.EqualTo(new Vector2Int(7, 0)));
            Assert.That(unit.transform.position, Is.EqualTo(grid.GridToWorld(cell)));
            Assert.That(move.IsActive, Is.False);
            Assert.That(unit.Tags.HasTag(GameplayTag.Moving), Is.False);
            Assert.That(unit.Abilities.TryActivate(move, AbilityTarget.ForCell(new Vector2Int(8, 0)), out _), Is.False);
            Assert.That(unit.Attributes.GetValue(AttributeType.ActionPoints), Is.Zero);
        }

        [Test]
        public void StalePreviewAndInvalidTargetsDoNotSpendAp()
        {
            Assert.That(move.TryGetMovePlan(Vector2Int.right, out _, out _), Is.True);
            grid.TrySetBlockState(Vector2Int.right, BlockState.HalfBlocked);
            Assert.That(unit.Abilities.TryActivate(move, AbilityTarget.ForCell(Vector2Int.right), out _), Is.False);
            Assert.That(unit.Abilities.TryActivate(move, AbilityTarget.ForCell(Vector2Int.zero), out _), Is.False);
            Assert.That(unit.Abilities.TryActivate(move, AbilityTarget.ForCell(new Vector2Int(-1, 0)), out _), Is.False);
            Assert.That(unit.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
        }

        [TestCase(GameplayTag.Bound)]
        [TestCase(GameplayTag.Stunned)]
        [TestCase(GameplayTag.Unconscious)]
        [TestCase(GameplayTag.Dead)]
        public void StatusPreventsMovement(GameplayTag tag)
        {
            unit.Tags.AddTag(tag);
            Assert.That(unit.Abilities.TryActivate(move, AbilityTarget.ForCell(Vector2Int.right), out var reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.BlockedByTag));
            Assert.That(unit.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
        }

        [Test]
        public void WeightedPathAndOccupiedTraversalUseTheSameRules()
        {
            grid.TrySetMoveCost(Vector2Int.right, 6);
            Assert.That(move.TryGetMovePlan(new Vector2Int(2, 0), out var plan, out _), Is.True);
            Assert.That(plan.TotalCost, Is.EqualTo(7));
            Assert.That(plan.ActionPointCost, Is.EqualTo(2));
            grid.TrySetMoveCost(Vector2Int.right, 1);
            Unit ally = CreateUnit("Ally");
            grid.TryPlaceUnit(ally, Vector2Int.right);
            Assert.That(move.TryGetMovePlan(new Vector2Int(2, 0), out _, out _), Is.True);
            Assert.That(move.TryGetMovePlan(Vector2Int.right, out _, out _), Is.False);
            ally.Faction = Faction.Enemy;
            Assert.That(move.TryGetMovePlan(new Vector2Int(2, 0), out _, out _), Is.False);
            grid.TryRemoveUnit(ally);
            grid.TrySetBlockState(Vector2Int.right, BlockState.HalfBlocked);
            Assert.That(move.TryGetMovePlan(new Vector2Int(2, 0), out plan, out _), Is.True);
            Assert.That(plan.Path[1], Is.EqualTo(Vector2Int.right));
            Assert.That(move.TryGetMovePlan(Vector2Int.right, out _, out _), Is.False);
        }
    }
}
