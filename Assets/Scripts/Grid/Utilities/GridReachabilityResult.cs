using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Wargame.GridSystem
{
    /// <summary>一次可达搜索的独立快照；棋盘变化后需要重新查询。</summary>
    public sealed class GridReachabilityResult
    {
        private readonly Dictionary<Vector2Int, Vector2Int> predecessors;

        /// <summary>搜索起点，不包含在可停留格集合中。</summary>
        public Vector2Int Start { get; }

        /// <summary>本次搜索允许消耗的最大移动力。</summary>
        public int MovementBudget { get; }

        /// <summary>可停留格子及其最小累计代价，不包含起点和其他单位占用格。</summary>
        public IReadOnlyDictionary<Vector2Int, int> ReachableCosts { get; }

        /// <summary>复制搜索数据，确保后续棋盘更新不会改变本次结果。</summary>
        /// <param name="start">搜索起点。</param>
        /// <param name="movementBudget">移动力预算。</param>
        /// <param name="reachableCosts">可以停留的格子及其最小代价。</param>
        /// <param name="predecessors">所有已到达格子的前驱，包括可穿过的占用格。</param>
        internal GridReachabilityResult(Vector2Int start, int movementBudget,
            Dictionary<Vector2Int, int> reachableCosts,
            Dictionary<Vector2Int, Vector2Int> predecessors)
        {
            Start = start;
            MovementBudget = movementBudget;
            ReachableCosts = new ReadOnlyDictionary<Vector2Int, int>(
                new Dictionary<Vector2Int, int>(reachableCosts));
            this.predecessors = new Dictionary<Vector2Int, Vector2Int>(predecessors);
        }

        /// <summary>还原包含起点和终点的路径；占用格可出现在中途，但不能作为终点。</summary>
        /// <param name="destination">目标格；允许指定起点以取得单格路径。</param>
        /// <param name="path">成功时返回只读路径，失败时返回空集合。</param>
        /// <returns>目标为起点或属于本次可停留集合时返回 true。</returns>
        public bool TryGetPath(Vector2Int destination, out IReadOnlyList<Vector2Int> path)
        {
            if (destination != Start && !ReachableCosts.ContainsKey(destination))
            {
                path = Array.Empty<Vector2Int>();
                return false;
            }

            var coordinates = new List<Vector2Int> { destination };
            while (destination != Start)
            {
                destination = predecessors[destination];
                coordinates.Add(destination);
            }

            coordinates.Reverse();
            path = coordinates.AsReadOnly();
            return true;
        }
    }
}
