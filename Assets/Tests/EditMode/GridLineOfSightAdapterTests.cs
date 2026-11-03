using NUnit.Framework;
using UnityEngine;
using Wargame.GridSystem;
using Wargame.Units;
using Object = UnityEngine.Object;

namespace Wargame.Tests
{
    public sealed class GridLineOfSightAdapterTests
    {
        private GameObject root;
        private GridManager grid;
        private GridLineOfSightMapModifier _mapModifier;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("LOS Grid Test");
            grid = root.AddComponent<GridManager>();
            grid.Initialize(5, 5);
            _mapModifier = new GridLineOfSightMapModifier(grid);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void AdapterUsesLiveBlockStateAndOnlyFullBlockedIsOpaque()
        {
            Vector2Int from = new Vector2Int(0, 2);
            Vector2Int to = new Vector2Int(4, 2);
            Vector2Int middle = new Vector2Int(2, 2);

            Assert.That(GridLineOfSight.HasLineOfSight(_mapModifier, from, to), Is.True);
            Assert.That(grid.TrySetBlockState(middle, BlockState.HalfBlocked), Is.True);
            Assert.That(GridLineOfSight.HasLineOfSight(_mapModifier, from, to), Is.True);
            Assert.That(grid.TrySetBlockState(middle, BlockState.FullBlocked), Is.True);
            Assert.That(GridLineOfSight.HasLineOfSight(_mapModifier, from, to), Is.False);
            Assert.That(grid.TrySetBlockState(middle, BlockState.Unblocked), Is.True);
            Assert.That(GridLineOfSight.HasLineOfSight(_mapModifier, from, to), Is.True);
        }

        [Test]
        public void OccupantsAndDirectionalCoverDoNotBlockSight()
        {
            Unit unit = new GameObject("LOS Occupant").AddComponent<Unit>();
            unit.transform.SetParent(root.transform);
            Vector2Int occupied = new Vector2Int(1, 2);
            Vector2Int covered = new Vector2Int(2, 2);

            Assert.That(grid.TryPlaceUnit(unit, occupied), Is.True);
            Assert.That(grid.TrySetCover(covered, GridDirection.West, CoverType.FullCover), Is.True);
            Assert.That(GridLineOfSight.HasLineOfSight(_mapModifier, new Vector2Int(0, 2), new Vector2Int(4, 2)), Is.True);
        }

        [Test]
        public void LiveGridStateDistinguishesVisibleTargetEdgeFromAnOpaqueWall()
        {
            Vector2Int from = Vector2Int.zero;
            Vector2Int to = new Vector2Int(4, 2);
            Assert.That(grid.TrySetBlockState(new Vector2Int(3, 2), BlockState.FullBlocked), Is.True);
            Assert.That(GridLineOfSight.HasLineOfSight(_mapModifier, from, to), Is.True,
                "格心连线受阻时，可见的目标格顶点仍应允许 LOS。");

            for (int y = 0; y < grid.Height; y++)
                Assert.That(grid.TrySetBlockState(new Vector2Int(2, y), BlockState.FullBlocked), Is.True);
            Assert.That(GridLineOfSight.HasLineOfSight(_mapModifier, from, to), Is.False);

            for (int y = 0; y < grid.Height; y++)
                Assert.That(grid.TrySetBlockState(new Vector2Int(2, y), BlockState.HalfBlocked), Is.True);
            Assert.That(GridLineOfSight.HasLineOfSight(_mapModifier, from, to), Is.True);
        }

        [Test]
        public void NullGridAndUninitializedGridHaveNoVisibleCells()
        {
            Assert.That(new GridLineOfSightMapModifier(null).Contains(Vector2Int.zero), Is.False);
            var uninitializedObject = new GameObject("Uninitialized LOS Grid");
            try
            {
                var uninitializedGrid = uninitializedObject.AddComponent<GridManager>();
                Assert.That(GridLineOfSight.HasLineOfSight(new GridLineOfSightMapModifier(uninitializedGrid),
                    Vector2Int.zero, Vector2Int.zero), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(uninitializedObject);
            }
        }
    }
}
