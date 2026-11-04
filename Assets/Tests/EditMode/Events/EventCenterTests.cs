using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Wargame.Events;

namespace Wargame.Tests
{
    public sealed class EventCenterTests
    {
        private readonly struct FirstEvent
        {
            public FirstEvent(int value) => Value = value;
            public int Value { get; }
        }

        private readonly struct SecondEvent
        {
        }

        private EventCenter center;

        [SetUp]
        public void SetUp()
        {
            EventCenter.ResetForTests();
            center = EventCenter.Instance;
        }

        [TearDown]
        public void TearDown() => EventCenter.ResetForTests();

        [Test]
        public void PublishOnlyInvokesListenersOfExactEventType()
        {
            int firstValue = 0;
            int secondCalls = 0;
            center.Subscribe<FirstEvent>(message => firstValue = message.Value);
            center.Subscribe<SecondEvent>(_ => secondCalls++);

            center.Publish(new FirstEvent(7));

            Assert.That(firstValue, Is.EqualTo(7));
            Assert.That(secondCalls, Is.Zero);
        }

        [Test]
        public void DuplicateSubscriptionIsIdempotentAndUnsubscribeIsSafe()
        {
            int calls = 0;
            Action<FirstEvent> listener = _ => calls++;
            center.Subscribe(listener);
            center.Subscribe(listener);

            center.Publish(new FirstEvent(1));
            center.Unsubscribe(listener);
            center.Unsubscribe(listener);
            center.Publish(new FirstEvent(2));

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void SubscriptionChangesDuringPublishApplyToNextPublish()
        {
            int firstCalls = 0;
            int secondCalls = 0;
            int lateCalls = 0;
            Action<FirstEvent> second = _ => secondCalls++;
            Action<FirstEvent> late = _ => lateCalls++;
            Action<FirstEvent> first = _ =>
            {
                firstCalls++;
                center.Unsubscribe(second);
                center.Subscribe(late);
            };
            center.Subscribe(first);
            center.Subscribe(second);

            center.Publish(new FirstEvent(1));
            Assert.That((firstCalls, secondCalls, lateCalls), Is.EqualTo((1, 1, 0)));

            center.Publish(new FirstEvent(2));
            Assert.That((firstCalls, secondCalls, lateCalls), Is.EqualTo((2, 1, 1)));
        }

        [Test]
        public void ClearAllListenersStopsFutureDelivery()
        {
            int calls = 0;
            center.Subscribe<FirstEvent>(_ => calls++);
            center.ClearAllListeners();

            center.Publish(new FirstEvent(1));

            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void ResetForTestsDestroysInstanceAndDropsOldListeners()
        {
            int calls = 0;
            center.Subscribe<FirstEvent>(_ => calls++);
            EventCenter previous = center;

            EventCenter.ResetForTests();
            center = EventCenter.Instance;
            center.Publish(new FirstEvent(1));

            Assert.That(previous == null, Is.True);
            Assert.That(center, Is.Not.Null);
            Assert.That(center.gameObject.name, Is.EqualTo(nameof(EventCenter)));
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void DuplicateComponentDoesNotReplacePrimaryInstance()
        {
            EventCenter primary = center;
            var duplicateRoot = new GameObject("DuplicateEventCenter");
            EventCenter duplicate = duplicateRoot.AddComponent<EventCenter>();
            typeof(EventCenter).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(duplicate, null);

            Assert.That(EventCenter.Instance, Is.SameAs(primary));
            Assert.That(duplicate == null, Is.True);
            Assert.That(duplicateRoot == null, Is.True);
        }

        [Test]
        public void DestroyedPrimaryCanBeCreatedAgain()
        {
            EventCenter previous = center;
            UnityEngine.Object.DestroyImmediate(previous.gameObject);

            center = EventCenter.Instance;

            Assert.That(previous == null, Is.True);
            Assert.That(center, Is.Not.Null);
            Assert.That(center.IsPrimaryInstance, Is.True);
        }
    }
}
