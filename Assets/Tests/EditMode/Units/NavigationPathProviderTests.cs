using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class NavigationPathProviderTests
    {
        [Test]
        public void CompletePathReturnsWorldCornersAndRestoresExactEndpoints()
        {
            var query = new FakeNavigationQuery
            {
                SampledStart = new Vector3(0.1f, 0f, 0.2f),
                SampledDestination = new Vector3(4.8f, 0f, 5.1f),
                Status = NavigationQueryStatus.Complete,
                Path = { new Vector3(0.1f, 0f, 0.2f), new Vector3(2f, 0f, 3f), new Vector3(4.8f, 0f, 5.1f) }
            };
            var provider = new NavigationPathProvider(query);
            Vector3 start = Vector3.zero;
            Vector3 destination = new Vector3(5f, 0f, 5f);

            Assert.That(provider.TryCalculate(start, destination, 1f, out var corners, out string error), Is.True);
            Assert.That(error, Is.Null);
            Assert.That(corners, Has.Count.EqualTo(3));
            Assert.That(corners[0], Is.EqualTo(start));
            Assert.That(corners[1], Is.EqualTo(new Vector3(2f, 0f, 3f)));
            Assert.That(corners[2], Is.EqualTo(destination));
            Assert.That(query.CalculatedStart, Is.EqualTo(query.SampledStart));
            Assert.That(query.CalculatedDestination, Is.EqualTo(query.SampledDestination));
        }

        [TestCase(true, false, "起点")]
        [TestCase(false, true, "终点")]
        public void SamplingFailureRejectsPath(bool failStart, bool failDestination, string expectedLabel)
        {
            var query = new FakeNavigationQuery
            {
                FailStartSample = failStart,
                FailDestinationSample = failDestination
            };
            var provider = new NavigationPathProvider(query);

            Assert.That(provider.TryCalculate(Vector3.zero, Vector3.one, 1f, out var corners, out string error), Is.False);
            Assert.That(corners, Is.Empty);
            Assert.That(error, Does.Contain(expectedLabel));
            Assert.That(query.CalculateCount, Is.Zero);
        }

        [Test]
        public void PartialPathIsRejectedEvenWhenItHasCorners()
        {
            var query = new FakeNavigationQuery { Status = NavigationQueryStatus.Partial };
            query.Path.Add(Vector3.zero);
            query.Path.Add(Vector3.one);
            var provider = new NavigationPathProvider(query);

            Assert.That(provider.TryCalculate(Vector3.zero, Vector3.one, 1f, out var corners, out string error), Is.False);
            Assert.That(corners, Is.Empty);
            Assert.That(error, Does.Contain("Partial"));
        }

        private sealed class FakeNavigationQuery : INavigationQuery
        {
            public readonly List<Vector3> Path = new List<Vector3>();
            public Vector3 SampledStart;
            public Vector3 SampledDestination = Vector3.one;
            public Vector3 CalculatedStart;
            public Vector3 CalculatedDestination;
            public NavigationQueryStatus Status = NavigationQueryStatus.Complete;
            public bool FailStartSample;
            public bool FailDestinationSample;
            public int CalculateCount;
            private int sampleCount;

            public bool TrySamplePosition(Vector3 source, float maxDistance, out Vector3 sampled)
            {
                bool isStart = sampleCount++ == 0;
                sampled = isStart ? SampledStart : SampledDestination;
                return isStart ? !FailStartSample : !FailDestinationSample;
            }

            public NavigationQueryStatus CalculatePath(Vector3 start, Vector3 destination, List<Vector3> corners)
            {
                CalculateCount++;
                CalculatedStart = start;
                CalculatedDestination = destination;
                corners.AddRange(Path);
                return Status;
            }
        }
    }
}
