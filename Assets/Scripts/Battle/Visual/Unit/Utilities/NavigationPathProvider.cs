using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Wargame.Units
{
    internal enum NavigationQueryStatus { Invalid, Partial, Complete }

    internal interface INavigationQuery
    {
        bool TrySamplePosition(Vector3 source, float maxDistance, out Vector3 sampled);
        NavigationQueryStatus CalculatePath(Vector3 start, Vector3 destination, List<Vector3> corners);
    }

    internal interface INavigationPathProvider
    {
        bool TryCalculate(Vector3 start, Vector3 destination, float sampleRadius,
            out IReadOnlyList<Vector3> corners, out string error);
    }

    internal sealed class NavigationPathProvider : INavigationPathProvider
    {
        private readonly INavigationQuery query;

        public NavigationPathProvider(INavigationQuery query) =>
            this.query = query ?? throw new ArgumentNullException(nameof(query));

        public bool TryCalculate(Vector3 start, Vector3 destination, float sampleRadius,
            out IReadOnlyList<Vector3> corners, out string error)
        {
            corners = Array.Empty<Vector3>();
            error = null;
            if (float.IsNaN(sampleRadius) || float.IsInfinity(sampleRadius) || sampleRadius <= 0f)
            { error = "导航端点采样半径必须为有限正数。"; return false; }
            if (!query.TrySamplePosition(start, sampleRadius, out Vector3 sampledStart))
            { error = $"无法在起点 {start} 附近采样 NavMesh。"; return false; }
            if (!query.TrySamplePosition(destination, sampleRadius, out Vector3 sampledDestination))
            { error = $"无法在终点 {destination} 附近采样 NavMesh。"; return false; }

            var buffer = new List<Vector3>();
            NavigationQueryStatus status = query.CalculatePath(sampledStart, sampledDestination, buffer);
            if (status != NavigationQueryStatus.Complete || buffer.Count < 2)
            { error = $"NavMesh 路径不是完整路径（状态 {status}，拐点 {buffer.Count}）。"; return false; }
            buffer[0] = start;
            buffer[buffer.Count - 1] = destination;
            corners = buffer.AsReadOnly();
            return true;
        }
    }

    internal sealed class UnityNavigationQuery : INavigationQuery
    {
        private readonly NavMeshPath path = new NavMeshPath();

        public bool TrySamplePosition(Vector3 source, float maxDistance, out Vector3 sampled)
        {
            if (NavMesh.SamplePosition(source, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
            { sampled = hit.position; return true; }
            sampled = default;
            return false;
        }

        public NavigationQueryStatus CalculatePath(Vector3 start, Vector3 destination, List<Vector3> corners)
        {
            corners.Clear();
            if (!NavMesh.CalculatePath(start, destination, NavMesh.AllAreas, path))
                return NavigationQueryStatus.Invalid;
            corners.AddRange(path.corners);
            return path.status == NavMeshPathStatus.PathComplete ? NavigationQueryStatus.Complete :
                path.status == NavMeshPathStatus.PathPartial ? NavigationQueryStatus.Partial : NavigationQueryStatus.Invalid;
        }
    }
}
