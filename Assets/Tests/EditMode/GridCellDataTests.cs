using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Wargame.GridSystem;
using Wargame.Units;
using Object = UnityEngine.Object;

namespace Wargame.Tests
{
    /// <summary>验证地图数据继承、引用隔离及原有序列化格式的兼容性。</summary>
    public sealed class GridCellDataTests
    {
        /// <summary>验证运行时属性来自同一基类，同时不修改用于初始化的地图对象。</summary>
        [Test]
        public void RuntimeCellUsesInheritedPropertiesOnAnIndependentCopy()
        {
            var source = new MapCellData(Vector2Int.one, 3, BlockState.HalfBlocked,
                CoverType.HalfCover, CoverType.FullCover, CoverType.NoCover, CoverType.HalfCover);
            var runtime = new GridCell(source);
            MapCellData inherited = runtime;
            Assert.That(runtime, Is.Not.SameAs(source));
            Assert.That(inherited.Coordinates, Is.EqualTo(Vector2Int.one));
            Assert.That(inherited.MoveCost, Is.EqualTo(3));
            Assert.That(inherited.BlockState, Is.EqualTo(BlockState.HalfBlocked));
            Assert.That(inherited.NorthCover, Is.EqualTo(CoverType.HalfCover));
            Assert.That(inherited.EastCover, Is.EqualTo(CoverType.FullCover));
            Assert.That(inherited.SouthCover, Is.EqualTo(CoverType.NoCover));
            Assert.That(inherited.WestCover, Is.EqualTo(CoverType.HalfCover));

            runtime.SetMoveCost(8);
            runtime.SetBlockState(BlockState.FullBlocked);
            runtime.SetCover(GridDirection.North, CoverType.FullCover);
            runtime.SetCover(GridDirection.East, CoverType.NoCover);
            runtime.SetCover(GridDirection.South, CoverType.HalfCover);
            runtime.SetCover(GridDirection.West, CoverType.FullCover);
            Assert.That(inherited.MoveCost, Is.EqualTo(-1));
            Assert.That(inherited.BlockState, Is.EqualTo(BlockState.FullBlocked));
            Assert.That(inherited.NorthCover, Is.EqualTo(CoverType.FullCover));
            Assert.That(inherited.EastCover, Is.EqualTo(CoverType.NoCover));
            Assert.That(inherited.SouthCover, Is.EqualTo(CoverType.HalfCover));
            Assert.That(inherited.WestCover, Is.EqualTo(CoverType.FullCover));
            Assert.That(source.MoveCost, Is.EqualTo(3));
            Assert.That(source.BlockState, Is.EqualTo(BlockState.HalfBlocked));
            Assert.That(source.NorthCover, Is.EqualTo(CoverType.HalfCover));
            Assert.That(source.EastCover, Is.EqualTo(CoverType.FullCover));
            Assert.That(source.SouthCover, Is.EqualTo(CoverType.NoCover));
            Assert.That(source.WestCover, Is.EqualTo(CoverType.HalfCover));
        }

        /// <summary>验证从运行时格子复制时保留地图属性，但不复制单位占用。</summary>
        [Test]
        public void CopyingAnOccupiedCellDoesNotCopyItsOccupant()
        {
            var unitObject = new GameObject("Cell Copy Test Unit");
            try
            {
                var source = new GridCell(new MapCellData(Vector2Int.zero, 4, BlockState.HalfBlocked));
                source.Occupant = unitObject.AddComponent<Unit>();
                var snapshot = new MapCellData(source);
                var runtimeCopy = new GridCell(source);
                Assert.That(runtimeCopy.Occupant, Is.Null);
                Assert.That(runtimeCopy.MoveCost, Is.EqualTo(4));
                source.SetMoveCost(7);
                Assert.That(snapshot.MoveCost, Is.EqualTo(4));
                Assert.That(runtimeCopy.MoveCost, Is.EqualTo(4));
                Assert.That(source.Occupant, Is.Not.Null);
            }
            finally { Object.DestroyImmediate(unitObject); }
        }

        /// <summary>验证资产写入逐格复制，并剥离派生类型中的局内状态。</summary>
        [Test]
        public void MapWritesCopyCellsInsteadOfSharingInputReferences()
        {
            MapData map = ScriptableObject.CreateInstance<MapData>();
            try
            {
                var source = new GridCell(new MapCellData(Vector2Int.zero, 2, BlockState.HalfBlocked));
                var input = new MapCellData[] { source };
                map.SetData(1, 1, 1, input);
                Assert.That(map.TryGetCell(Vector2Int.zero, out MapCellData saved), Is.True);
                Assert.That(saved, Is.Not.SameAs(source));
                Assert.That(saved.GetType(), Is.EqualTo(typeof(MapCellData)));
                source.SetMoveCost(9);
                source.SetBlockState(BlockState.FullBlocked);
                input[0] = null;
                Assert.That(saved.MoveCost, Is.EqualTo(2));
                Assert.That(saved.BlockState, Is.EqualTo(BlockState.HalfBlocked));
                Assert.That(map.TryValidate(out _), Is.True);
            }
            finally { Object.DestroyImmediate(map); }
        }

