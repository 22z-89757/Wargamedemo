using NUnit.Framework;
using UnityEngine;
using Wargame.Battle;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class ShootAbilityTests : UnitTestBase
    {
        private GridManager grid;
        private Unit shooter;
        private Unit target;
        private FixedShootRandomSource random;
        private ShootAbility shoot;

        [SetUp]
        public void Setup()
        {
            grid = CreateComponent<GridManager>();
            grid.Initialize(32, 12);
            shooter = CreateUnit("Shooter", Faction.Player);
            target = CreateUnit("Target", Faction.Enemy);
            Assert.That(grid.TryPlaceUnit(shooter, Vector2Int.zero), Is.True);
            Assert.That(grid.TryPlaceUnit(target, new Vector2Int(6, 0)), Is.True);
            random = new FixedShootRandomSource(0.1f, 0.9f);
            shoot = new ShootAbility(grid, new ShootSettings(), random);
            shooter.Abilities.Grant(shoot);
            shooter.BeginTurn();
        }

        [Test]
        public void PreviewUsesAimAndDefenseAndDoesNotChangeHealthOrAp()
        {
            shooter.Attributes.SetBaseValue(AttributeType.Aim, 72f);
            target.Attributes.SetBaseValue(AttributeType.Defense, 17f);
            float health = target.Attributes.GetValue(AttributeType.Health);
            float ap = shooter.Attributes.GetValue(AttributeType.ActionPoints);

            ShootPreview preview = shoot.GetPreview(target);

            Assert.That(preview.IsAvailable, Is.True);
            Assert.That(preview.TargetName, Is.EqualTo("Target"));
            Assert.That(preview.Distance, Is.EqualTo(6));
            Assert.That(preview.HitChance, Is.EqualTo(65f));
            Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(health));
            Assert.That(shooter.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(ap));
        }

        [Test]
        public void RejectsIllegalTargetsWithoutSpendingAp()
        {
            Unit ally = CreateUnit("Ally", Faction.Player);
            grid.TryPlaceUnit(ally, new Vector2Int(1, 1));
            Unit tooFar = CreateUnit("TooFar", Faction.Enemy);
            grid.TryPlaceUnit(tooFar, new Vector2Int(13, 0));
            Unit unregistered = CreateUnit("Unregistered", Faction.Enemy);
            Unit neutral = CreateUnit("Neutral", Faction.Neutral);
            grid.TryPlaceUnit(neutral, new Vector2Int(1, 0));
            Unit dead = CreateUnit("Dead", Faction.Enemy);
            grid.TryPlaceUnit(dead, new Vector2Int(5, 1));
            dead.Kill();
            float ap = shooter.Attributes.GetValue(AttributeType.ActionPoints);

            Assert.That(shooter.Abilities.CanActivate(shoot, AbilityTarget.ForUnit(shooter), out _), Is.False);
            Assert.That(shooter.Abilities.CanActivate(shoot, AbilityTarget.ForUnit(ally), out _), Is.False);
            Assert.That(shooter.Abilities.CanActivate(shoot, AbilityTarget.ForUnit(tooFar), out _), Is.False);
            Assert.That(shooter.Abilities.CanActivate(shoot, AbilityTarget.ForUnit(unregistered), out _), Is.False);
            Assert.That(shooter.Abilities.CanActivate(shoot, AbilityTarget.ForUnit(dead), out _), Is.False);
            foreach (Unit invalid in new[] { shooter, ally, tooFar, unregistered, dead, neutral, null })
            {
                Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(invalid), out _), Is.False);
                Assert.That(shooter.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(ap));
            }
            Assert.That(shooter.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(ap));

            grid.TrySetBlockState(new Vector2Int(3, 0), BlockState.FullBlocked);
            Assert.That(shooter.Abilities.CanActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.False);
            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.False);
            Assert.That(shooter.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(ap));
        }

        [Test]
        public void PreviewCannotBypassTargetInvalidationOrTurnEnd()
        {
            Assert.That(shoot.GetPreview(target).CanFire, Is.True, "当前目标处于视野内。");
            grid.TryRemoveUnit(target);
            float ap = shooter.Attributes.GetValue(AttributeType.ActionPoints);
            float health = target.Attributes.GetValue(AttributeType.Health);
            Assert.That(shoot.GetPreview(target).IsAvailable, Is.False);
            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.False);
            Assert.That(shooter.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(ap));
            Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(health));

            grid.TryPlaceUnit(target, new Vector2Int(6, 0));
            shooter.EndTurn();
            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out AbilityFailReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.NotYourTurn));
            Assert.That(shooter.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(ap));
        }

        [Test]
        public void InsufficientApAndRandomFailureDoNotPartiallyCommit()
        {
            shooter.Attributes.SetBaseValue(AttributeType.Aim, 100f);
            shooter.Attributes.SetBaseValue(AttributeType.ActionPoints, 0f);
            float health = target.Attributes.GetValue(AttributeType.Health);

            ShootPreview preview = shoot.GetPreview(target);
            Assert.That(preview.IsAvailable, Is.True);
            Assert.That(preview.CanFire, Is.False);
            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out AbilityFailReason apReason), Is.False);
            Assert.That(apReason, Is.EqualTo(AbilityFailReason.NotEnoughActionPoints));
            Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(health));

            shooter.Attributes.SetBaseValue(AttributeType.ActionPoints, 2f);
            var throwingAbility = new ShootAbility(grid, new ShootSettings(), new ThrowingShootRandomSource());
            shooter.Abilities.Remove(shoot);
            shooter.Abilities.Grant(throwingAbility);
            float ap = shooter.Attributes.GetValue(AttributeType.ActionPoints);

            Assert.That(shooter.Abilities.TryActivate(throwingAbility, AbilityTarget.ForUnit(target), out AbilityFailReason randomReason), Is.False);
            Assert.That(randomReason, Is.EqualTo(AbilityFailReason.Custom));
            Assert.That(shooter.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(ap));
            Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(health));
            Assert.That(throwingAbility.IsActive, Is.False);
        }

        [Test]
        public void ShotHitsWithArmorAndConsumesOneAp()
        {
            shooter.Attributes.SetBaseValue(AttributeType.Aim, 100f);
            target.Attributes.SetBaseValue(AttributeType.Armor, 1.5f);
            target.Attributes.SetBaseValue(AttributeType.Dodge, 0f);

            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.True);

            Assert.That(shoot.LastResult.Hit, Is.True);
            Assert.That(shoot.LastResult.Grazing, Is.False);
            Assert.That(shoot.LastResult.Damage, Is.EqualTo(2.5f));
            Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(7.5f));
            Assert.That(shooter.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1f));
            Assert.That(shoot.IsActive, Is.False);
        }

        [Test]
        public void DodgeCausesGrazingAndMissDealsNoDamage()
        {
            shooter.Attributes.SetBaseValue(AttributeType.Aim, 100f);
            target.Attributes.SetBaseValue(AttributeType.Dodge, 100f);
            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.True);
            Assert.That(shoot.LastResult.Grazing, Is.True);
            Assert.That(shoot.LastResult.Damage, Is.EqualTo(2f));
            Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(8f));

            shooter.Attributes.SetBaseValue(AttributeType.Aim, 0f);
            target.Attributes.SetBaseValue(AttributeType.Defense, 100f);
            random.Reset(0.1f);
            float health = target.Attributes.GetValue(AttributeType.Health);
            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.True);
            Assert.That(shoot.LastResult.Hit, Is.False);
            Assert.That(shoot.LastResult.Damage, Is.Zero);
            Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(health));
        }

        [Test]
        public void LethalShotUsesUnitDeathAndGridCleanupEvents()
        {
            shooter.Attributes.SetBaseValue(AttributeType.Aim, 100f);
            target.Attributes.SetBaseValue(AttributeType.Health, 4f);
            target.Attributes.SetBaseValue(AttributeType.Armor, 0f);
            bool died = false;
            target.Died += _ =>
            {
                died = true;
                grid.TryRemoveUnit(target);
            };

            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.True);

            Assert.That(died, Is.True);
            Assert.That(target.IsAlive, Is.False);
            Assert.That(grid.TryGetUnitCoordinates(target, out _), Is.False);
        }

        [TestCase(1, 1, 1, true)]
        [TestCase(3, 4, 5, true)]
        [TestCase(3, 4, 4, false)]
        [TestCase(10, 0, 12, true)]
        [TestCase(12, 0, 12, true)]
        [TestCase(13, 0, 12, false)]
        [TestCase(25, 0, 30, true)]
        [TestCase(1, 1, -1, false)]
        public void SightRangeUsesFlooredEuclideanDistanceWithoutWeaponLimit(int x, int y, float sight, bool expected)
        {
            Assert.That(grid.TryMoveUnit(target, new Vector2Int(x, y)), Is.True);
            shooter.Attributes.SetBaseValue(AttributeType.SightRange, sight);
            Assert.That(shoot.GetPreview(target).IsAvailable, Is.EqualTo(expected));
            Assert.That(shoot.GetPreview(target).CanFire, Is.EqualTo(expected));
            if (!expected)
            {
                Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.False);
                Assert.That(shooter.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
                Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(10));
            }
        }

        [Test]
        public void SightRangeDecreaseInvalidatesAnExistingPreview()
        {
            grid.TryMoveUnit(target, new Vector2Int(10, 0));
            Assert.That(shoot.GetPreview(target).CanFire, Is.True);
            shooter.Attributes.SetBaseValue(AttributeType.SightRange, 9);
            Assert.That(shoot.GetPreview(target).IsAvailable, Is.False);
            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.False);
            Assert.That(shooter.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(10));
        }

        [Test]
        public void ZeroChanceTargetCanFireButAlwaysMisses()
        {
            shooter.Attributes.SetBaseValue(AttributeType.Aim, 0);
            target.Attributes.SetBaseValue(AttributeType.Defense, 100);
            random.Reset(0);
            ShootPreview preview = shoot.GetPreview(target);
            Assert.That(preview.IsAvailable && preview.CanFire, Is.True);
            Assert.That(preview.HitChance, Is.Zero);
            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.True);
            Assert.That(shoot.LastResult.Hit, Is.False);
            Assert.That(shoot.LastResult.HitChance, Is.Zero);
            Assert.That(shoot.LastResult.Distance, Is.EqualTo(preview.Distance));
            Assert.That(shooter.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1));
            Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(10));
        }

        [Test]
        public void PreviewAndResultUseInjectedSettingsAndKeepHistoricalSnapshot()
        {
            var settings = new ShootSettings { DistanceHitChanceModifier = AnimationCurve.Constant(0, 20, 5) };
            shooter.Abilities.Remove(shoot);
            shoot = new ShootAbility(grid, settings, random);
            shooter.Abilities.Grant(shoot);
            settings.DistanceHitChanceModifier = AnimationCurve.Constant(0, 20, 99);
            shooter.Attributes.SetBaseValue(AttributeType.Aim, 70);
            target.Attributes.SetBaseValue(AttributeType.Defense, 20);
            ShootPreview preview = shoot.GetPreview(target);
            Assert.That(preview.HitChance, Is.EqualTo(55));
            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.True);
            Assert.That(shoot.LastResult.Distance, Is.EqualTo(preview.Distance));
            Assert.That(shoot.LastResult.HitChance, Is.EqualTo(preview.HitChance));
            grid.TryMoveUnit(target, new Vector2Int(10, 0));
            target.Attributes.SetBaseValue(AttributeType.Defense, 40);
            Assert.That(shoot.LastResult.Distance, Is.EqualTo(6));
            Assert.That(shoot.LastResult.HitChance, Is.EqualTo(55));
        }

        [Test]
        public void MovingAcrossStepAfterPreviewUsesCurrentDistanceAtCommit()
        {
            shooter.Attributes.SetBaseValue(AttributeType.Aim, 70);
            target.Attributes.SetBaseValue(AttributeType.Defense, 20);
            grid.TryMoveUnit(target, new Vector2Int(9, 0));
            Assert.That(shoot.GetPreview(target).HitChance, Is.EqualTo(60));
            grid.TryMoveUnit(target, new Vector2Int(10, 0));
            random.Reset(0.55f);
            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.True);
            Assert.That(shoot.LastResult.Distance, Is.EqualTo(10));
            Assert.That(shoot.LastResult.HitChance, Is.EqualTo(50));
            Assert.That(shoot.LastResult.Hit, Is.False, "必须使用新概率 50%，不能沿用旧预览 60%。");
        }

        [TestCase("move")]
        [TestCase("aim")]
        [TestCase("sight")]
        [TestCase("armor")]
        [TestCase("los")]
        [TestCase("turn")]
        public void InputsChangingDuringRandomPreparationDoNotSpendAp(string change)
        {
            shooter.Abilities.Remove(shoot);
            var source = new MutatingShootRandomSource(() =>
            {
                switch (change)
                {
                    case "move": grid.TryMoveUnit(target, new Vector2Int(10, 0)); break;
                    case "aim": shooter.Attributes.SetBaseValue(AttributeType.Aim, 90); break;
                    case "sight": shooter.Attributes.SetBaseValue(AttributeType.SightRange, 1); break;
                    case "armor": target.Attributes.SetBaseValue(AttributeType.Armor, 2); break;
                    case "los": grid.TrySetBlockState(new Vector2Int(3, 0), BlockState.FullBlocked); break;
                    case "turn": shooter.EndTurn(); break;
                }
            });
            shoot = new ShootAbility(grid, new ShootSettings(), source);
            shooter.Abilities.Grant(shoot);
            Assert.That(shooter.Abilities.TryActivate(shoot, AbilityTarget.ForUnit(target), out _), Is.False);
            Assert.That(shooter.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Assert.That(target.Attributes.GetValue(AttributeType.Health), Is.EqualTo(10));
            Assert.That(shoot.IsActive, Is.False);
            Assert.That(shooter.Abilities.ActiveExclusiveAbility, Is.Null);
        }

        private sealed class MutatingShootRandomSource : IShootRandomSource
        {
            private System.Action mutate;
            public MutatingShootRandomSource(System.Action mutate) => this.mutate = mutate;
            public float NextValue()
            {
                System.Action action = mutate;
                mutate = null;
                action?.Invoke();
                return 0;
            }
        }

        private sealed class FixedShootRandomSource : IShootRandomSource
        {
            private float[] values;
            private int index;
            public FixedShootRandomSource(params float[] values) => Reset(values);
            public float NextValue() => values[index++ % values.Length];
            public void Reset(params float[] nextValues) => (values, index) = (nextValues, 0);
        }

        private sealed class ThrowingShootRandomSource : IShootRandomSource
        {
            public float NextValue() => throw new System.InvalidOperationException("test random failure");
        }
    }
}
