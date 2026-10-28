using System;
using UnityEngine;

namespace Wargame.GridSystem
{
    /// <summary>随场景保存的地图草稿，仅由编辑器修改，不用于运行时初始化。</summary>
    [Serializable]
    internal sealed class GridMapDraft
    {
        [SerializeField, Tooltip("草稿的逻辑宽度。")]
        private int width;
        [SerializeField, Tooltip("草稿的逻辑高度。")]
        private int height;
        [SerializeField, Tooltip("草稿格子；掩体在烘焙时重新计算。")]
        private MapCellData[] cells = Array.Empty<MapCellData>();

        /// <summary>草稿宽度。</summary>
        internal int Width => width;
        /// <summary>草稿高度。</summary>
        internal int Height => height;

        /// <summary>创建指定尺寸的默认草稿。</summary>
        /// <param name="width">初始宽度。</param>
        /// <param name="height">初始高度。</param>
        /// <exception cref="ArgumentOutOfRangeException">宽高无效或格数超出数组上限。</exception>
        internal GridMapDraft(int width, int height)
        {
            Resize(width, height);
        }

        /// <summary>调整草稿尺寸，保留重叠区域，新格采用默认数据。</summary>
        /// <param name="newWidth">新的正数宽度。</param>
        /// <param name="newHeight">新的正数高度。</param>
        /// <exception cref="ArgumentOutOfRangeException">宽高无效或格数超出数组上限。</exception>
        internal void Resize(int newWidth, int newHeight)
        {
            if (newWidth <= 0 || newHeight <= 0 || (long)newWidth * newHeight > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(newWidth), "草稿宽高必须为正数，且格数不能超出数组上限。");
            var resized = new MapCellData[newWidth * newHeight];
            for (int y = 0; y < newHeight; y++)
            for (int x = 0; x < newWidth; x++)
            {
                var coordinates = new Vector2Int(x, y);
                resized[x + y * newWidth] = TryGetCell(coordinates, out MapCellData old)
                    ? old : new MapCellData(coordinates, 1, BlockState.Unblocked);
            }

            cells = resized;
            width = newWidth;
            height = newHeight;
        }

        /// <summary>读取草稿格子，不向外暴露内部数组。</summary>
        /// <param name="coordinates">目标逻辑坐标。</param>
        /// <param name="cell">成功时为只读格子，否则为 null。</param>
        /// <returns>坐标和数组位置均存在时返回 true。</returns>
        internal bool TryGetCell(Vector2Int coordinates, out MapCellData cell)
        {
            cell = default;
            if (coordinates.x < 0 || coordinates.y < 0 || coordinates.x >= width || coordinates.y >= height)
                return false;
            long index = coordinates.x + (long)coordinates.y * width;
            if (cells == null || index >= cells.Length)
                return false;
            cell = cells[(int)index];
            return cell != null;
        }

        /// <summary>修改草稿属性并同步障碍代价；全障碍为 -1，解除后默认恢复为 1。</summary>
        /// <param name="coordinates">目标坐标。</param>
        /// <param name="moveCost">正数进入代价；全障碍忽略此值，解除全障碍时传 -1 会恢复为 1。</param>
        /// <param name="blockState">新的障碍状态。</param>
        /// <returns>请求有效且数据发生变化时返回 true。</returns>
        internal bool TrySetCell(Vector2Int coordinates, int moveCost, BlockState blockState)
        {
            if ((uint)blockState > (uint)BlockState.FullBlocked ||
                !TryGetCell(coordinates, out MapCellData old))
                return false;
            if (blockState == BlockState.FullBlocked)
                moveCost = -1;
            else if (moveCost == -1 && old.BlockState == BlockState.FullBlocked)
                moveCost = 1;
            else if (moveCost < 1)
                return false;
            if (old.MoveCost == moveCost && old.BlockState == blockState)
                return false;
            cells[coordinates.x + coordinates.y * width] = new MapCellData(coordinates, moveCost, blockState);
            return true;
        }

        /// <summary>逐格复制草稿，用于导入校验及烘焙，不共享数组和格子对象。</summary>
        /// <returns>独立的格子数组；缺失数据时返回空数组，非法空条目保留供校验。</returns>
        internal MapCellData[] CopyCells()
        {
            if (cells == null)
                return Array.Empty<MapCellData>();
            var copy = new MapCellData[cells.Length];
            for (int i = 0; i < cells.Length; i++)
                copy[i] = cells[i] == null ? null : new MapCellData(cells[i]);
            return copy;
        }
    }
}
