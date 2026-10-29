using System;
using System.Collections.Generic;
using NUnit.Framework;
using Wargame.Units;

namespace Wargame.Tests
{
    /// <summary>验证属性的基础值、修饰器公式、上限钳制与变化事件。</summary>
    public sealed class CharacterAttributeTests : UnitTestBase
    {
        private CharacterAttribute attributes;

        [SetUp]
        public void SetUp()
        {
            attributes = CreateComponent<CharacterAttribute>("Attributes");
        }

        /// <summary>默认配置创建常用属性，未配置的属性访问时报错。</summary>
        [Test]
        public void DefaultsAreCreatedAndMissingAttributeThrows()
        {
            Assert.That(attributes.GetValue(AttributeType.Health), Is.EqualTo(10f));
            Assert.That(attributes.GetValue(AttributeType.MaxHealth), Is.EqualTo(10f));
            Assert.That(attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2f));
            Assert.That(attributes.GetValue(AttributeType.Aim), Is.EqualTo(65f));

            var bare = CreateComponent<CharacterAttribute>("Bare");
            var serialized = new UnityEditor.SerializedObject(bare);
            serialized.FindProperty("defaultValues").arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(bare.HasAttribute(AttributeType.Aim), Is.False);
            Assert.Throws<KeyNotFoundException>(() => bare.GetValue(AttributeType.Aim));
            Assert.Throws<KeyNotFoundException>(() => bare.SetBaseValue(AttributeType.Aim, 1f));
            Assert.That(bare.TryGetValue(AttributeType.Aim, out float value), Is.False);
            Assert.That(value, Is.EqualTo(0f));
            Assert.That(bare.RemoveModifier(new AttributeModifier(AttributeType.Aim, ModifierOperation.Add, 1f)), Is.False);
        }

        /// <summary>Current = (Base + ΣAdd) × (1 + Σ(Mul - 1))，乘法按加算合并。</summary>
        [Test]
        public void ModifiersFollowAggregationFormula()
        {
            attributes.SetBaseValue(AttributeType.Aim, 50f);
            attributes.AddModifier(new AttributeModifier(AttributeType.Aim, ModifierOperation.Add, 10f));
            attributes.AddModifier(new AttributeModifier(AttributeType.Aim, ModifierOperation.Add, -5f));
            Assert.That(attributes.GetValue(AttributeType.Aim), Is.EqualTo(55f).Within(1e-4f));

            var mulA = new AttributeModifier(AttributeType.Aim, ModifierOperation.Multiply, 1.2f);
            attributes.AddModifier(mulA);
            attributes.AddModifier(new AttributeModifier(AttributeType.Aim, ModifierOperation.Multiply, 1.2f));
            Assert.That(attributes.GetValue(AttributeType.Aim), Is.EqualTo(55f * 1.4f).Within(1e-4f));
            Assert.That(attributes.GetBaseValue(AttributeType.Aim), Is.EqualTo(50f));

            Assert.That(attributes.RemoveModifier(mulA), Is.True);
            Assert.That(attributes.RemoveModifier(mulA), Is.False);
            Assert.That(attributes.GetValue(AttributeType.Aim), Is.EqualTo(55f * 1.2f).Within(1e-4f));
            Assert.That(attributes.GetModifiers(AttributeType.Aim).Count, Is.EqualTo(3));
        }

        /// <summary>Override 覆盖计算结果，多个时以最后添加的为准；移除后恢复。</summary>
        [Test]
        public void OverrideWinsAndLastOverrideIsUsed()
        {
            attributes.SetBaseValue(AttributeType.Aim, 50f);
            attributes.AddModifier(new AttributeModifier(AttributeType.Aim, ModifierOperation.Add, 10f));
            var first = new AttributeModifier(AttributeType.Aim, ModifierOperation.Override, 0f);
            var second = new AttributeModifier(AttributeType.Aim, ModifierOperation.Override, 99f);
            attributes.AddModifier(first);
            attributes.AddModifier(second);
            Assert.That(attributes.GetValue(AttributeType.Aim), Is.EqualTo(99f));

            attributes.RemoveModifier(second);
            Assert.That(attributes.GetValue(AttributeType.Aim), Is.EqualTo(0f));
            attributes.RemoveModifier(first);
            Assert.That(attributes.GetValue(AttributeType.Aim), Is.EqualTo(60f));
        }

