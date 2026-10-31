using NUnit.Framework;
using UnityEngine;
using Wargame.Battle;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class ShootRulesTests : UnitTestBase
    {
        [TestCase(65f, 0f, 10f, 75f)]
        [TestCase(20f, 45f, 20f, 0f)]
        [TestCase(100f, 0f, 20f, 100f)]
        [TestCase(150f, 0f, -60f, 90f)]
        public void HitChanceAddsDistanceBeforeClamping(float aim, float defense, float modifier, float expected)
        {
            Assert.That(ShootRules.GetHitChance(aim, defense, modifier), Is.EqualTo(expected));
        }

        [TestCase(0, 0, 0)]
        [TestCase(1, 1, 1)]
        [TestCase(3, 4, 5)]
        [TestCase(-3, -4, 5)]
        [TestCase(7, 7, 9)]
        [TestCase(5000, 1, 5000)]
        public void DistanceFloorsEuclideanLogicalCoordinates(int dx, int dy, int expected)
        {
            var start = new Vector2Int(10, 10);
            var end = start + new Vector2Int(dx, dy);
            Assert.That(ShootRules.GetDistance(start, end), Is.EqualTo(expected));
            Assert.That(ShootRules.GetDistance(end, start), Is.EqualTo(expected));
        }

        [TestCase(5, 60)]
        [TestCase(10, 50)]
        [TestCase(15, 40)]
        public void DefaultCurveUsesTenCellBaseline(int distance, float expected)
        {
            var settings = new ShootSettings().CreateSnapshot();
            Assert.That(ShootRules.GetHitChance(70, 20, settings.GetHitChanceModifier(distance)), Is.EqualTo(expected));
        }

        [TestCase(4, 0f, 4)]
        [TestCase(4, 1f, 3)]
        [TestCase(2, 3f, 0)]
        public void ArmorReducesDamageWithoutGoingBelowZero(int damage, float armor, float expected)
        {
            Assert.That(ShootRules.GetDamage(damage, armor), Is.EqualTo(expected));
        }

    }
}
