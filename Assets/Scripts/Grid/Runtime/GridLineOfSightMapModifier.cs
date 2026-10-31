using UnityEngine;

namespace Wargame.GridSystem
{
    /// <summary>将运行时 GridManager 适配为只读视线地图。</summary>
    public sealed class GridLineOfSightMapModifier : ILineOfSightMap
    {
        private readonly GridManager grid;

        public GridLineOfSightMapModifier(GridManager grid)
        {
            this.grid = grid;
        }

        public bool Contains(Vector2Int cell) => grid != null && grid.TryGetCell(cell, out _);

        public bool BlocksLineOfSight(Vector2Int cell) =>
            grid != null && grid.TryGetCell(cell, out GridCell data) && data.BlockState == BlockState.FullBlocked;
    }
}