        /// <summary>多个降低倍率叠加时总倍率不低于 0。</summary>
        [Test]
        public void MultiplierIsFlooredAtZero()
        {
            attributes.SetBaseValue(AttributeType.Mobility, 6f);
            attributes.AddModifier(new AttributeModifier(AttributeType.Mobility, ModifierOperation.Multiply, 0.3f));
            attributes.AddModifier(new AttributeModifier(AttributeType.Mobility, ModifierOperation.Multiply, 0.3f));
            Assert.That(attributes.GetValue(AttributeType.Mobility), Is.EqualTo(0f));
        }

        /// <summary>按来源整组移除，只影响该来源。</summary>
        [Test]
        public void RemoveModifiersFromSourceOnlyRemovesThatSource()
        {
            object sourceA = new object();
            object sourceB = new object();
            attributes.AddModifier(new AttributeModifier(AttributeType.Aim, ModifierOperation.Add, 10f, sourceA));
            attributes.AddModifier(new AttributeModifier(AttributeType.Defense, ModifierOperation.Add, 20f, sourceA));
            attributes.AddModifier(new AttributeModifier(AttributeType.Aim, ModifierOperation.Add, 5f, sourceB));

            Assert.That(attributes.RemoveModifiersFromSource(sourceA), Is.EqualTo(2));
            Assert.That(attributes.GetValue(AttributeType.Aim), Is.EqualTo(70f));
            Assert.That(attributes.GetValue(AttributeType.Defense), Is.EqualTo(0f));
            Assert.That(attributes.RemoveModifiersFromSource(sourceA), Is.EqualTo(0));
            Assert.That(attributes.RemoveModifiersFromSource(null), Is.EqualTo(0));
        }

        /// <summary>生命和行动点的基础值钳制在 [0, 上限]，ApplyBaseDelta 返回实际变化量。</summary>
        [Test]
        public void ClampedAttributesStayWithinBounds()
        {
            Assert.That(attributes.ApplyBaseDelta(AttributeType.Health, 5f), Is.EqualTo(0f));
            Assert.That(attributes.GetValue(AttributeType.Health), Is.EqualTo(10f));

            Assert.That(attributes.ApplyBaseDelta(AttributeType.Health, -25f), Is.EqualTo(-10f));
            Assert.That(attributes.GetValue(AttributeType.Health), Is.EqualTo(0f));
            Assert.That(attributes.GetBaseValue(AttributeType.Health), Is.EqualTo(0f));

            attributes.SetBaseValue(AttributeType.ActionPoints, 5f);
            Assert.That(attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2f));
            attributes.SetBaseValue(AttributeType.ActionPoints, -1f);
            Assert.That(attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(0f));