        /// <summary>验证草稿导出的数组和格子对象均为独立副本。</summary>
        [Test]
        public void DraftCopiesDoNotShareCellReferences()
        {
            var draft = new GridMapDraft(2, 1);
            draft.TrySetCell(Vector2Int.zero, 3, BlockState.HalfBlocked);
            draft.TryGetCell(Vector2Int.zero, out MapCellData original);
            MapCellData[] copy = draft.CopyCells();
            Assert.That(copy[0], Is.Not.SameAs(original));
            Assert.That(copy[0].MoveCost, Is.EqualTo(3));
            draft.TrySetCell(Vector2Int.zero, 8, BlockState.FullBlocked);
            Assert.That(copy[0].MoveCost, Is.EqualTo(3));
            Assert.That(copy[0].BlockState, Is.EqualTo(BlockState.HalfBlocked));
            copy[1] = null;
            Assert.That(draft.TryGetCell(Vector2Int.right, out _), Is.True);
        }

        /// <summary>验证从结构体改为类后，非法空格子被校验拒绝而不会触发空引用异常。</summary>
        [Test]
        public void NullCellsAndNullCopySourcesAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new MapCellData(null));
            Assert.Throws<ArgumentNullException>(() => new GridCell((MapCellData)null));
            Assert.That(MapData.ValidateData(1, 1, 1, new MapCellData[] { null }, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            MapData map = ScriptableObject.CreateInstance<MapData>();
            try
            {
                map.SetData(1, 1, 1, new[] { new MapCellData(Vector2Int.zero, 1, BlockState.Unblocked) });
                Assert.Throws<ArgumentException>(() => map.SetData(1, 1, 1, new MapCellData[] { null }));
                Assert.That(map.TryValidate(out _), Is.True);
                typeof(MapData).GetField("cells", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(map, new MapCellData[] { null });
                Assert.That(map.TryValidate(out _), Is.False);
                Assert.That(map.TryGetCell(Vector2Int.zero, out MapCellData cell), Is.False);
                Assert.That(cell, Is.Null);
            }
            finally { Object.DestroyImmediate(map); }
        }

        /// <summary>验证原有字段名及内联序列化格式仍可直接读取。</summary>
        [Test]
        public void ExistingSerializedCellFieldsRemainCompatible()
        {
            const string serialized = "{\"width\":1,\"height\":1,\"cellSize\":2," +
                "\"cells\":[{\"coordinates\":{\"x\":0,\"y\":0},\"moveCost\":5," +
                "\"blockState\":1,\"northCover\":1,\"eastCover\":2,\"southCover\":0,\"westCover\":1}]}";
            MapData map = ScriptableObject.CreateInstance<MapData>();
            try
            {
                JsonUtility.FromJsonOverwrite(serialized, map);
                Assert.That(map.TryValidate(out string error), Is.True, error);
                Assert.That(map.TryGetCell(Vector2Int.zero, out MapCellData cell), Is.True);
                Assert.That(cell.MoveCost, Is.EqualTo(5));
                Assert.That(cell.BlockState, Is.EqualTo(BlockState.HalfBlocked));
                Assert.That(cell.NorthCover, Is.EqualTo(CoverType.HalfCover));
                Assert.That(cell.EastCover, Is.EqualTo(CoverType.FullCover));
                Assert.That(cell.SouthCover, Is.EqualTo(CoverType.NoCover));
                Assert.That(cell.WestCover, Is.EqualTo(CoverType.HalfCover));
                var runtime = new GridCell(cell);
                Assert.That(runtime.MoveCost, Is.EqualTo(5));
                Assert.That(runtime.Occupant, Is.Null);
            }
            finally { Object.DestroyImmediate(map); }
        }

        /// <summary>验证构造、复制及旧数据反序列化都统一全障碍代价。</summary>
        [Test]
        public void FullBlockedCostsNormalizeAcrossConstructionAndSerialization()
        {
            var blocked = new MapCellData(Vector2Int.zero, 5, BlockState.FullBlocked);
            Assert.That(blocked.MoveCost, Is.EqualTo(-1));
            Assert.That(blocked.HasValidValues(), Is.True);
            Assert.That(new MapCellData(blocked).MoveCost, Is.EqualTo(-1));

            const string legacyJson = "{\"coordinates\":{\"x\":2,\"y\":3},\"moveCost\":8,\"blockState\":2," +
                "\"northCover\":1,\"eastCover\":2,\"southCover\":0,\"westCover\":1}";
            MapCellData legacy = JsonUtility.FromJson<MapCellData>(legacyJson);
            Assert.That(legacy.MoveCost, Is.EqualTo(-1));
            Assert.That(legacy.HasValidValues(), Is.True);
            Assert.That(legacy.Coordinates, Is.EqualTo(new Vector2Int(2, 3)));
            Assert.That(legacy.EastCover, Is.EqualTo(CoverType.FullCover));
            Assert.That(JsonUtility.ToJson(legacy), Does.Contain("\"moveCost\":-1"));

            var invalid = new MapCellData(Vector2Int.zero, -1, BlockState.HalfBlocked);
            Assert.That(invalid.HasValidValues(), Is.False);
            Assert.That(JsonUtility.FromJson<MapCellData>(JsonUtility.ToJson(invalid)).HasValidValues(), Is.False);
        }
    }
}
