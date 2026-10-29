using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Wargame.GridSystem;
using Wargame.Units;
using Object = UnityEngine.Object;

namespace Wargame.Tests
{
    /// <summary>验证棋盘坐标、占用规则、加权可达范围和路径快照。</summary>
    public sealed class GridManagerTests
    {
        private GameObject root;
        private GridManager grid;

        /// <summary>为每个用例创建独立的五乘五棋盘。</summary>
        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Grid Test");
            grid = root.AddComponent<GridManager>();
            grid.Initialize(5, 5);
        }

        /// <summary>销毁棋盘及其子单位，避免测试污染场景。</summary>
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        /// <summary>验证所有格子的默认数据和边界查询。</summary>
        [Test]
        public void InitializationCreatesDefaultCells()
        {
            Assert.That(grid.Width, Is.EqualTo(5));
            Assert.That(grid.Height, Is.EqualTo(5));
            Assert.That(grid.CellSize, Is.EqualTo(1f));
            for (int x = 0; x < 5; x++)
            for (int y = 0; y < 5; y++)
            {
                GridCell cell = Cell(new Vector2Int(x, y));
                Assert.That(cell.Coordinates, Is.EqualTo(new Vector2Int(x, y)));
                Assert.That(cell.BlockState, Is.EqualTo(BlockState.Unblocked));
                Assert.That(cell.NorthCover, Is.EqualTo(CoverType.NoCover));
                Assert.That(cell.EastCover, Is.EqualTo(CoverType.NoCover));
                Assert.That(cell.SouthCover, Is.EqualTo(CoverType.NoCover));
                Assert.That(cell.WestCover, Is.EqualTo(CoverType.NoCover));
                Assert.That(cell.Occupant, Is.Null);
                Assert.That(cell.MoveCost, Is.EqualTo(1));
            }

            Assert.That(grid.TryGetCell(new Vector2Int(-1, 0), out GridCell missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(grid.TryGetCell(new Vector2Int(5, 0), out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => grid.Initialize(2, 2));
            Assert.That(grid.Width, Is.EqualTo(5));
        }

        /// <summary>验证未初始化时的失败行为和初始化参数校验。</summary>
        [Test]
        public void UninitializedGridRejectsInvalidRequests()
        {
            var other = new GameObject("Uninitialized Grid");
            other.transform.SetParent(root.transform);
            GridManager uninitialized = other.AddComponent<GridManager>();
            Assert.That(uninitialized.TryGetCell(Vector2Int.zero, out _), Is.False);
            Assert.That(uninitialized.TrySetMoveCost(Vector2Int.zero, 2), Is.False);
            Assert.That(uninitialized.TryWorldToGrid(Vector3.zero, out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => uninitialized.GetReachableCells(Vector2Int.zero, 1));
            Assert.Throws<InvalidOperationException>(() => uninitialized.GridToWorld(Vector2Int.zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => uninitialized.Initialize(0, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => uninitialized.Initialize(2, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => uninitialized.Initialize(2, 2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => uninitialized.Initialize(2, 2, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => uninitialized.Initialize(2, 2, float.PositiveInfinity));
            Assert.That(uninitialized.IsInitialized, Is.False);
            uninitialized.Initialize(2, 3, 2f);
            Assert.That(uninitialized.GridToWorld(new Vector2Int(1, 2)), Is.EqualTo(new Vector3(3, 0, 5)));
        }

        /// <summary>验证原点偏移、格子中心往返和忽略高度的转换约定。</summary>
        [Test]
        public void CoordinateConversionUsesCellCentersAndWorldAxes()
        {
            root.transform.position = new Vector3(-3, 2, 7);
            for (int x = 0; x < 5; x++)
            for (int y = 0; y < 5; y++)
            {
                var coordinates = new Vector2Int(x, y);
                Vector3 center = grid.GridToWorld(coordinates);
                Assert.That(center, Is.EqualTo(new Vector3(-2.5f + x, 2, 7.5f + y)));
                center.y = 100;
                Assert.That(grid.TryWorldToGrid(center, out Vector2Int actual), Is.True);
                Assert.That(actual, Is.EqualTo(coordinates));
            }

            Assert.That(grid.TryWorldToGrid(grid.Origin, out Vector2Int origin), Is.True);
            Assert.That(origin, Is.EqualTo(Vector2Int.zero));
            Assert.That(grid.TryWorldToGrid(grid.Origin + new Vector3(1, 0, 1), out Vector2Int edge), Is.True);
            Assert.That(edge, Is.EqualTo(Vector2Int.one));
            Assert.That(grid.TryWorldToGrid(grid.Origin + new Vector3(-0.01f, 0, 0), out _), Is.False);
            Assert.That(grid.TryWorldToGrid(grid.Origin + new Vector3(0, 0, -0.01f), out _), Is.False);
            Assert.That(grid.TryWorldToGrid(grid.Origin + new Vector3(5, 0, 0), out _), Is.False);
            Assert.That(grid.TryWorldToGrid(grid.Origin + new Vector3(0, 0, 5), out _), Is.False);
            Assert.That(grid.TryWorldToGrid(new Vector3(float.NaN, 0, 0), out _), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GridToWorld(new Vector2Int(0, 5)));
        }

        /// <summary>验证旋转和缩放不会被坐标转换静默接受。</summary>
        [Test]
        public void UnsupportedTransformIsRejected()
        {
            root.transform.rotation = Quaternion.Euler(0, 45, 0);
            Assert.That(grid.TryWorldToGrid(Vector3.zero, out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => grid.GridToWorld(Vector2Int.zero));
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = new Vector3(2, 1, 1);
            Assert.That(grid.TryWorldToGrid(Vector3.zero, out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => grid.GridToWorld(Vector2Int.zero));
        }

        /// <summary>验证邻格遵循北、东、南、西顺序，并正确裁剪边界。</summary>
        [Test]
        public void NeighborsFollowCardinalOrderAndRespectBounds()
        {
            IReadOnlyList<GridCell> neighbors = grid.GetNeighbors(Vector2Int.one);
            Assert.That(neighbors.Count, Is.EqualTo(4));
            Assert.That(neighbors[0].Coordinates, Is.EqualTo(new Vector2Int(1, 2)));
            Assert.That(neighbors[1].Coordinates, Is.EqualTo(new Vector2Int(2, 1)));
            Assert.That(neighbors[2].Coordinates, Is.EqualTo(new Vector2Int(1, 0)));
            Assert.That(neighbors[3].Coordinates, Is.EqualTo(new Vector2Int(0, 1)));
            Assert.That(grid.GetNeighbors(Vector2Int.zero).Count, Is.EqualTo(2));
            Assert.That(grid.GetNeighbors(new Vector2Int(-1, 0)), Is.Empty);
        }

        /// <summary>验证普通地面范围是四方向步数范围。</summary>
        /// <param name="x">起点的 X 坐标。</param>
        /// <param name="y">起点的逻辑 Y 坐标。</param>
        /// <param name="budget">移动力预算。</param>
        [TestCase(2, 2, 0)]
        [TestCase(2, 2, 1)]
        [TestCase(2, 2, 2)]
        [TestCase(0, 0, 2)]
        [TestCase(4, 2, 3)]
        public void EmptyBoardMatchesManhattanRange(int x, int y, int budget)
        {
            var start = new Vector2Int(x, y);
            GridReachabilityResult result = grid.GetReachableCells(start, budget);
            int expectedCount = 0;
            for (int cx = 0; cx < 5; cx++)
            for (int cy = 0; cy < 5; cy++)
            {
                var position = new Vector2Int(cx, cy);
                int cost = Math.Abs(cx - x) + Math.Abs(cy - y);
                bool expected = cost > 0 && cost <= budget;
                Assert.That(result.ReachableCosts.ContainsKey(position), Is.EqualTo(expected));
                if (!expected)
                    continue;
                expectedCount++;
                Assert.That(result.ReachableCosts[position], Is.EqualTo(cost));
            }

            Assert.That(result.ReachableCosts.Count, Is.EqualTo(expectedCount));
            Assert.That(result.Start, Is.EqualTo(start));
            Assert.That(result.MovementBudget, Is.EqualTo(budget));
            Assert.That(result.TryGetPath(start, out IReadOnlyList<Vector2Int> path), Is.True);
            CollectionAssert.AreEqual(new[] { start }, path);
        }

        /// <summary>验证搜索绕开高代价格，且起点不收取进入代价。</summary>
        [Test]
        public void WeightedSearchChoosesCheaperDetour()
        {
            grid.TrySetMoveCost(Vector2Int.zero, 99);
            grid.TrySetMoveCost(Vector2Int.right, 9);
            GridReachabilityResult result = grid.GetReachableCells(Vector2Int.zero, 4);
            var target = new Vector2Int(2, 0);
            Assert.That(result.ReachableCosts.ContainsKey(Vector2Int.right), Is.False);
            Assert.That(result.ReachableCosts[target], Is.EqualTo(4));
            Assert.That(result.TryGetPath(target, out IReadOnlyList<Vector2Int> path), Is.True);
            CollectionAssert.AreEqual(new[]
            {
                Vector2Int.zero, Vector2Int.up, Vector2Int.one, new Vector2Int(2, 1), target
            }, path);
            Assert.That(result.TryGetPath(new Vector2Int(4, 4), out path), Is.False);
            Assert.That(path, Is.Empty);
        }

        /// <summary>验证同代价路径稳定地优先向北扩展。</summary>
        [Test]
        public void EqualCostPathsUseStableDirectionOrder()
        {
            for (int repeat = 0; repeat < 3; repeat++)
            {
                GridReachabilityResult result = grid.GetReachableCells(Vector2Int.one, 2);
                Assert.That(result.TryGetPath(new Vector2Int(2, 2), out IReadOnlyList<Vector2Int> path), Is.True);
                CollectionAssert.AreEqual(new[]
                {
                    Vector2Int.one, new Vector2Int(1, 2), new Vector2Int(2, 2)
                }, path);
            }
        }

        /// <summary>验证半障碍正常通行、全障碍阻挡以及封闭区域不可达。</summary>
        [Test]
        public void BlockStatesControlTraversalAndEnclosure()
        {
            grid.TrySetBlockState(Vector2Int.up, BlockState.HalfBlocked);
            GridReachabilityResult result = grid.GetReachableCells(Vector2Int.zero, 1);
            Assert.That(result.ReachableCosts.ContainsKey(Vector2Int.up), Is.False);
            Assert.That(grid.GetReachableCells(Vector2Int.zero, 2).TryGetPath(new Vector2Int(0, 2), out _), Is.True);
            grid.TrySetBlockState(Vector2Int.up, BlockState.FullBlocked);
            grid.TrySetBlockState(Vector2Int.right, BlockState.FullBlocked);
            Assert.That(grid.GetReachableCells(Vector2Int.zero, 100).ReachableCosts, Is.Empty);
        }

        /// <summary>验证障碍切换同步进入代价，代价接口拒绝与障碍状态冲突的输入。</summary>
        [Test]
        public void FullBlockedStateKeepsAnImpassableCostUntilUnblocked()
        {
            Assert.That(grid.TrySetMoveCost(Vector2Int.one, 7), Is.True);
            Assert.That(grid.TrySetBlockState(Vector2Int.one, BlockState.HalfBlocked), Is.True);
            Assert.That(Cell(Vector2Int.one).MoveCost, Is.EqualTo(7));
            Assert.That(grid.TrySetBlockState(Vector2Int.one, BlockState.FullBlocked), Is.True);
            Assert.That(Cell(Vector2Int.one).MoveCost, Is.EqualTo(-1));
            Assert.That(grid.TrySetMoveCost(Vector2Int.one, 3), Is.False);
            Assert.That(grid.TrySetMoveCost(Vector2Int.one, 0), Is.False);
            Assert.That(grid.TrySetMoveCost(Vector2Int.one, -2), Is.False);
            Assert.That(grid.TrySetMoveCost(Vector2Int.one, -1), Is.True);
            Assert.That(Cell(Vector2Int.one).BlockState, Is.EqualTo(BlockState.FullBlocked));
            Assert.That(Cell(Vector2Int.one).MoveCost, Is.EqualTo(-1));

            Assert.That(grid.TrySetBlockState(Vector2Int.one, BlockState.HalfBlocked), Is.True);
            Assert.That(Cell(Vector2Int.one).MoveCost, Is.EqualTo(1));
            Assert.That(grid.TrySetMoveCost(Vector2Int.one, -1), Is.False);
            Assert.That(grid.TrySetMoveCost(Vector2Int.one, 4), Is.True);
            Assert.That(grid.TrySetBlockState(Vector2Int.one, BlockState.Unblocked), Is.True);
            Assert.That(Cell(Vector2Int.one).MoveCost, Is.EqualTo(4));
            grid.TrySetBlockState(Vector2Int.one, BlockState.FullBlocked);
            grid.TrySetBlockState(Vector2Int.one, BlockState.Unblocked);
            Assert.That(Cell(Vector2Int.one).MoveCost, Is.EqualTo(1));
        }

        /// <summary>验证 -1 是不可通行标记，不会作为负权参与最短路径计算。</summary>
        [Test]
        public void SearchDetoursAroundNegativeBlockedCost()
        {
            grid.TrySetBlockState(Vector2Int.right, BlockState.FullBlocked);
            GridReachabilityResult result = grid.GetReachableCells(Vector2Int.zero, 4);
            Assert.That(Cell(Vector2Int.right).MoveCost, Is.EqualTo(-1));
            Assert.That(result.ReachableCosts.ContainsKey(Vector2Int.right), Is.False);
            Assert.That(result.TryGetPath(Vector2Int.right, out _), Is.False);
            Assert.That(result.ReachableCosts[new Vector2Int(2, 0)], Is.EqualTo(4));
            Assert.That(result.TryGetPath(new Vector2Int(2, 0), out IReadOnlyList<Vector2Int> path), Is.True);
            CollectionAssert.DoesNotContain(path, Vector2Int.right);
            foreach (int cost in result.ReachableCosts.Values)
                Assert.That(cost, Is.GreaterThan(0));
        }

        /// <summary>验证起点和预算的无效请求会抛出对应参数异常。</summary>
        [Test]
        public void SearchRejectsInvalidArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetReachableCells(Vector2Int.zero, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetReachableCells(new Vector2Int(5, 0), 1));
            grid.TrySetBlockState(Vector2Int.zero, BlockState.FullBlocked);
            Assert.Throws<ArgumentException>(() => grid.GetReachableCells(Vector2Int.zero, 1));
        }

        /// <summary>验证可以穿过占用格到达后方，但不能将占用格作为终点。</summary>
        [Test]
        public void OccupiedCellsAreTraversableButNotDestinations()
        {
            Unit mover = CreateUnit("Mover");
            Unit other = CreateUnit("Other");
            Assert.That(grid.TryPlaceUnit(mover, Vector2Int.zero), Is.True);
            Assert.That(grid.TryPlaceUnit(other, Vector2Int.right), Is.True);
            GridReachabilityResult result = grid.GetReachableCells(Vector2Int.zero, 2);
            Assert.That(result.ReachableCosts.ContainsKey(Vector2Int.zero), Is.False);
            Assert.That(result.ReachableCosts.ContainsKey(Vector2Int.right), Is.False);
            Assert.That(result.TryGetPath(Vector2Int.right, out _), Is.False);
            var target = new Vector2Int(2, 0);
            Assert.That(result.ReachableCosts[target], Is.EqualTo(2));
            Assert.That(result.TryGetPath(target, out IReadOnlyList<Vector2Int> path), Is.True);
            CollectionAssert.AreEqual(new[] { Vector2Int.zero, Vector2Int.right, target }, path);
        }

        /// <summary>验证四边掩体互相独立，不影响通行和邻格数据。</summary>
        [Test]
        public void DirectionalCoverDoesNotAffectNeighborsOrMovement()
        {
            grid.TrySetCover(Vector2Int.zero, GridDirection.North, CoverType.FullCover);
            grid.TrySetCover(Vector2Int.zero, GridDirection.East, CoverType.HalfCover);
            grid.TrySetCover(Vector2Int.zero, GridDirection.South, CoverType.HalfCover);
            grid.TrySetCover(Vector2Int.zero, GridDirection.West, CoverType.FullCover);
            GridCell cell = Cell(Vector2Int.zero);
            Assert.That(cell.NorthCover, Is.EqualTo(CoverType.FullCover));
            Assert.That(cell.EastCover, Is.EqualTo(CoverType.HalfCover));
            Assert.That(cell.SouthCover, Is.EqualTo(CoverType.HalfCover));
            Assert.That(cell.WestCover, Is.EqualTo(CoverType.FullCover));
            Assert.That(Cell(Vector2Int.up).SouthCover, Is.EqualTo(CoverType.NoCover));
            Assert.That(Cell(Vector2Int.right).WestCover, Is.EqualTo(CoverType.NoCover));
            Assert.That(grid.GetReachableCells(Vector2Int.zero, 1).ReachableCosts.Count, Is.EqualTo(2));
        }

        /// <summary>验证非法枚举、代价和坐标不会部分修改格子。</summary>
        [Test]
        public void InvalidCellEditsLeaveDataUnchanged()
        {
            Assert.That(grid.TrySetMoveCost(Vector2Int.zero, 0), Is.False);
            Assert.That(grid.TrySetMoveCost(Vector2Int.zero, -1), Is.False);
            Assert.That(grid.TrySetBlockState(Vector2Int.zero, (BlockState)99), Is.False);
            Assert.That(grid.TrySetCover(Vector2Int.zero, (GridDirection)(-1), CoverType.FullCover), Is.False);
            Assert.That(grid.TrySetCover(Vector2Int.zero, GridDirection.North, (CoverType)99), Is.False);
            var outside = new Vector2Int(-1, 0);
            Assert.That(grid.TrySetMoveCost(outside, 2), Is.False);
            Assert.That(grid.TrySetBlockState(outside, BlockState.FullBlocked), Is.False);
            Assert.That(grid.TrySetCover(outside, GridDirection.North, CoverType.FullCover), Is.False);
            Assert.That(Cell(Vector2Int.zero).MoveCost, Is.EqualTo(1));
            Assert.That(Cell(Vector2Int.zero).BlockState, Is.EqualTo(BlockState.Unblocked));
            Assert.That(Cell(Vector2Int.zero).NorthCover, Is.EqualTo(CoverType.NoCover));
        }

        /// <summary>验证单位放置、转移、移除保持唯一占用，且不移动场景物体。</summary>
        [Test]
        public void UnitUpdatesAreAtomicAndOnlyChangeLogicalOccupancy()
        {
            Unit mover = CreateUnit("Mover");
            Unit other = CreateUnit("Other");
            Vector3 originalPosition = mover.transform.position;
            Assert.That(grid.TryPlaceUnit(mover, Vector2Int.zero), Is.True);
            Assert.That(grid.TryPlaceUnit(mover, Vector2Int.up), Is.False);
            Assert.That(grid.TryPlaceUnit(other, Vector2Int.zero), Is.False);
            Assert.That(grid.TryPlaceUnit(null, Vector2Int.up), Is.False);
            Assert.That(grid.TrySetBlockState(Vector2Int.zero, BlockState.FullBlocked), Is.False);
            Assert.That(Cell(Vector2Int.zero).BlockState, Is.EqualTo(BlockState.Unblocked));
            Assert.That(grid.TryPlaceUnit(other, Vector2Int.right), Is.True);
            Assert.That(grid.TryMoveUnit(mover, Vector2Int.right), Is.False);
            Assert.That(Cell(Vector2Int.zero).Occupant, Is.SameAs(mover));
            Assert.That(Cell(Vector2Int.right).Occupant, Is.SameAs(other));
            Assert.That(grid.TryMoveUnit(mover, new Vector2Int(-1, 0)), Is.False);
            grid.TrySetBlockState(Vector2Int.up, BlockState.FullBlocked);
            Assert.That(grid.TryPlaceUnit(CreateUnit("Third"), Vector2Int.up), Is.False);
            Assert.That(grid.TryMoveUnit(mover, Vector2Int.up), Is.False);
            Assert.That(Cell(Vector2Int.zero).Occupant, Is.SameAs(mover));
            Assert.That(grid.TryMoveUnit(mover, Vector2Int.zero), Is.True);
            grid.TrySetBlockState(Vector2Int.up, BlockState.HalfBlocked);
            Assert.That(grid.TryMoveUnit(mover, Vector2Int.up), Is.False);
            Assert.That(grid.TryPlaceUnit(CreateUnit("HalfBlockedSpawn"), Vector2Int.up), Is.False);
            Assert.That(grid.TrySetBlockState(Vector2Int.zero, BlockState.HalfBlocked), Is.False);
            grid.TrySetBlockState(Vector2Int.up, BlockState.Unblocked);
            Assert.That(grid.TryMoveUnit(mover, Vector2Int.up), Is.True);
            Assert.That(Cell(Vector2Int.zero).Occupant, Is.Null);
            Assert.That(Cell(Vector2Int.up).Occupant, Is.SameAs(mover));
            Assert.That(mover.transform.position, Is.EqualTo(originalPosition));
            Assert.That(grid.TryRemoveUnit(mover), Is.True);
            Assert.That(Cell(Vector2Int.up).Occupant, Is.Null);
            Assert.That(grid.TryRemoveUnit(mover), Is.False);
            Assert.That(grid.TryMoveUnit(mover, Vector2Int.zero), Is.False);
        }

        /// <summary>验证已销毁单位按空格处理，不阻止后续放置。</summary>
        [Test]
        public void DestroyedUnitDoesNotLeaveAnOccupiedCell()
        {
            Unit unit = CreateUnit("Destroyed");
            grid.TryPlaceUnit(unit, Vector2Int.right);
            Object.DestroyImmediate(unit.gameObject);
            Assert.That(Cell(Vector2Int.right).Occupant == null, Is.True);
            Assert.That(grid.GetReachableCells(Vector2Int.zero, 1).ReachableCosts.ContainsKey(Vector2Int.right), Is.True);
            Assert.That(grid.TryPlaceUnit(CreateUnit("Replacement"), Vector2Int.right), Is.True);
        }

        /// <summary>验证结果和路径是快照，后续格子变化只影响新查询。</summary>
        [Test]
        public void SearchResultsRemainIndependentSnapshots()
        {
            GridReachabilityResult result = grid.GetReachableCells(Vector2Int.zero, 1);
            grid.TryPlaceUnit(CreateUnit("Later Occupant"), Vector2Int.right);
            grid.TrySetBlockState(Vector2Int.up, BlockState.FullBlocked);
            grid.TrySetMoveCost(Vector2Int.right, 5);
            Assert.That(result.ReachableCosts[Vector2Int.right], Is.EqualTo(1));
            Assert.That(result.TryGetPath(Vector2Int.up, out IReadOnlyList<Vector2Int> path), Is.True);
            CollectionAssert.AreEqual(new[] { Vector2Int.zero, Vector2Int.up }, path);
            Assert.That(grid.GetReachableCells(Vector2Int.zero, 1).ReachableCosts, Is.Empty);
            var dictionary = (IDictionary<Vector2Int, int>)result.ReachableCosts;
            Assert.Throws<NotSupportedException>(() => dictionary.Add(new Vector2Int(4, 4), 1));
            Assert.Throws<NotSupportedException>(() => ((IList<Vector2Int>)path)[0] = Vector2Int.one);
        }

        /// <summary>验证累计代价达到 int 上限时不会溢出为负数或错误扩展。</summary>
        [Test]
        public void MaximumMovementCostDoesNotOverflow()
        {
            for (int x = 0; x < 5; x++)
            for (int y = 0; y < 5; y++)
            {
                if (y != 0 || x > 2)
                    grid.TrySetBlockState(new Vector2Int(x, y), BlockState.FullBlocked);
            }

            grid.TrySetMoveCost(Vector2Int.right, int.MaxValue);
            GridReachabilityResult result = grid.GetReachableCells(Vector2Int.zero, int.MaxValue);
            Assert.That(result.ReachableCosts.Count, Is.EqualTo(1));
            Assert.That(result.ReachableCosts[Vector2Int.right], Is.EqualTo(int.MaxValue));
            Assert.That(result.ReachableCosts.ContainsKey(new Vector2Int(2, 0)), Is.False);
        }

        /// <summary>用独立的反复松弛算法交叉验证多组加权地图及路径。</summary>
        [Test]
        public void WeightedMapsMatchIndependentShortestPathReference()
        {
            for (int seed = 0; seed < 12; seed++)
            {
                var random = new System.Random(seed);
                for (int x = 0; x < 5; x++)
                for (int y = 0; y < 5; y++)
                {
                    var position = new Vector2Int(x, y);
                    grid.TrySetBlockState(position, position != Vector2Int.zero && random.Next(4) == 0
                        ? BlockState.FullBlocked : BlockState.Unblocked);
                    grid.TrySetMoveCost(position, random.Next(1, 8));
                }

                const int budget = 20;
                int[,] expected = ReferenceCosts(Vector2Int.zero);
                GridReachabilityResult result = grid.GetReachableCells(Vector2Int.zero, budget);
                for (int x = 0; x < 5; x++)
                for (int y = 0; y < 5; y++)
                {
                    var destination = new Vector2Int(x, y);
                    bool reachable = destination != Vector2Int.zero && expected[x, y] <= budget;
                    Assert.That(result.ReachableCosts.ContainsKey(destination), Is.EqualTo(reachable),
                        $"seed={seed}, destination={destination}");
                    if (!reachable)
                        continue;

                    Assert.That(result.ReachableCosts[destination], Is.EqualTo(expected[x, y]));
                    Assert.That(result.TryGetPath(destination, out IReadOnlyList<Vector2Int> path), Is.True);
                    Assert.That(path[0], Is.EqualTo(Vector2Int.zero));
                    Assert.That(path[path.Count - 1], Is.EqualTo(destination));
                    int pathCost = 0;
                    for (int i = 1; i < path.Count; i++)
                    {
                        Vector2Int step = path[i] - path[i - 1];
                        Assert.That(Math.Abs(step.x) + Math.Abs(step.y), Is.EqualTo(1));
                        Assert.That(Cell(path[i]).BlockState, Is.Not.EqualTo(BlockState.FullBlocked));
                        pathCost += Cell(path[i]).MoveCost;
                    }

                    Assert.That(pathCost, Is.EqualTo(expected[x, y]));
                }
            }
        }

        /// <summary>通过反复遍历所有边计算参考最短距离，不依赖最小堆。</summary>
        /// <param name="start">参考搜索起点。</param>
        /// <returns>各格最小代价，不可达格为 int.MaxValue。</returns>
        private int[,] ReferenceCosts(Vector2Int start)
        {
            var costs = new int[5, 5];
            for (int x = 0; x < 5; x++)
            for (int y = 0; y < 5; y++)
                costs[x, y] = int.MaxValue;
            costs[start.x, start.y] = 0;

            for (int pass = 0; pass < 24; pass++)
            for (int x = 0; x < 5; x++)
            for (int y = 0; y < 5; y++)
            {
                if (costs[x, y] == int.MaxValue)
                    continue;
                foreach (GridCell neighbor in grid.GetNeighbors(new Vector2Int(x, y)))
                {
                    if (neighbor.BlockState == BlockState.FullBlocked)
                        continue;
                    Vector2Int next = neighbor.Coordinates;
                    costs[next.x, next.y] = Math.Min(costs[next.x, next.y], costs[x, y] + neighbor.MoveCost);
                }
            }

            return costs;
        }

        /// <summary>取得测试所需的有效格子，并断言查询成功。</summary>
        /// <param name="coordinates">棋盘内坐标。</param>
        /// <returns>对应格子的逻辑数据。</returns>
        private GridCell Cell(Vector2Int coordinates)
        {
            Assert.That(grid.TryGetCell(coordinates, out GridCell cell), Is.True);
            return cell;
        }

        /// <summary>创建受测试根物体管理的单位。</summary>
        /// <param name="name">测试单位名称。</param>
        /// <returns>新建的单位组件。</returns>
        private Unit CreateUnit(string name)
        {
            var unitObject = new GameObject(name);
            unitObject.transform.SetParent(root.transform);
            return unitObject.AddComponent<Unit>();
        }
    }
}