            // 未受约束的属性允许为负，例如被压制后的命中。
            attributes.SetBaseValue(AttributeType.Aim, -10f);
            Assert.That(attributes.GetValue(AttributeType.Aim), Is.EqualTo(-10f));
        }

        /// <summary>上限下降时级联钳制当前值和基础值；上限恢复后不会自动回血。</summary>
        [Test]
        public void LoweringMaxClampsDependentAttribute()
        {
            object buff = new object();
            attributes.AddModifier(new AttributeModifier(AttributeType.MaxHealth, ModifierOperation.Add, 5f, buff));
            attributes.SetBaseValue(AttributeType.Health, 15f);
            Assert.That(attributes.GetValue(AttributeType.Health), Is.EqualTo(15f));

            attributes.RemoveModifiersFromSource(buff);
            Assert.That(attributes.GetValue(AttributeType.MaxHealth), Is.EqualTo(10f));
            Assert.That(attributes.GetValue(AttributeType.Health), Is.EqualTo(10f));
            Assert.That(attributes.GetBaseValue(AttributeType.Health), Is.EqualTo(10f));

            attributes.SetBaseValue(AttributeType.MaxHealth, 20f);
            Assert.That(attributes.GetValue(AttributeType.Health), Is.EqualTo(10f));
        }

        /// <summary>变化事件携带旧值和新值；值不变时不触发；级联变化同样通知。</summary>
        [Test]
        public void AttributeChangedReportsOldAndNewValues()
        {
            var events = new List<(AttributeType, float, float)>();
            attributes.EnsureInitialized();
            attributes.AttributeChanged += (type, oldValue, newValue) => events.Add((type, oldValue, newValue));

            attributes.ApplyBaseDelta(AttributeType.Health, -3f);
            attributes.SetBaseValue(AttributeType.Health, 7f);
            attributes.SetBaseValue(AttributeType.MaxHealth, 5f);

            Assert.That(events, Is.EqualTo(new[]
            {
                (AttributeType.Health, 10f, 7f),
                (AttributeType.MaxHealth, 10f, 5f),
                (AttributeType.Health, 7f, 5f)
            }));
        }

        /// <summary>DefineAttribute 新建或重置属性，并按上限钳制。</summary>
        [Test]
        public void DefineAttributeCreatesOrResets()
        {
            var bare = CreateComponent<CharacterAttribute>("Bare");
            var serialized = new UnityEditor.SerializedObject(bare);
            serialized.FindProperty("defaultValues").arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            bare.DefineAttribute(AttributeType.Health, 30f);
            Assert.That(bare.GetValue(AttributeType.Health), Is.EqualTo(30f), "没有上限属性时只限制下界。");

            bare.DefineAttribute(AttributeType.MaxHealth, 12f);
            Assert.That(bare.GetValue(AttributeType.Health), Is.EqualTo(12f));

            bare.DefineAttribute(AttributeType.MaxHealth, 20f);
            bare.DefineAttribute(AttributeType.Health, 18f);
            Assert.That(bare.GetValue(AttributeType.Health), Is.EqualTo(18f));
        }

        /// <summary>非有限数值被拒绝。</summary>
        [Test]
        public void NonFiniteValuesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => attributes.SetBaseValue(AttributeType.Aim, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => attributes.ApplyBaseDelta(AttributeType.Aim, float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => attributes.DefineAttribute(AttributeType.Aim, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AttributeModifier(AttributeType.Aim, ModifierOperation.Add, float.NaN));
            Assert.Throws<ArgumentNullException>(() => attributes.AddModifier(null));
            Assert.That(attributes.GetValue(AttributeType.Aim), Is.EqualTo(65f));
        }

        /// <summary>修饰配置按层数缩放：Add 乘层数，Multiply 的增量乘层数，Override 不变。</summary>
        [Test]
        public void ModifierSpecScalesWithStacks()
        {
            object source = new object();
            AttributeModifier add = new AttributeModifierSpec(AttributeType.Aim, ModifierOperation.Add, 5f).CreateModifier(3, source);
            AttributeModifier mul = new AttributeModifierSpec(AttributeType.Aim, ModifierOperation.Multiply, 1.1f).CreateModifier(3, source);
            AttributeModifier ovr = new AttributeModifierSpec(AttributeType.Aim, ModifierOperation.Override, 7f).CreateModifier(3, source);
            AttributeModifier clamped = new AttributeModifierSpec(AttributeType.Aim, ModifierOperation.Add, 5f).CreateModifier(0, source);

            Assert.That(add.Magnitude, Is.EqualTo(15f));
            Assert.That(mul.Magnitude, Is.EqualTo(1.3f).Within(1e-4f));
            Assert.That(ovr.Magnitude, Is.EqualTo(7f));
            Assert.That(clamped.Magnitude, Is.EqualTo(5f));
            Assert.That(add.Source, Is.SameAs(source));
        }
    }
}
