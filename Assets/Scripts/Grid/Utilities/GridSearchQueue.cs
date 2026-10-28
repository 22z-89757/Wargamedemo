using System.Collections.Generic;
using UnityEngine;

namespace Wargame.GridSystem
{
    /// <summary>自定义的 Dijkstra 算法使用的 稳定最小堆，同代价节点按入队顺序弹出。</summary>
    internal sealed class GridSearchQueue
    {
        private readonly List<Entry> entries = new List<Entry>();
        private long nextOrder;

        /// <summary>保存待扩展格子的坐标、代价和入队顺序。</summary>
        private readonly struct Entry
        {
            internal readonly Vector2Int Coordinates;
            internal readonly int Cost;
            internal readonly long Order;

            /// <summary>创建一个待扩展节点。</summary>
            /// <param name="coordinates">节点坐标。</param>
            /// <param name="cost">从起点抵达节点的累计代价。</param>
            /// <param name="order">单调递增的入队序号。</param>
            internal Entry(Vector2Int coordinates, int cost, long order)
            {
                Coordinates = coordinates;
                Cost = cost;
                Order = order;
            }
        }

        /// <summary>将格子和当前最小代价加入队列。</summary>
        /// <param name="coordinates">待扩展格子的坐标。</param>
        /// <param name="cost">抵达格子的累计代价。</param>
        internal void Enqueue(Vector2Int coordinates, int cost)
        {
            var entry = new Entry(coordinates, cost, nextOrder++);
            entries.Add(entry);
            int index = entries.Count - 1;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!ComesBefore(entry, entries[parent]))
                    break;

                entries[index] = entries[parent];
                index = parent;
            }

            entries[index] = entry;
        }

        /// <summary>取出累计代价最小的节点，同代价时先取较早入队的节点。</summary>
        /// <param name="coordinates">成功时为节点坐标，队列为空时为默认值。</param>
        /// <param name="cost">成功时为累计代价，队列为空时为 0。</param>
        /// <returns>队列中存在节点时返回 true。</returns>
        internal bool TryDequeue(out Vector2Int coordinates, out int cost)
        {
            if (entries.Count == 0)
            {
                coordinates = default;
                cost = 0;
                return false;
            }

            Entry first = entries[0];
            Entry last = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            if (entries.Count > 0)
            {
                int index = 0;
                while (index * 2 + 1 < entries.Count)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < entries.Count && ComesBefore(entries[child + 1], entries[child]))
                        child++;
                    if (!ComesBefore(entries[child], last))
                        break;

                    entries[index] = entries[child];
                    index = child;
                }

                entries[index] = last;
            }

            coordinates = first.Coordinates;
            cost = first.Cost;
            return true;
        }

        /// <summary>比较两个节点的代价和入队顺序。</summary>
        /// <param name="left">待比较的节点。</param>
        /// <param name="right">另一个节点。</param>
        /// <returns>左侧节点应更早弹出时返回 true。</returns>
        private static bool ComesBefore(Entry left, Entry right)
        {
            return left.Cost < right.Cost || (left.Cost == right.Cost && left.Order < right.Order);
        }
    }
}
