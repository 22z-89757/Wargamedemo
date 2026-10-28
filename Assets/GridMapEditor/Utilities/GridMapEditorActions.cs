using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Editor
{
    /// <summary>统一处理草稿修改的撤销、场景脏标记及 Prefab 实例覆盖。</summary>
    internal static class GridMapEditorActions
    {
        /// <summary>判断棋盘当前是否允许编辑，播放切换期间也禁止修改。</summary>
        /// <param name="grid">待编辑棋盘。</param>
        /// <returns>有效、未播放且未创建运行时数组时返回 true。</returns>
        internal static bool CanEdit(GridManager grid)
        {
            return grid != null && !EditorApplication.isPlayingOrWillChangePlaymode && !grid.IsInitialized;
        }

        /// <summary>拒绝播放模式或运行时棋盘上的编辑操作。</summary>
        /// <param name="grid">待编辑棋盘。</param>
        /// <exception cref="InvalidOperationException">棋盘当前不可编辑。</exception>
        internal static void EnsureEditable(GridManager grid)
        {
            if (!CanEdit(grid))
                throw new InvalidOperationException("只能在非播放模式编辑尚未初始化的棋盘草稿。");
        }

        /// <summary>记录并修改棋盘草稿尺寸，重叠区域数据保持不变。</summary>
        /// <param name="grid">待编辑棋盘。</param>
        /// <param name="width">新宽度。</param>
        /// <param name="height">新高度。</param>
        /// <param name="cellSize">新的格子尺寸。</param>
        /// <exception cref="InvalidOperationException">棋盘当前不可编辑。</exception>
        /// <exception cref="ArgumentOutOfRangeException">尺寸参数无效。</exception>
        internal static void ResizeDraft(GridManager grid, int width, int height, float cellSize)
        {
            EnsureEditable(grid);
            if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue ||
                float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "宽高和格子尺寸必须有效且为正数。");
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(grid, "调整棋盘尺寸");
            grid.ConfigureDraft(width, height, cellSize);
            MarkChanged(grid);
            Undo.IncrementCurrentGroup();
        }

        /// <summary>将关联地图复制为场景草稿，允许撤销整个导入。</summary>
        /// <param name="grid">持有有效 SourceMap 的棋盘。</param>
        /// <exception cref="InvalidOperationException">棋盘不可编辑或关联资产无效。</exception>
        internal static void ImportDraft(GridManager grid)
        {
            EnsureEditable(grid);
            if (grid.SourceMap == null || !grid.SourceMap.TryValidate(out _))
                throw new InvalidOperationException("请先关联有效的 MapData 地图资产。");
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(grid, "导入地图草稿");
            grid.ImportDraft(grid.SourceMap);
            MarkChanged(grid);
            Undo.IncrementCurrentGroup();
        }

        /// <summary>记录组件修改，确保场景保存及 Prefab 实例覆盖包含编辑结果。</summary>
        /// <param name="grid">已修改的棋盘。</param>
        internal static void MarkChanged(GridManager grid)
        {
            EditorUtility.SetDirty(grid);
            if (PrefabUtility.IsPartOfPrefabInstance(grid))
                PrefabUtility.RecordPrefabInstancePropertyModifications(grid);
            if (grid.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
            SceneView.RepaintAll();
        }
    }
}
