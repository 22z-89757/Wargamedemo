using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Wargame.Editor;
using Wargame.GridSystem;
using Object = UnityEngine.Object;

namespace Wargame.Tests
{
    /// <summary>验证场景草稿、Scene 笔刷、地图烘焙与运行时数据隔离。</summary>
    public sealed class GridMapEditorTests
    {
        private readonly List<string> outputPaths = new List<string>();
        private string testFolder;
        private string uniqueName;
        private Scene testScene;
        private GameObject root;
        private GridManager grid;

        /// <summary>为文件持久化测试创建独立且唯一的临时资产目录。</summary>
        [OneTimeSetUp]
        public void CreateTestFolder()
        {
            uniqueName = "__GridMapTest_" + Guid.NewGuid().ToString("N");
            testFolder = "Assets/" + uniqueName;
            AssetDatabase.CreateFolder("Assets", uniqueName);
        }

        /// <summary>仅删除本次测试记录的资产，不触及现有地图或用户场景。</summary>
        [OneTimeTearDown]
        public void RemoveTestAssets()
        {
            foreach (string path in outputPaths)
            {
                MapData map = AssetDatabase.LoadAssetAtPath<MapData>(path);
                if (map != null)
                    Undo.ClearUndo(map);
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.DeleteAsset(testFolder);
        }

        /// <summary>在测试运行器隔离的临时场景中创建草稿。</summary>
        [SetUp]
        public void SetUp()
        {
            testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("Map Editor Test");
            grid = root.AddComponent<GridManager>();
            GridMapEditorActions.ResizeDraft(grid, 5, 5, 1f);
        }

        /// <summary>清理测试场景及自身撤销记录，用户场景由测试运行器恢复。</summary>
        [TearDown]
        public void TearDown()
        {
            if (grid != null)
                Undo.ClearUndo(grid);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>验证三种笔刷按所选模式修改属性，障碍变化时同步不可通行代价。</summary>
        /// <param name="mode">笔刷模式的枚举序号。</param>
        /// <param name="expectedCost">绘制后预期进入代价。</param>
        /// <param name="expectedBlock">绘制后预期障碍状态。</param>
        [TestCase(0, -1, BlockState.FullBlocked)]
        [TestCase(1, 7, BlockState.HalfBlocked)]
        [TestCase(2, -1, BlockState.FullBlocked)]
        public void BrushModesOnlyChangeSelectedValues(int mode, int expectedCost, BlockState expectedBlock)
        {
            Paint(Vector2Int.one, 3, BlockState.HalfBlocked);
            using (var stroke = new GridMapBrushStroke(grid, (GridBrushMode)mode, 7, BlockState.FullBlocked))
                Assert.That(stroke.Paint(Vector2Int.one), Is.True);
            MapCellData cell = DraftCell(Vector2Int.one);
            Assert.That(cell.MoveCost, Is.EqualTo(expectedCost));
            Assert.That(cell.BlockState, Is.EqualTo(expectedBlock));
            Assert.That(DraftCell(Vector2Int.zero).MoveCost, Is.EqualTo(1));
            Assert.That(testScene.isDirty, Is.True);
        }

        /// <summary>验证代价笔刷不覆盖全障碍标记，障碍笔刷解除后恢复默认代价并支持撤销重做。</summary>
        [Test]
        public void BrushesPreserveBlockedCostsAndUndoStateTransitions()
        {
            Paint(Vector2Int.one, -1, BlockState.FullBlocked);
            using (var stroke = new GridMapBrushStroke(grid, GridBrushMode.MoveCost, 8, BlockState.Unblocked))
                Assert.That(stroke.Paint(Vector2Int.one), Is.False);
            Assert.That(DraftCell(Vector2Int.one).MoveCost, Is.EqualTo(-1));
            Assert.That(DraftCell(Vector2Int.one).BlockState, Is.EqualTo(BlockState.FullBlocked));

            using (var stroke = new GridMapBrushStroke(grid, GridBrushMode.BlockState, 8, BlockState.HalfBlocked))
                Assert.That(stroke.Paint(Vector2Int.one), Is.True);
            Assert.That(DraftCell(Vector2Int.one).MoveCost, Is.EqualTo(1));
            Assert.That(DraftCell(Vector2Int.one).BlockState, Is.EqualTo(BlockState.HalfBlocked));
            Undo.PerformUndo();
            Assert.That(DraftCell(Vector2Int.one).MoveCost, Is.EqualTo(-1));
            Assert.That(DraftCell(Vector2Int.one).BlockState, Is.EqualTo(BlockState.FullBlocked));
            Undo.PerformRedo();
            Assert.That(DraftCell(Vector2Int.one).MoveCost, Is.EqualTo(1));
            Assert.That(DraftCell(Vector2Int.one).BlockState, Is.EqualTo(BlockState.HalfBlocked));
        }

        /// <summary>验证稀疏拖动事件会补齐路径，且整个笔画只需一次撤销。</summary>
        [Test]
        public void StrokeFillsGapsAndUndoesAsOneOperation()
        {
            using (var stroke = new GridMapBrushStroke(grid, GridBrushMode.Both, 4, BlockState.HalfBlocked))
            {
                stroke.Paint(Vector2Int.zero);
                stroke.Paint(new Vector2Int(4, 0));
                stroke.Paint(new Vector2Int(4, 4));
            }

            for (int i = 0; i < 5; i++)
            {
                Assert.That(DraftCell(new Vector2Int(i, 0)).MoveCost, Is.EqualTo(4));
                Assert.That(DraftCell(new Vector2Int(4, i)).BlockState, Is.EqualTo(BlockState.HalfBlocked));
            }

            Undo.PerformUndo();
            for (int i = 0; i < 5; i++)
            {
                Assert.That(DraftCell(new Vector2Int(i, 0)).MoveCost, Is.EqualTo(1));
                Assert.That(DraftCell(new Vector2Int(4, i)).BlockState, Is.EqualTo(BlockState.Unblocked));
            }

            Undo.PerformRedo();
            Assert.That(DraftCell(new Vector2Int(4, 4)).MoveCost, Is.EqualTo(4));
        }

        /// <summary>验证离开棋盘后重新进入不会补画两点之间的格子。</summary>
        [Test]
        public void LeavingBoardBreaksTheStrokeLine()
        {
            using (var stroke = new GridMapBrushStroke(grid, GridBrushMode.Both, 2, BlockState.FullBlocked))
            {
                stroke.Paint(Vector2Int.zero);
                Assert.That(stroke.Paint(new Vector2Int(-1, 0)), Is.False);
                stroke.Paint(new Vector2Int(4, 0));
            }

            Assert.That(DraftCell(new Vector2Int(2, 0)).BlockState, Is.EqualTo(BlockState.Unblocked));
            Assert.That(DraftCell(new Vector2Int(4, 0)).BlockState, Is.EqualTo(BlockState.FullBlocked));
        }

        /// <summary>验证尺寸调整保留重叠格，新增格默认初始化，撤销后恢复裁剪内容。</summary>
        [Test]
        public void ResizePreservesOverlapAndUndoRestoresCroppedCells()
        {
            Paint(new Vector2Int(4, 4), 9, BlockState.FullBlocked);
            Paint(Vector2Int.one, 3, BlockState.HalfBlocked);
            GridMapEditorActions.ResizeDraft(grid, 2, 2, 2f);
            Assert.That(grid.Width, Is.EqualTo(2));
            Assert.That(grid.CellSize, Is.EqualTo(2f));
            Assert.That(DraftCell(Vector2Int.one).MoveCost, Is.EqualTo(3));
            Undo.PerformUndo();
            Assert.That(grid.Width, Is.EqualTo(5));
            Assert.That(grid.CellSize, Is.EqualTo(1f));
            Assert.That(DraftCell(new Vector2Int(4, 4)).MoveCost, Is.EqualTo(-1));
            Undo.PerformRedo();
            Assert.That(grid.Width, Is.EqualTo(2));
            GridMapEditorActions.ResizeDraft(grid, 3, 4, 1f);
            Assert.That(DraftCell(new Vector2Int(2, 3)).MoveCost, Is.EqualTo(1));
            Assert.That(DraftCell(new Vector2Int(2, 3)).BlockState, Is.EqualTo(BlockState.Unblocked));
            Assert.That(DraftCell(Vector2Int.one).MoveCost, Is.EqualTo(3));
        }

        /// <summary>验证草稿真正写入场景文件，并在关闭重开后恢复。</summary>
        [Test]
        public void DraftSurvivesSceneSaveAndReload()
        {
            GridMapEditorActions.ResizeDraft(grid, 3, 4, 2f);
            Paint(new Vector2Int(2, 3), 6, BlockState.HalfBlocked);
            Paint(Vector2Int.one, -1, BlockState.FullBlocked);
            string path = testFolder + "/Draft.unity";
            Assert.That(EditorSceneManager.SaveScene(testScene, path), Is.True);
            testScene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            SceneManager.SetActiveScene(testScene);
            root = testScene.GetRootGameObjects()[0];
            grid = root.GetComponent<GridManager>();
            Assert.That(grid.Width, Is.EqualTo(3));
            Assert.That(grid.Height, Is.EqualTo(4));
            Assert.That(grid.CellSize, Is.EqualTo(2f));
            Assert.That(DraftCell(new Vector2Int(2, 3)).MoveCost, Is.EqualTo(6));
            Assert.That(DraftCell(new Vector2Int(2, 3)).BlockState, Is.EqualTo(BlockState.HalfBlocked));
            Assert.That(DraftCell(Vector2Int.one).MoveCost, Is.EqualTo(-1));
            Assert.That(DraftCell(Vector2Int.one).BlockState, Is.EqualTo(BlockState.FullBlocked));
            Assert.That(grid.IsInitialized, Is.False);
        }

        /// <summary>验证笔刷会记录 Prefab 实例的嵌套草稿覆盖。</summary>
        [Test]
        public void PaintingPrefabInstanceRecordsOverrides()
        {
            string prefabPath = testFolder + "/Grid.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, testScene);
            GridManager instanceGrid = instance.GetComponent<GridManager>();
            using (var stroke = new GridMapBrushStroke(instanceGrid, GridBrushMode.Both, 8, BlockState.FullBlocked))
                stroke.Paint(Vector2Int.one);
            Assert.That(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false), Is.True);
            var originalGrid = prefab.GetComponent<GridManager>();
            originalGrid.Draft.TryGetCell(Vector2Int.one, out MapCellData original);
            Assert.That(original.MoveCost, Is.EqualTo(1));
            instanceGrid.Draft.TryGetCell(Vector2Int.one, out MapCellData painted);
            Assert.That(painted.MoveCost, Is.EqualTo(-1));
            Undo.ClearUndo(instanceGrid);
        }

        /// <summary>验证四方向都按紧邻格推导，包含全障碍本格和棋盘外边界。</summary>
        [Test]
        public void CoversUseNeighborStatesForEveryCell()
        {
            GridMapEditorActions.ResizeDraft(grid, 3, 3, 1f);
            Paint(new Vector2Int(1, 2), 2, BlockState.HalfBlocked);
            Paint(new Vector2Int(2, 1), 3, BlockState.FullBlocked);
            Paint(new Vector2Int(0, 1), 4, BlockState.HalfBlocked);
            Paint(Vector2Int.one, 5, BlockState.FullBlocked);
            MapCellData[] baked = GridMapBaker.BuildCells(grid);
            MapCellData center = baked[4];
            Assert.That(center.BlockState, Is.EqualTo(BlockState.FullBlocked));
            Assert.That(center.MoveCost, Is.EqualTo(-1));
            Assert.That(center.NorthCover, Is.EqualTo(CoverType.HalfCover));
            Assert.That(center.EastCover, Is.EqualTo(CoverType.FullCover));
            Assert.That(center.SouthCover, Is.EqualTo(CoverType.NoCover));
            Assert.That(center.WestCover, Is.EqualTo(CoverType.HalfCover));
            Assert.That(baked[0].SouthCover, Is.EqualTo(CoverType.NoCover));
            Assert.That(baked[0].WestCover, Is.EqualTo(CoverType.NoCover));
            Assert.That(baked[8].NorthCover, Is.EqualTo(CoverType.NoCover));
            Assert.That(baked[8].EastCover, Is.EqualTo(CoverType.NoCover));
            Assert.That(baked[1].NorthCover, Is.EqualTo(CoverType.FullCover));
            Assert.That(DraftCell(Vector2Int.one).NorthCover, Is.EqualTo(CoverType.NoCover));
        }

        /// <summary>验证重复烘焙保留同一资产和 GUID，另存创建独立资产。</summary>
        [Test]
        public void BakingUpdatesGuidAndSaveAsCreatesAnotherAsset()
        {
            Paint(Vector2Int.one, 2, BlockState.HalfBlocked);
            MapData first = Bake();
            string path = AssetDatabase.GetAssetPath(first);
            string guid = AssetDatabase.AssetPathToGUID(path);
            Assert.That(path, Does.StartWith("Assets/MapData/"));
            Assert.That(grid.SourceMap, Is.SameAs(first));
            Assert.That(first.CellCount, Is.EqualTo(25));
            Assert.That(first.TryValidate(out _), Is.True);
            Paint(Vector2Int.one, 7, BlockState.FullBlocked);
            MapData updated = Bake();
            Assert.That(updated, Is.SameAs(first));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(MapCell(updated, Vector2Int.one).MoveCost, Is.EqualTo(-1));
            MapData savedAs = Bake(true);
            Assert.That(savedAs, Is.Not.SameAs(first));
            Assert.That(AssetDatabase.GetAssetPath(savedAs), Is.Not.EqualTo(path));
            Assert.That(grid.SourceMap, Is.SameAs(savedAs));
        }

        /// <summary>验证创建同名地图时自动使用唯一文件名。</summary>
        [Test]
        public void NewMapsNeverOverwriteAnExistingFilename()
        {
            MapData first = Bake(true);
            MapData second = Bake(true);
            Assert.That(AssetDatabase.GetAssetPath(second), Is.Not.EqualTo(AssetDatabase.GetAssetPath(first)));
            Assert.That(first.TryValidate(out _), Is.True);
        }

        /// <summary>验证关联目录外的资产时只在输出目录创建副本。</summary>
        [Test]
        public void BakingDoesNotOverwriteAnExternalAsset()
        {
            MapData external = ScriptableObject.CreateInstance<MapData>();
            external.SetData(grid.Width, grid.Height, grid.CellSize, GridMapBaker.BuildCells(grid));
            string path = testFolder + "/External.asset";
            AssetDatabase.CreateAsset(external, path);
            AssetDatabase.SaveAssetIfDirty(external);
            string before = File.ReadAllText(path);
            grid.SourceMap = external;
            Paint(Vector2Int.zero, 8, BlockState.FullBlocked);
            MapData output = Bake();
            Assert.That(output, Is.Not.SameAs(external));
            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
            Assert.That(MapCell(external, Vector2Int.zero).MoveCost, Is.EqualTo(1));
        }

        /// <summary>验证烘焙资产的 Undo/Redo 会同步恢复磁盘内容。</summary>
        [Test]
        public void BakeUndoRedoRestoresAssetAndDiskContents()
        {
            Paint(Vector2Int.zero, 2, BlockState.HalfBlocked);
            MapData map = Bake();
            string path = AssetDatabase.GetAssetPath(map);
            Paint(Vector2Int.zero, 7, BlockState.FullBlocked);
            Bake();
            Assert.That(MapCell(map, Vector2Int.zero).MoveCost, Is.EqualTo(-1));
            Undo.PerformUndo();
            Assert.That(MapCell(map, Vector2Int.zero).MoveCost, Is.EqualTo(2));
            Assert.That(File.ReadAllText(path), Does.Contain("moveCost: 2"));
            Assert.That(File.ReadAllText(path), Does.Not.Contain("moveCost: -1"));
            Undo.PerformRedo();
            Assert.That(MapCell(map, Vector2Int.zero).MoveCost, Is.EqualTo(-1));
            Assert.That(File.ReadAllText(path), Does.Contain("moveCost: -1"));
        }

        /// <summary>验证无效草稿在修改资产前失败，原内容与 GUID 保持不变。</summary>
        [Test]
        public void InvalidDraftDoesNotOverwriteMap()
        {
            MapData map = Bake();
            string path = AssetDatabase.GetAssetPath(map);
            string before = File.ReadAllText(path);
            string guid = AssetDatabase.AssetPathToGUID(path);
            var serialized = new SerializedObject(grid);
            serialized.FindProperty("draft").FindPropertyRelative("cells").GetArrayElementAtIndex(0)
                .FindPropertyRelative("moveCost").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.Throws<InvalidOperationException>(() => GridMapBaker.Bake(grid, uniqueName));
            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(MapCell(map, Vector2Int.zero).MoveCost, Is.EqualTo(1));
        }

        /// <summary>验证非法笔刷、尺寸及新资产名称被拒绝。</summary>
        [Test]
        public void InvalidEditorInputsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridMapBrushStroke(grid, GridBrushMode.Both, 0, BlockState.Unblocked));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridMapBrushStroke(grid, (GridBrushMode)99, 1, BlockState.Unblocked));
            Assert.Throws<ArgumentOutOfRangeException>(() => GridMapEditorActions.ResizeDraft(grid, 0, 5, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => GridMapEditorActions.ResizeDraft(grid, 5, 5, float.NaN));
            Assert.Throws<ArgumentException>(() => GridMapBaker.Bake(grid, "../Invalid", true));
            Assert.That(grid.Width, Is.EqualTo(5));
            Assert.That(grid.SourceMap, Is.Null);
        }

