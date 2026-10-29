using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Wargame.Units;

namespace Wargame.Tests
{
    /// <summary>验证标记的引用计数、查询与变化事件。</summary>
    public sealed class TagComponentTests : UnitTestBase
    {
        private TagComponent tags;
        private List<(GameplayTag tag, bool added)> events;

        [SetUp]
        public void SetUp()
        {
            tags = CreateComponent<TagComponent>("Tags");
            events = new List<(GameplayTag, bool)>();
            tags.TagChanged += (tag, added) => events.Add((tag, added));
        }

        /// <summary>同一标记多个来源时，最后一个来源移除才真正清除，事件只在状态翻转时触发。</summary>
        [Test]
        public void ReferenceCountKeepsTagUntilLastSourceRemoved()
        {
            tags.AddTag(GameplayTag.Burning);
            tags.AddTag(GameplayTag.Burning);
            Assert.That(tags.HasTag(GameplayTag.Burning), Is.True);
            Assert.That(tags.GetTagCount(GameplayTag.Burning), Is.EqualTo(2));

            Assert.That(tags.RemoveTag(GameplayTag.Burning), Is.True);
            Assert.That(tags.HasTag(GameplayTag.Burning), Is.True);
            Assert.That(tags.GetTagCount(GameplayTag.Burning), Is.EqualTo(1));

            Assert.That(tags.RemoveTag(GameplayTag.Burning), Is.True);
            Assert.That(tags.HasTag(GameplayTag.Burning), Is.False);
            Assert.That(tags.GetTagCount(GameplayTag.Burning), Is.EqualTo(0));

            Assert.That(events, Is.EqualTo(new[] { (GameplayTag.Burning, true), (GameplayTag.Burning, false) }));
        }

        /// <summary>移除不存在的标记返回 false 且不触发事件；None 不会被记录。</summary>
        [Test]
        public void RemovingMissingTagAndAddingNoneAreIgnored()
        {
            Assert.That(tags.RemoveTag(GameplayTag.Stunned), Is.False);
            Assert.That(tags.ClearTag(GameplayTag.Stunned), Is.False);
            tags.AddTag(GameplayTag.None);

            Assert.That(tags.HasTag(GameplayTag.None), Is.False);
            Assert.That(tags.ActiveTags, Is.Empty);
            Assert.That(events, Is.Empty);
        }

        /// <summary>ClearTag 无视计数直接清除。</summary>
        [Test]
        public void ClearTagIgnoresReferenceCount()
        {
            tags.AddTag(GameplayTag.Poisoned);
            tags.AddTag(GameplayTag.Poisoned);
            tags.AddTag(GameplayTag.Poisoned);

            Assert.That(tags.ClearTag(GameplayTag.Poisoned), Is.True);
            Assert.That(tags.HasTag(GameplayTag.Poisoned), Is.False);
            Assert.That(tags.GetTagCount(GameplayTag.Poisoned), Is.EqualTo(0));
            Assert.That(events.Count, Is.EqualTo(2));
        }

        /// <summary>HasAny/HasAll 的空集合与混合集合语义。</summary>
        [Test]
        public void HasAnyAndHasAllQueries()
        {
            tags.AddTag(GameplayTag.Robotic);
            tags.AddTag(GameplayTag.ImmunePoison);

            Assert.That(tags.HasAny(new[] { GameplayTag.Organic, GameplayTag.Robotic }), Is.True);
            Assert.That(tags.HasAny(new[] { GameplayTag.Organic, GameplayTag.Dead }), Is.False);
            Assert.That(tags.HasAny(new GameplayTag[0]), Is.False);
            Assert.That(tags.HasAny(null), Is.False);

            Assert.That(tags.HasAll(new[] { GameplayTag.Robotic, GameplayTag.ImmunePoison }), Is.True);
            Assert.That(tags.HasAll(new[] { GameplayTag.Robotic, GameplayTag.Organic }), Is.False);
            Assert.That(tags.HasAll(new GameplayTag[0]), Is.True);
            Assert.That(tags.HasAll(null), Is.True);
            Assert.That(tags.ActiveTags, Is.EquivalentTo(new[] { GameplayTag.Robotic, GameplayTag.ImmunePoison }));
        }

        /// <summary>Inspector 配置的初始标记在首次访问时写入。</summary>
        [Test]
        public void InitialTagsAreAppliedOnFirstAccess()
        {
            var configured = CreateComponent<TagComponent>("Configured");
            var serialized = new SerializedObject(configured);
            SerializedProperty list = serialized.FindProperty("initialTags");
            list.arraySize = 2;
            list.GetArrayElementAtIndex(0).intValue = (int)GameplayTag.Robotic;
            list.GetArrayElementAtIndex(1).intValue = (int)GameplayTag.Robotic;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(configured.HasTag(GameplayTag.Robotic), Is.True);
            Assert.That(configured.GetTagCount(GameplayTag.Robotic), Is.EqualTo(2));
        }
    }
}
