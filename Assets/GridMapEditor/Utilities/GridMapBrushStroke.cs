using System;
using UnityEditor;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Editor
{
    /// <summary>笔刷修改的属性范围。</summary>
    internal enum GridBrushMode
    {
        /// <summary>同时修改代价和障碍。</summary>
        Both,
        /// <summary>只修改进入代价。</summary>
        MoveCost,
        /// <summary>只修改障碍状态。</summary>
        BlockState
    }

    /// <summary>一次完整的绘制笔画，负责补齐拖动路径和合并撤销记录。</summary>
    internal sealed class GridMapBrushStroke : IDisposable
    {
        private readonly GridManager grid;
        private readonly GridBrushMode mode;
        private readonly int moveCost;
        private readonly BlockState blockState;
        private readonly int undoGroup;
        private Vector2Int? previous;
        private bool disposed;

        /// <summary>开始一次笔画并保存修改前的完整草稿。</summary>
        /// <param name="grid">待绘制棋盘。</param>
        /// <param name="mode">需要修改的属性。</param>
        /// <param name="moveCost">可通行笔刷的正数代价；绘制全障碍时自动使用 -1。</param>
        /// <param name="blockState">笔刷障碍状态。</param>
        /// <exception cref="InvalidOperationException">棋盘当前不可编辑。</exception>
        /// <exception cref="ArgumentOutOfRangeException">笔刷参数无效。</exception>
        internal GridMapBrushStroke(GridManager grid, GridBrushMode mode, int moveCost, BlockState blockState)
        {
            GridMapEditorActions.EnsureEditable(grid);
            bool paintsFullBlocked = mode != GridBrushMode.MoveCost && blockState == BlockState.FullBlocked;
            if ((uint)mode > (uint)GridBrushMode.BlockState || (!paintsFullBlocked && moveCost < 1) ||
                (uint)blockState > (uint)BlockState.FullBlocked)
                throw new ArgumentOutOfRangeException(nameof(mode), "笔刷模式、代价或障碍状态无效。");
            this.grid = grid;
            this.mode = mode;
            this.moveCost = paintsFullBlocked ? -1 : moveCost;
            this.blockState = blockState;
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("绘制棋盘地图");
            Undo.RegisterCompleteObjectUndo(grid, "绘制棋盘地图");
        }

        /// <summary>绘制当前格并用整数直线补齐与上次位置之间的格子。</summary>
        /// <param name="coordinates">当前鼠标指向的格子；越界会中断连续轨迹。</param>
        /// <returns>至少一个格子发生变化时返回 true。</returns>
        internal bool Paint(Vector2Int coordinates)
        {
            if (disposed || !GridMapEditorActions.CanEdit(grid) || grid.Draft == null ||
                !grid.Draft.TryGetCell(coordinates, out _))
            {
                BreakLine();
                return false;
            }

            Vector2Int from = previous ?? coordinates;
            int x = from.x;
            int y = from.y;
            int dx = Math.Abs(coordinates.x - x);
            int dy = -Math.Abs(coordinates.y - y);
            int stepX = x < coordinates.x ? 1 : -1;
            int stepY = y < coordinates.y ? 1 : -1;
            int error = dx + dy;
            bool changed = false;
            while (true)
            {
                changed |= PaintCell(new Vector2Int(x, y));
                if (x == coordinates.x && y == coordinates.y)
                    break;
                int twiceError = 2 * error;
                if (twiceError >= dy) { error += dy; x += stepX; }
                if (twiceError <= dx) { error += dx; y += stepY; }
            }

            previous = coordinates;
            if (changed)
                GridMapEditorActions.MarkChanged(grid);
            return changed;
        }

        /// <summary>中断轨迹连接，防止鼠标离开棋盘后跨越中间格子补画。</summary>
        internal void BreakLine()
        {
            previous = null;
        }

        /// <summary>结束笔画并把全部拖动修改合并为一次撤销。</summary>
        public void Dispose()
        {
            if (disposed)
                return;
            Undo.CollapseUndoOperations(undoGroup);
            Undo.IncrementCurrentGroup();
            disposed = true;
        }

        /// <summary>按模式修改格子；全障碍代价固定为 -1，只解除障碍时恢复为 1。</summary>
        /// <param name="coordinates">目标格子坐标。</param>
        /// <returns>格子数据发生变化时返回 true。</returns>
        private bool PaintCell(Vector2Int coordinates)
        {
            if (!grid.Draft.TryGetCell(coordinates, out MapCellData old))
                return false;
            return grid.Draft.TrySetCell(coordinates,
                mode == GridBrushMode.BlockState ? old.MoveCost : moveCost,
                mode == GridBrushMode.MoveCost ? old.BlockState : blockState);
        }
    }
}