        /// <summary>验证导入复制尺寸及可编辑属性，修改草稿不会反写原地图。</summary>
        [Test]
        public void ImportCopiesMapAndSupportsUndo()
        {
            GridMapEditorActions.ResizeDraft(grid, 3, 4, 2f);
            Paint(new Vector2Int(2, 3), 6, BlockState.FullBlocked);
            MapData map = Bake();
            GridMapEditorActions.ResizeDraft(grid, 2, 2, 1f);
            GridMapEditorActions.ImportDraft(grid);
            Assert.That(grid.Width, Is.EqualTo(3));
            Assert.That(grid.Height, Is.EqualTo(4));
            Assert.That(grid.CellSize, Is.EqualTo(2f));
            Assert.That(DraftCell(new Vector2Int(2, 3)).MoveCost, Is.EqualTo(-1));
            Undo.PerformUndo();
            Assert.That(grid.Width, Is.EqualTo(2));
            Undo.PerformRedo();
            Assert.That(grid.Width, Is.EqualTo(3));
            Paint(new Vector2Int(2, 3), 9, BlockState.Unblocked);
            Assert.That(MapCell(map, new Vector2Int(2, 3)).MoveCost, Is.EqualTo(-1));
        }

        /// <summary>验证加载复制全部静态属性，且运行时变化不会修改资产。</summary>
        [Test]
        public void RuntimeLoadCopiesDataAndPreservesWeightedSearch()
        {
            GridMapEditorActions.ResizeDraft(grid, 3, 2, 2f);
            Paint(Vector2Int.right, 9, BlockState.HalfBlocked);
            MapData map = Bake();
            root.transform.position = new Vector3(7, 0, 3);
            grid.Initialize(map);
            Assert.That(grid.CellSize, Is.EqualTo(2f));
            Assert.That(grid.GridToWorld(Vector2Int.zero), Is.EqualTo(new Vector3(8, 0, 4)));
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var position = new Vector2Int(x, y);
                Assert.That(grid.TryGetCell(position, out GridCell cell), Is.True);
                MapCellData source = MapCell(map, position);
                Assert.That(cell.MoveCost, Is.EqualTo(source.MoveCost));
                Assert.That(cell.BlockState, Is.EqualTo(source.BlockState));
                Assert.That(cell.NorthCover, Is.EqualTo(source.NorthCover));
                Assert.That(cell.EastCover, Is.EqualTo(source.EastCover));
                Assert.That(cell.SouthCover, Is.EqualTo(source.SouthCover));
                Assert.That(cell.WestCover, Is.EqualTo(source.WestCover));
                Assert.That(cell.Occupant, Is.Null);
            }

