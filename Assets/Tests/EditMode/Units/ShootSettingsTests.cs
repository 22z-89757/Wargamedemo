using System;
using NUnit.Framework;
using UnityEngine;
using Wargame.Battle;

namespace Wargame.Tests
{
    public sealed class ShootSettingsTests
    {
        [Test]
        public void DefaultGameAssetLoadsWithSteppedCurve()
        {
            var game = UnityEditor.AssetDatabase.LoadAssetAtPath<GameSettings>("Assets/Data/Game/GameSettings.asset");
            Assert.That(game, Is.Not.Null);
            Assert.That(game.TryValidate(out string error), Is.True, error);
            Assert.That(game.ShootSettings.DistanceHitChanceModifier.Evaluate(9), Is.EqualTo(10));
            Assert.That(game.ShootSettings.DistanceHitChanceModifier.Evaluate(10), Is.Zero);
            Assert.That(game.ShootSettings.DistanceHitChanceModifier.Evaluate(25), Is.EqualTo(-20));
        }

        [TestCase(-1, 20)]
        [TestCase(0, 20)]
        [TestCase(4, 20)]
        [TestCase(5, 10)]
        [TestCase(9, 10)]
        [TestCase(10, 0)]
        [TestCase(14, 0)]
        [TestCase(15, -10)]
        [TestCase(19, -10)]
        [TestCase(20, -20)]
        [TestCase(25, -20)]
        public void DefaultCurveUsesConstantStepsAndClampedEnds(int distance, float expected)
        {
            var settings = new ShootSettings();
            Assert.That(settings.TryValidate(out _), Is.True);
            Assert.That(settings.DistanceHitChanceModifier.Evaluate(distance), Is.EqualTo(expected));
            Assert.That(settings.CreateSnapshot().GetHitChanceModifier(distance), Is.EqualTo(expected));
        }

        [Test]
        public void SnapshotUsesCustomStepsAndDoesNotShareMutableCurve()
        {
            var settings = new ShootSettings
            {
                DistanceHitChanceModifier = AnimationCurve.Linear(0, 20, 10, 5)
            };
            var snapshot = settings.CreateSnapshot();
            settings.DistanceHitChanceModifier = AnimationCurve.Constant(0, 20, 99);
            Assert.That(snapshot.GetHitChanceModifier(9), Is.EqualTo(20));
            Assert.That(snapshot.GetHitChanceModifier(10), Is.EqualTo(5));
            Assert.That(snapshot.GetHitChanceModifier(100), Is.EqualTo(5));
        }

        [Test]
        public void NormalizeMakesInspectorCurveConstantAndClamped()
        {
            var settings = new ShootSettings
            {
                DistanceHitChanceModifier = AnimationCurve.Linear(0, 20, 10, 5)
            };
            settings.NormalizeCurve();
            Assert.That(settings.DistanceHitChanceModifier.Evaluate(9), Is.EqualTo(20));
            Assert.That(settings.DistanceHitChanceModifier.Evaluate(10), Is.EqualTo(5));
            Assert.That(settings.DistanceHitChanceModifier.Evaluate(100), Is.EqualTo(5));
            Assert.That(settings.DistanceHitChanceModifier.postWrapMode, Is.EqualTo(WrapMode.ClampForever));
        }

        [Test]
        public void MissingAndInvalidConfigurationFailsExplicitly()
        {
            var game = ScriptableObject.CreateInstance<GameSettings>();
            try
            {
                game.ShootSettings = null;
                Assert.That(game.TryValidate(out string error), Is.False);
                Assert.That(error, Does.Contain("ShootSettings"));
                foreach (AnimationCurve curve in new[]
                {
                    null, new AnimationCurve(), new AnimationCurve(new Keyframe(-1, 0)),
                    new AnimationCurve(new Keyframe(0.5f, 0)), new AnimationCurve(new Keyframe(0, float.NaN))
                })
                {
                    var settings = new ShootSettings { DistanceHitChanceModifier = curve };
                    Assert.That(settings.TryValidate(out _), Is.False);
                    Assert.Throws<ArgumentException>(() => settings.CreateSnapshot());
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(game); }
        }
    }
}
