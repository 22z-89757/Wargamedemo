using UnityEngine;

namespace Wargame.GridSystem
{
    /// <summary>LOS系统，在整数格子地图上执行无状态的二维 supercover 视线查询。通过实现了ILineOfSightMap接口的地图适配器来进行视线可达性查询</summary>
    public static class GridLineOfSight
    {
        /// <summary>判断起点格中心能否接触目标格边界；端点必须在地图内，但不作为阻挡检查。</summary>
        public static bool HasLineOfSight(ILineOfSightMap map, Vector2Int from, Vector2Int to)
        {
            if (map == null || !map.Contains(from) || !map.Contains(to))
                return false;
            if (from == to)
                return true;

            long left = (long)to.x * 2;
            long bottom = (long)to.y * 2;
            return HasClearRay(map, from, to, left, bottom) ||
                   HasClearRay(map, from, to, left + 2, bottom) ||
                   HasClearRay(map, from, to, left, bottom + 2) ||
                   HasClearRay(map, from, to, left + 2, bottom + 2);
        }

        private static bool HasClearRay(ILineOfSightMap map, Vector2Int from, Vector2Int to,
            long targetX, long targetY)
        {
            long startX = (long)from.x * 2 + 1;
            long startY = (long)from.y * 2 + 1;
            long dx = targetX - startX;
            long dy = targetY - startY;
            long absoluteX = System.Math.Abs(dx);
            long absoluteY = System.Math.Abs(dy);
            long stepX = System.Math.Sign(dx);
            long stepY = System.Math.Sign(dy);
            long crossingsX = (absoluteX + 1) / 2;
            long crossingsY = (absoluteY + 1) / 2;
            long crossedX = 0;
            long crossedY = 0;
            long x = from.x;
            long y = from.y;

            while (crossedX < crossingsX || crossedY < crossingsY)
            {
                long nextXDistance = 2 * crossedX + 1;
                long nextYDistance = 2 * crossedY + 1;
                decimal nextVertical = (decimal)nextXDistance * absoluteY;
                decimal nextHorizontal = (decimal)nextYDistance * absoluteX;

                if (nextVertical == nextHorizontal)
                {
                    if (crossedX + 1 == crossingsX && crossedY + 1 == crossingsY)
                        return true;

                    long nextX = x + stepX;
                    long nextY = y + stepY;
                    if (IsTargetCell(nextX, y, to) || IsTargetCell(x, nextY, to) ||
                        IsTargetCell(nextX, nextY, to))
                        return true;
                    if (IsBlockingIntermediate(map, nextX, y, from, to) ||
                        IsBlockingIntermediate(map, x, nextY, from, to))
                        return false;

                    x = nextX;
                    y = nextY;
                    crossedX++;
                    crossedY++;
                }
                else if (nextVertical < nextHorizontal)
                {
                    x += stepX;
                    crossedX++;
                    if (IsTargetCell(x, y, to))
                        return true;
                }
                else
                {
                    y += stepY;
                    crossedY++;
                    if (IsTargetCell(x, y, to))
                        return true;
                }

                if (IsBlockingIntermediate(map, x, y, from, to))
                    return false;
            }

            return true;
        }

        private static bool IsTargetCell(long x, long y, Vector2Int to) => x == to.x && y == to.y;

        private static bool IsBlockingIntermediate(ILineOfSightMap map, long x, long y,
            Vector2Int from, Vector2Int to)
        {
            var cell = new Vector2Int((int)x, (int)y);
            return cell != from && cell != to && map.Contains(cell) && map.BlocksLineOfSight(cell);
        }
    }
}
