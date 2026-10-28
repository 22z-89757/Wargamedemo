using System;
using UnityEngine;

namespace Wargame.GridSystem
{
    /// <summary>烘焙后的只读地图资产，格子按 x + y * Width 顺序保存。</summary>
    public sealed class MapData : ScriptableObject
    {
        [SerializeField, Tooltip("沿世界 X 轴的格数。")]
        private int width;
        [SerializeField, Tooltip("沿世界 Z 轴的格数。")]
        private int height;
        [SerializeField, Tooltip("格子的世界尺寸。")]
        private float cellSize = 1f;
        [SerializeField, Tooltip("包含坐标、代价、障碍和四边掩体的全部格子。")]
        private MapCellData[] cells = Array.Empty<MapCellData>();

        /// <summary>地图宽度。</summary>
        public int Width => width;
        /// <summary>地图高度。</summary>
        public int Height => height;
        /// <summary>格子的世界尺寸。</summary>
        public float CellSize => cellSize;
        /// <summary>序列化的格子总数。</summary>
        public int CellCount => cells == null ? 0 : cells.Length;

        /// <summary>读取只读地图格子，运行时修改应先构造独立的 GridCell 副本。</summary>
        /// <param name="coordinates">地图内的逻辑坐标。</param>
        /// <param name="cell">成功时为格子快照，失败时为默认值。</param>
        /// <returns>坐标及对应序列化条目有效时返回 true。</returns>
        public bool TryGetCell(Vector2Int coordinates, out MapCellData cell)
        {
            cell = default;
            if (coordinates.x < 0 || coordinates.y < 0 || coordinates.x >= width || coordinates.y >= height)
                return false;
            long index = coordinates.x + (long)coordinates.y * width;
            if (cells == null || index >= cells.Length)
                return false;
            MapCellData candidate = cells[(int)index];
            if (candidate == null || candidate.Coordinates != coordinates || !candidate.HasValidValues())
                return false;
            cell = candidate;
            return true;
        }

        /// <summary>检查完整地图，供加载和编辑器诊断使用。</summary>
        /// <param name="error">无效时为中文原因，有效时为空字符串。</param>
        /// <returns>尺寸、格数、坐标及格子属性全部有效时返回 true。</returns>
        public bool TryValidate(out string error)
        {
            return ValidateData(width, height, cellSize, cells, out error);
        }

        /// <summary>校验并复制完整地图，失败时保留资产原数据。</summary>
        /// <param name="width">新地图宽度。</param>
        /// <param name="height">新地图高度。</param>
        /// <param name="cellSize">格子的世界尺寸。</param>
        /// <param name="cells">按 x + y * width 排列的全部格子。</param>
        /// <exception cref="ArgumentException">地图尺寸、格数、坐标或格子属性无效。</exception>
        internal void SetData(int width, int height, float cellSize, MapCellData[] cells)
        {
            if (!ValidateData(width, height, cellSize, cells, out string error))
                throw new ArgumentException(error, nameof(cells));
            var copy = new MapCellData[cells.Length];
            for (int i = 0; i < cells.Length; i++)
                copy[i] = new MapCellData(cells[i]);
            this.width = width;
            this.height = height;
            this.cellSize = cellSize;
            this.cells = copy;
        }

        /// <summary>验证待写入或加载的地图，避免部分初始化。</summary>
        /// <param name="width">地图宽度。</param>
        /// <param name="height">地图高度。</param>
        /// <param name="cellSize">格子的世界尺寸。</param>
        /// <param name="cells">全部格子，坐标必须与数组位置一致。</param>
        /// <param name="error">无效原因；有效时为空字符串。</param>
        /// <returns>地图数据完整合法时返回 true。</returns>
        internal static bool ValidateData(int width, int height, float cellSize, MapCellData[] cells,
            out string error)
        {
            error = string.Empty;
            if (width <= 0 || height <= 0 || float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0)
                error = "地图宽高及格子尺寸必须为有限正数。";
            else if (cells == null || (long)width * height != cells.Length)
                error = "地图格子数量与宽高不一致。";
            else
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i] == null || cells[i].Coordinates != new Vector2Int(i % width, i / width) || !cells[i].HasValidValues())
                    {
                        error = $"第 {i} 个格子的坐标、代价或枚举值无效。";
                        break;
                    }
                }
            }

            return error.Length == 0;
        }
    }
}
