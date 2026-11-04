using NUnit.Framework;
using UnityEngine;
using Wargame.Battle;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class ShotEvaluatorTests : UnitTestBase
    {
        [Test]
        public void HypotheticalCoordinateUsesSharedRulesWithoutMutatingBattleState()
        {
            GridManager grid = CreateComponent<GridManager>();
            grid.Initialize(12, 8);
            Unit attacker = CreateUnit("Attacker", Faction.Enemy);
            Unit target = CreateUnit("Target", Faction.Player);
            Assert.That(grid.TryPlaceUnit(attacker, Vector2Int.zero), Is.True);
            Assert.That(grid.TryPlaceUnit(target, new Vector2Int(6, 0)), Is.True);
            attacker.Attributes.SetBaseValue(AttributeType.Aim, 70f);
            target.Attributes.SetBaseValue(AttributeType.Defense, 20f);
            target.Buffs.TryApply(new CoverBuff(GridDirection.West, CoverType.HalfCover, 20f));
            var evaluator = new ShotEvaluator(grid, new ShootSettings().CreateSnapshot());
            int revision = grid.Revision;
            Vector3 attackerPosition = attacker.transform.position;
            Vector3 targetPosition = target.transform.position;
            float actionPoints = attacker.Attributes.GetValue(AttributeType.ActionPoints);
            int attackerBuffs = attacker.Buffs.Buffs.Count;
            int targetBuffs = target.Buffs.Buffs.Count;
            int changed = 0;
            grid.Changed += () => changed++;

            bool success = evaluator.TryEvaluate(attacker, new Vector2Int(2, 0), target,
                out ShotEvaluation evaluation);

            Assert.That(success, Is.True);
            Assert.That(evaluation.Distance, Is.EqualTo(4));
            Assert.That(evaluation.FiringOrigin, Is.EqualTo(new Vector2Int(2, 0)));
            Assert.That(evaluation.Breakdown.BaseChance, Is.EqualTo(70f));
            Assert.That(evaluation.Breakdown.FinalChance, Is.EqualTo(50f));
            Assert.That(grid.Revision, Is.EqualTo(revision));
            Assert.That(changed, Is.Zero);
            Assert.That(grid.TryGetUnitCoordinates(attacker, out Vector2Int attackerCell), Is.True);
            Assert.That(grid.TryGetUnitCoordinates(target, out Vector2Int targetCell), Is.True);
            Assert.That(attackerCell, Is.EqualTo(Vector2Int.zero));
            Assert.That(targetCell, Is.EqualTo(new Vector2Int(6, 0)));
            Assert.That(attacker.transform.position, Is.EqualTo(attackerPosition));
            Assert.That(target.transform.position, Is.EqualTo(targetPosition));
            Assert.That(attacker.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(actionPoints));
            Assert.That(attacker.Buffs.Buffs.Count, Is.EqualTo(attackerBuffs));
            Assert.That(target.Buffs.Buffs.Count, Is.EqualTo(targetBuffs));
        }

        [Test]
        public void HypotheticalCoordinateStillRequiresRegisteredUnitsAndEmptyCandidateCell()
        {
            GridManager grid = CreateComponent<GridManager>();
            grid.Initialize(8, 8);
            Unit attacker = CreateUnit("Attacker", Faction.Enemy);
            Unit target = CreateUnit("Target", Faction.Player);
            Unit blocker = CreateUnit("Blocker", Faction.Enemy);
            grid.TryPlaceUnit(attacker, Vector2Int.zero);
            grid.TryPlaceUnit(target, new Vector2Int(5, 0));
            grid.TryPlaceUnit(blocker, new Vector2Int(2, 0));
            var evaluator = new ShotEvaluator(grid, new ShootSettings().CreateSnapshot());

            Assert.That(evaluator.TryEvaluate(attacker, new Vector2Int(2, 0), target, out _), Is.False);
            grid.TryRemoveUnit(target);
            Assert.That(evaluator.TryEvaluate(attacker, Vector2Int.right, target, out _), Is.False);
        }
    }
}
