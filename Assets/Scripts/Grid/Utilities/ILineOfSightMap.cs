using UnityEngine;

namespace Wargame.GridSystem
{
    /// <summary>视线遍历所需的最小只读地图接口。</summary>
    public interface ILineOfSightMap
    {
        /// <summary>坐标是否位于当前地图范围内。</summary>
        bool Contains(Vector2Int cell);

        /// <summary>该格是否阻挡视线。</summary>
        bool BlocksLineOfSight(Vector2Int cell);
    }
}
