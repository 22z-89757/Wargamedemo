using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Editor
{
    /// <summary>从场景草稿计算四边掩体，并保存到固定地图目录。</summary>
    internal static class GridMapBaker
    {
        /// <summary>所有烘焙地图的项目相对目录。</summary>
        internal const string OutputFolder = "Assets/MapData";

        /// <summary>完整校验草稿并计算所有格子的掩体，不修改场景或资产。</summary>
        /// <param name="grid">包含有效草稿的棋盘。</param>
        /// <returns>按 x + y * width 排列的烘焙格子。</returns>
        /// <exception cref="InvalidOperationException">棋盘不可编辑或草稿无效。</exception>
        internal static MapCellData[] BuildCells(GridManager grid)
        {
            GridMapEditorActions.EnsureEditable(grid);
            if (grid.Draft == null || grid.Draft.Width != grid.Width || grid.Draft.Height != grid.Height)
                throw new InvalidOperationException("草稿尺寸与棋盘不一致，请先调整棋盘尺寸。");
            MapCellData[] cells = grid.Draft.CopyCells();
            if (!MapData.ValidateData(grid.Width, grid.Height, grid.CellSize, cells, out string error))
                throw new InvalidOperationException(error);

            for (int i = 0; i < cells.Length; i++)
            {
                MapCellData cell = cells[i];
                Vector2Int position = cell.Coordinates;
                cells[i] = new MapCellData(position, cell.MoveCost, cell.BlockState,
                    GetCover(grid, position + Vector2Int.up), GetCover(grid, position + Vector2Int.right),
                    GetCover(grid, position + Vector2Int.down), GetCover(grid, position + Vector2Int.left));
            }

            return cells;
        }

        /// <summary>烘焙到 Assets/MapData；默认更新当前目录内的关联资产并保留 GUID。</summary>
        /// <param name="grid">待烘焙棋盘，成功后自动关联输出资产。</param>
        /// <param name="mapName">新建资产时使用的文件名，不包含扩展名和目录。</param>
        /// <param name="saveAsNew">为 true 时始终创建新资产。</param>
        /// <returns>保存成功的地图资产。</returns>
        /// <exception cref="InvalidOperationException">棋盘不可编辑或草稿无效。</exception>
        /// <exception cref="ArgumentException">新资产文件名无效。</exception>
        internal static MapData Bake(GridManager grid, string mapName, bool saveAsNew = false)
        {
            MapCellData[] cells = BuildCells(grid);
            string existingPath = grid.SourceMap == null ? string.Empty : AssetDatabase.GetAssetPath(grid.SourceMap);
            bool updateExisting = !saveAsNew && existingPath.StartsWith(OutputFolder + "/", StringComparison.Ordinal);
            string path = existingPath;
            if (!updateExisting)
            {
                mapName = (mapName ?? string.Empty).Trim();
                if (mapName.Length == 0 || mapName == "." || mapName == ".." || mapName.EndsWith(".") ||
                    mapName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || mapName.Contains("/") || mapName.Contains("\\"))
                    throw new ArgumentException("地图名称不能为空，且不能包含目录或非法文件名字符。", nameof(mapName));
                if (!AssetDatabase.IsValidFolder(OutputFolder))
                    AssetDatabase.CreateFolder("Assets", "MapData");
                path = AssetDatabase.GenerateUniqueAssetPath(OutputFolder + "/" + mapName + ".asset");
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("烘焙地图数据");
            MapData output = updateExisting ? grid.SourceMap : ScriptableObject.CreateInstance<MapData>();
            if (updateExisting)
                Undo.RegisterCompleteObjectUndo(output, "烘焙地图数据");
            output.SetData(grid.Width, grid.Height, grid.CellSize, cells);
            if (!updateExisting)
                AssetDatabase.CreateAsset(output, path);
            EditorUtility.SetDirty(output);
            AssetDatabase.SaveAssetIfDirty(output);
            GridMapUndoPersistence.Track(output);

            Undo.RegisterCompleteObjectUndo(grid, "关联烘焙地图");
            grid.SourceMap = output;
            GridMapEditorActions.MarkChanged(grid);
            Undo.CollapseUndoOperations(group);
            Undo.IncrementCurrentGroup();
            return output;
        }

        /// <summary>按相邻格障碍推导本格对应侧的掩体，棋盘外视为无掩体。</summary>
        /// <param name="grid">草稿所在棋盘。</param>
        /// <param name="neighbor">对应方向紧邻格子的坐标。</param>
        /// <returns>邻格为半障碍或全障碍时返回对应掩体，否则为无掩体。</returns>
        private static CoverType GetCover(GridManager grid, Vector2Int neighbor)
        {
            if (!grid.Draft.TryGetCell(neighbor, out MapCellData cell))
                return CoverType.NoCover;
            return cell.BlockState == BlockState.FullBlocked ? CoverType.FullCover :
                cell.BlockState == BlockState.HalfBlocked ? CoverType.HalfCover : CoverType.NoCover;
        }
    }

    /// <summary>撤销或重做烘焙后，及时把恢复的地图内容保存回资产。</summary>
    [InitializeOnLoad]
    internal static class GridMapUndoPersistence
    {
        private const string TrackedMapsKey = "Wargame.GridMapBaker.TrackedMaps";
        private static readonly HashSet<string> TrackedGuids = new HashSet<string>(
            SessionState.GetString(TrackedMapsKey, string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

        /// <summary>在编辑器加载时注册地图资产的撤销保存回调。</summary>
        static GridMapUndoPersistence()
        {
            Undo.undoRedoPerformed += SaveRestoredMaps;
        }

        /// <summary>记录本会话实际烘焙的地图，域重载后仍可恢复其撤销保存。</summary>
        /// <param name="map">已经保存的烘焙输出资产。</param>
        internal static void Track(MapData map)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(map));
            if (!string.IsNullOrEmpty(guid) && TrackedGuids.Add(guid))
                SessionState.SetString(TrackedMapsKey, string.Join(";", TrackedGuids));
        }

        /// <summary>仅保存本工具烘焙过且在撤销重做后变脏的地图，并刷新预览。</summary>
        private static void SaveRestoredMaps()
        {
            foreach (string guid in TrackedGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                    continue;
                MapData map = AssetDatabase.LoadAssetAtPath<MapData>(path);
                if (map != null && EditorUtility.IsDirty(map))
                    AssetDatabase.SaveAssetIfDirty(map);
            }

            SceneView.RepaintAll();
        }
    }
}
