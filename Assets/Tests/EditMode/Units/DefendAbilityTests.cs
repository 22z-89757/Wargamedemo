using NUnit.Framework;
using UnityEngine;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class DefendAbilityTests : UnitTestBase
    {
        [Test]
        public void ActivationAppliesCrouchModifiersAndConsumesOneActionPoint()
        {
            Unit unit = CreateUnit();
            var ability = new DefendAbility();
            unit.Abilities.Grant(ability);
            unit.BeginTurn();

            Assert.That(unit.Abilities.TryActivate(ability, AbilityTarget.Self(unit), out _), Is.True);

            Assert.That(unit.Attributes.GetValue(AttributeType.Dodge), Is.EqualTo(20f));
            Assert.That(unit.Attributes.GetValue(AttributeType.Defense), Is.EqualTo(10f));
            Assert.That(unit.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1f));
            Assert.That(unit.Tags.HasTag(GameplayTag.HunkerDown), Is.True);
            Assert.That(unit.Buffs.HasBuff(CrouchBuff.Id), Is.True);
            Assert.That(ability.IsActive, Is.False);
        }

        [Test]
        public void CrouchBuffPreventsRepeatedActivationAndMovementRemovesItsModifiers()
        {
            GridManager grid = CreateComponent<GridManager>();
            grid.Initialize(4, 1);
            Unit unit = CreateUnit();
            Assert.That(grid.TryPlaceUnit(unit, Vector2Int.zero), Is.True);
            var move = new MoveAbility(grid);
            var defend = new DefendAbility();
            unit.Abilities.Grant(move);
            unit.Abilities.Grant(defend);
            unit.BeginTurn();

            Assert.That(unit.Abilities.TryActivate(defend, AbilityTarget.Self(unit), out _), Is.True);
            Assert.That(unit.Abilities.TryActivate(defend, AbilityTarget.Self(unit), out AbilityFailReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.BlockedByTag));
            Assert.That(unit.Abilities.TryActivate(move, AbilityTarget.ForCell(Vector2Int.right), out _), Is.True);

            Assert.That(unit.Buffs.HasBuff(CrouchBuff.Id), Is.False);
            Assert.That(unit.Tags.HasTag(GameplayTag.HunkerDown), Is.False);
            Assert.That(unit.Attributes.GetValue(AttributeType.Dodge), Is.Zero);
            Assert.That(unit.Attributes.GetValue(AttributeType.Defense), Is.Zero);
        }
    }
}
