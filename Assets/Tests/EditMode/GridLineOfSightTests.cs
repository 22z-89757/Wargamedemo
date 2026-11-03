using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Tests
{
    public sealed class GridLineOfSightTests
    {
        private sealed class MemoryMap : ILineOfSightMap
        {
            private readonly int width;
            private readonly int height;
            private readonly HashSet<Vector2Int> blockers = new HashSet<Vector2Int>();

            public MemoryMap(int width, int height)
            {
                this.width = width;
                this.height = height;
            }

            public bool Contains(Vector2Int cell) =>
                cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;

            public bool BlocksLineOfSight(Vector2Int cell) => blockers.Contains(cell);

            public void SetBlocked(Vector2Int cell, bool blocked)
            {
                if (blocked) blockers.Add(cell);
                else blockers.Remove(cell);
            }
        }

        private MemoryMap map;

        [SetUp]
        public void SetUp() => map = new MemoryMap(7, 7);

        [Test]
        public void ClearLineAndSameCellAreVisible()
        {
            Assert.That(GridLineOfSight.HasLineOfSight(map, new Vector2Int(0, 0), new Vector2Int(6, 2)), Is.True);
            Assert.That(GridLineOfSight.HasLineOfSight(map, new Vector2Int(3, 4), new Vector2Int(3, 4)), Is.True);
        }

        [Test]
        public void VisibleTargetCornerAllowsSightWhenCenterLineIsBlocked()
        {
            Vector2Int from = Vector2Int.zero;
            Vector2Int to = new Vector2Int(6, 2);
            map.SetBlocked(new Vector2Int(5, 2), true);

            Assert.That(GridLineOfSight.HasLineOfSight(map, from, to), Is.True);
        }

        [Test]
        public void WallAcrossEveryTargetCornerBlocksSight()
        {
            Vector2Int from = new Vector2Int(0, 3);
            Vector2Int to = new Vector2Int(6, 3);
            for (int y = 0; y < 7; y++)
                map.SetBlocked(new Vector2Int(3, y), true);

            Assert.That(GridLineOfSight.HasLineOfSight(map, from, to), Is.False);
        }

        [Test]
        public void NullMapAndOutOfBoundsEndpointsAreNotVisible()
        {
            Assert.That(GridLineOfSight.HasLineOfSight(null, Vector2Int.zero, Vector2Int.zero), Is.False);
            Assert.That(GridLineOfSight.HasLineOfSight(map, new Vector2Int(-1, 0), Vector2Int.zero), Is.False);
            Assert.That(GridLineOfSight.HasLineOfSight(map, Vector2Int.zero, new Vector2Int(7, 0)), Is.False);
        }

        [Test]
        public void OnlyIntermediateBlockersAreChecked()
        {
            Vector2Int from = new Vector2Int(0, 3);
            Vector2Int to = new Vector2Int(6, 3);
            map.SetBlocked(from, true);
            map.SetBlocked(to, true);
            Assert.That(GridLineOfSight.HasLineOfSight(map, from, to), Is.True);

            map.SetBlocked(new Vector2Int(3, 3), true);
            Assert.That(GridLineOfSight.HasLineOfSight(map, from, to), Is.False);
        }

        [Test]
        public void IntermediateCornerBlockersCanCoverEveryTargetRay()
        {
            map.SetBlocked(new Vector2Int(1, 0), true);
            map.SetBlocked(new Vector2Int(0, 1), true);

            Assert.That(GridLineOfSight.HasLineOfSight(map, Vector2Int.zero, new Vector2Int(4, 4)), Is.False);
        }

        [Test]
        public void OpenDiagonalCornerAllowsSight()
        {
            Assert.That(GridLineOfSight.HasLineOfSight(map, Vector2Int.zero, new Vector2Int(2, 2)), Is.True);
        }

        [Test]
        public void TerminalCornerContactIgnoresWallsThatOnlyShareTheEndpoint()
        {
            map.SetBlocked(new Vector2Int(2, 1), true);
            map.SetBlocked(new Vector2Int(1, 2), true);

            Assert.That(GridLineOfSight.HasLineOfSight(map, Vector2Int.zero, new Vector2Int(2, 2)), Is.True);
        }

        [Test]
        public void BlockerBeforeTargetBoundaryStillBlocksEveryRay()
        {
            map.SetBlocked(new Vector2Int(1, 1), true);

            Assert.That(GridLineOfSight.HasLineOfSight(map, Vector2Int.zero, new Vector2Int(2, 2)), Is.False);
        }

        [Test]
        public void ForwardAndReverseQueriesUseTheirOwnTargetBoundary()
        {
            Vector2Int from = Vector2Int.zero;
            Vector2Int to = new Vector2Int(6, 2);
            map.SetBlocked(new Vector2Int(5, 2), true);

            Assert.That(GridLineOfSight.HasLineOfSight(map, from, to), Is.True);
            Assert.That(GridLineOfSight.HasLineOfSight(map, to, from), Is.False);
        }

        [Test]
        public void HorizontalVerticalAndSteepLinesAreTraversed()
        {
            Assert.That(GridLineOfSight.HasLineOfSight(map, new Vector2Int(0, 2), new Vector2Int(6, 2)), Is.True);
            Assert.That(GridLineOfSight.HasLineOfSight(map, new Vector2Int(4, 0), new Vector2Int(4, 6)), Is.True);
            Assert.That(GridLineOfSight.HasLineOfSight(map, new Vector2Int(1, 0), new Vector2Int(3, 6)), Is.True);
        }
    }
}
