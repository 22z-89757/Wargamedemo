using UnityEngine;
using Wargame.Units;

namespace Wargame.GridSystem
{
    /// <summary>继承地图属性的运行时格子，仅额外保存单位占用，由 GridManager 修改局内副本。</summary>
    public sealed class GridCell : MapCellData
    {
        /// <summary>当前占用单位；空格或单位已销毁时为 Unity 空引用。</summary>
        public Unit Occupant { get; internal set; }

        /// <summary>创建无障碍、无掩体、无单位且进入代价为 1 的格子。</summary>
        /// <param name="coordinates">格子的逻辑坐标。</param>
        internal GridCell(Vector2Int coordinates)
            : base(coordinates, 1, BlockState.Unblocked)
        {
        }

        /// <summary>从地图属性创建独立的运行时格子，单位占用初始为空。</summary>
        /// <param name="source">需要复制的地图格子或运行时格子。</param>
        /// <exception cref="System.ArgumentNullException">源格子为空。</exception>
        internal GridCell(MapCellData source) : base(source)
        {
        }

        /// <summary>由管理器校验后更新移动代价，全障碍始终保持 -1。</summary>
        /// <param name="value">可通行格的正数代价，或全障碍的 -1。</param>
        internal void SetMoveCost(int value)
        {
            moveCost = NormalizeMoveCost(value, blockState);
        }

        /// <summary>更新障碍及代价；全障碍设为 -1，解除全障碍后默认恢复为 1。</summary>
        /// <param name="value">已经校验的障碍状态。</param>
        internal void SetBlockState(BlockState value)
        {
            blockState = value;
            moveCost = NormalizeMoveCost(Mathf.Max(1, moveCost), value);
        }

        /// <summary>由管理器校验后更新本格指定方向的掩体。</summary>
        /// <param name="direction">有效的棋盘方向。</param>
        /// <param name="value">已经校验的掩体等级。</param>
        internal void SetCover(GridDirection direction, CoverType value)
        {
            switch (direction)
            {
                case GridDirection.North: northCover = value; break;
                case GridDirection.East: eastCover = value; break;
                case GridDirection.South: southCover = value; break;
                case GridDirection.West: westCover = value; break;
            }
        }
    }
}