            Assert.That(grid.GetReachableCells(Vector2Int.zero, 4).ReachableCosts[new Vector2Int(2, 0)], Is.EqualTo(4));
            grid.TrySetMoveCost(Vector2Int.right, 1);
            grid.TrySetBlockState(Vector2Int.right, BlockState.FullBlocked);
            grid.TrySetCover(Vector2Int.zero, GridDirection.East, CoverType.FullCover);
            Assert.That(MapCell(map, Vector2Int.right).MoveCost, Is.EqualTo(9));
            Assert.That(MapCell(map, Vector2Int.right).BlockState, Is.EqualTo(BlockState.HalfBlocked));
            Assert.That(MapCell(map, Vector2Int.zero).EastCover, Is.EqualTo(CoverType.HalfCover));
            Assert.Throws<InvalidOperationException>(() => grid.Initialize(map));
            Assert.That(GridMapEditorActions.CanEdit(grid), Is.False);
            Assert.Throws<InvalidOperationException>(() => GridMapBaker.Bake(grid, uniqueName));
            Assert.Throws<InvalidOperationException>(() => GridMapEditorActions.ImportDraft(grid));
        }

        /// <summary>验证全障碍代价写入资产并正确加载，解除运行时障碍不会反写资产。</summary>
        [Test]
        public void BakedBlockedCostsLoadAndRemainIndependent()
        {
            GridMapEditorActions.ResizeDraft(grid, 3, 2, 1f);
            Paint(Vector2Int.right, -1, BlockState.FullBlocked);
            MapData map = Bake();
            Assert.That(File.ReadAllText(AssetDatabase.GetAssetPath(map)), Does.Contain("moveCost: -1"));
            grid.Initialize(map);
            grid.TryGetCell(Vector2Int.right, out GridCell blocked);
            Assert.That(blocked.MoveCost, Is.EqualTo(-1));
            Assert.That(blocked.Occupant, Is.Null);
            Assert.That(grid.GetReachableCells(Vector2Int.zero, 4).ReachableCosts[new Vector2Int(2, 0)], Is.EqualTo(4));

            Assert.That(grid.TrySetBlockState(Vector2Int.right, BlockState.Unblocked), Is.True);
            Assert.That(blocked.MoveCost, Is.EqualTo(1));
            Assert.That(grid.GetReachableCells(Vector2Int.zero, 2).ReachableCosts[new Vector2Int(2, 0)], Is.EqualTo(2));
            Assert.That(MapCell(map, Vector2Int.right).MoveCost, Is.EqualTo(-1));
            Assert.That(MapCell(map, Vector2Int.right).BlockState, Is.EqualTo(BlockState.FullBlocked));
        }

        /// <summary>验证 Awake 优先加载已关联地图，未关联时不使用未烘焙草稿。</summary>
        [Test]
        public void AwakeUsesBakedMapAndIgnoresUnbakedDraft()
        {
            Paint(Vector2Int.zero, 4, BlockState.HalfBlocked);
            MapData map = Bake();
            Paint(Vector2Int.zero, 9, BlockState.FullBlocked);
            InvokeAwake(grid);
            Assert.That(grid.TryGetCell(Vector2Int.zero, out GridCell loaded), Is.True);
            Assert.That(loaded.MoveCost, Is.EqualTo(4));
            Assert.That(loaded.BlockState, Is.EqualTo(BlockState.HalfBlocked));

            var other = new GameObject("Unbaked Grid");
            other.transform.SetParent(root.transform);
            GridManager unbaked = other.AddComponent<GridManager>();
            unbaked.ConfigureDraft(2, 2, 1);
            unbaked.Draft.TrySetCell(Vector2Int.zero, 8, BlockState.FullBlocked);
            InvokeAwake(unbaked);
            unbaked.TryGetCell(Vector2Int.zero, out GridCell empty);
            Assert.That(empty.MoveCost, Is.EqualTo(1));
            Assert.That(empty.BlockState, Is.EqualTo(BlockState.Unblocked));
            Assert.That(MapCell(map, Vector2Int.zero).MoveCost, Is.EqualTo(4));
        }

        /// <summary>验证非法地图不会部分初始化棋盘，资产写入也会先完整校验。</summary>
        [Test]
        public void InvalidMapDoesNotPartiallyInitializeOrOverwrite()
        {
            MapData invalid = ScriptableObject.CreateInstance<MapData>();
            try
            {
                Assert.Throws<ArgumentNullException>(() => grid.Initialize((MapData)null));
                Assert.Throws<ArgumentException>(() => grid.Initialize(invalid));
                Assert.That(grid.IsInitialized, Is.False);
                MapCellData[] validCells = GridMapBaker.BuildCells(grid);
                invalid.SetData(5, 5, 1, validCells);
                validCells[0] = new MapCellData(Vector2Int.zero, 0, BlockState.Unblocked);
                Assert.That(MapCell(invalid, Vector2Int.zero).MoveCost, Is.EqualTo(1));
                Assert.Throws<ArgumentException>(() => invalid.SetData(5, 5, 1, validCells));
                Assert.That(invalid.TryValidate(out _), Is.True);
                Assert.That(MapCell(invalid, Vector2Int.zero).MoveCost, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(invalid); }
        }

        /// <summary>验证 Scene 拾取遵循棋盘原点、尺寸和最大边界排除规则。</summary>
        [Test]
        public void ScenePickingDoesNotNeedColliders()
        {
            root.transform.position = new Vector3(4, 2, -3);
            Assert.That(GridManagerEditor.TryGetDraftCoordinates(grid,
                new Ray(new Vector3(5.5f, 20, -0.5f), Vector3.down), out Vector2Int coordinates), Is.True);
            Assert.That(coordinates, Is.EqualTo(new Vector2Int(1, 2)));
            Assert.That(GridManagerEditor.TryGetDraftCoordinates(grid,
                new Ray(new Vector3(9, 20, -3), Vector3.down), out _), Is.False);
            Assert.That(GridManagerEditor.TryGetDraftCoordinates(grid,
                new Ray(new Vector3(3.99f, 20, -3), Vector3.down), out _), Is.False);
            Assert.That(GridManagerEditor.TryGetDraftCoordinates(grid,
                new Ray(Vector3.zero, Vector3.right), out _), Is.False);
        }

        /// <summary>在 EditMode 中调用生命周期逻辑，避免 SendMessage 触发原生播放状态断言。</summary>
        /// <param name="targetGrid">待验证的棋盘。</param>
        private static void InvokeAwake(GridManager targetGrid)
        {
            typeof(GridManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(targetGrid, null);
        }

        /// <summary>通过完整笔画修改单个测试格子。</summary>
        /// <param name="coordinates">待修改坐标。</param>
        /// <param name="cost">新的进入代价。</param>
        /// <param name="state">新的障碍状态。</param>
        private void Paint(Vector2Int coordinates, int cost, BlockState state)
        {
            using (var stroke = new GridMapBrushStroke(grid, GridBrushMode.Both, cost, state))
                stroke.Paint(coordinates);
        }

        /// <summary>烘焙并记录输出文件，供测试完成后精确清理。</summary>
        /// <param name="saveAsNew">是否创建新资产。</param>
        /// <returns>本次烘焙的地图资产。</returns>
        private MapData Bake(bool saveAsNew = false)
        {
            MapData map = GridMapBaker.Bake(grid, uniqueName, saveAsNew);
            string path = AssetDatabase.GetAssetPath(map);
            if (!outputPaths.Contains(path))
                outputPaths.Add(path);
            return map;
        }

        /// <summary>读取草稿格子并断言查询成功。</summary>
        /// <param name="coordinates">草稿内坐标。</param>
        /// <returns>只读地图格子。</returns>
        private MapCellData DraftCell(Vector2Int coordinates)
        {
            Assert.That(grid.Draft.TryGetCell(coordinates, out MapCellData cell), Is.True);
            return cell;
        }

        /// <summary>读取地图资产格子并断言查询成功。</summary>
        /// <param name="map">地图资产。</param>
        /// <param name="coordinates">地图内坐标。</param>
        /// <returns>只读地图格子。</returns>
        private static MapCellData MapCell(MapData map, Vector2Int coordinates)
        {
            Assert.That(map.TryGetCell(coordinates, out MapCellData cell), Is.True);
            return cell;
        }
    }
}
