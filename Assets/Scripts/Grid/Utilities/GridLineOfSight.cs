using UnityEngine;

namespace Wargame.GridSystem
{
    /// <summary>LOS系统，在整数格子地图上执行无状态的二维 supercover 视线查询。通过实现了ILineOfSightMap接口的地图适配器来进行视线可达性查询</summary>
    public static class GridLineOfSight
    {
        /// <summary>判断两个格子中心之间是否可见；端点必须在地图内，但不作为阻挡检查。</summary>
        public static bool HasLineOfSight(ILineOfSightMap map, Vector2Int from, Vector2Int to)
        {
            if (map == null || !map.Contains(from) || !map.Contains(to))
                return false;
            if (from == to)
                return true;

            long x = from.x;
            long y = from.y;
            long dx = (long)to.x - from.x;
            long dy = (long)to.y - from.y;
            long nx = System.Math.Abs(dx);
            long ny = System.Math.Abs(dy);
            long stepX = System.Math.Sign(dx);
            long stepY = System.Math.Sign(dy);
            long crossedX = 0;
            long crossedY = 0;

            while (crossedX < nx || crossedY < ny)
            {
                decimal nextVertical = (decimal)(2 * crossedX + 1) * ny;
                decimal nextHorizontal = (decimal)(2 * crossedY + 1) * nx;

                if (nextVertical == nextHorizontal)
                {
                    Vector2Int sideX = new Vector2Int((int)(x + stepX), (int)y);
                    Vector2Int sideY = new Vector2Int((int)x, (int)(y + stepY));
                    if (IsBlockingIntermediate(map, sideX, from, to) ||
                        IsBlockingIntermediate(map, sideY, from, to))
                        return false;

                    x += stepX;
                    y += stepY;
                    crossedX++;
                    crossedY++;
                }
                else if (nextVertical < nextHorizontal)
                {
                    x += stepX;
                    crossedX++;
                }
                else
                {
                    y += stepY;
                    crossedY++;
                }

                Vector2Int current = new Vector2Int((int)x, (int)y);
                if (IsBlockingIntermediate(map, current, from, to))
                    return false;
            }

            return true;
        }

        private static bool IsBlockingIntermediate(ILineOfSightMap map, Vector2Int cell,
            Vector2Int from, Vector2Int to)
        {
            return cell != from && cell != to && map.Contains(cell) && map.BlocksLineOfSight(cell);
        }
    }
}
