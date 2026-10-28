using System;
using System.Collections.Generic;
using UnityEngine;
using Wargame.Units;

namespace Wargame.GridSystem
{
    /// <summary>管理 XZ 平面的四方向棋盘、单位占用和按移动力计算的可达范围。</summary>
    [DisallowMultipleComponent]
    public sealed class GridManager : MonoBehaviour
    {
        [SerializeField, Min(1), Tooltip("棋盘沿世界 X 轴的格子数量，运行时初始化后固定。")]
        private int width = 10;

        [SerializeField, Min(1), Tooltip("棋盘沿世界 Z 轴的格子数量，运行时初始化后固定。")]
        private int height = 10;

        [SerializeField, Min(0.01f), Tooltip("格子的世界尺寸，必须为正数；运行时初始化后固定。")]
        private float cellSize = 1f;

        [SerializeField, Tooltip("显示棋盘格线、边界、障碍状态与单位占用。")]
        private bool drawGizmos = true;

        [SerializeField, Tooltip("运行时优先加载的烘焙地图；不指定时创建默认空棋盘。")]
        private MapData sourceMap;

        [SerializeField, HideInInspector, Tooltip("随场景保存的地图编辑草稿，不自动用于运行时。")]
        private GridMapDraft draft = new GridMapDraft(10, 10);

        private static readonly Vector2Int[] NeighborOffsets =
        {
            Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
        };

        private GridCell[,] cells;
        private float initializedCellSize;

        /// <summary>棋盘是否已创建运行时数据。</summary>
        public bool IsInitialized => cells != null;

        /// <summary>X 方向格数；初始化后返回实际数组宽度。</summary>
        public int Width => IsInitialized ? cells.GetLength(0) : width;

        /// <summary>Z 方向格数；初始化后返回实际数组高度。</summary>
        public int Height => IsInitialized ? cells.GetLength(1) : height;

        /// <summary>格子的世界尺寸，初始化后保持不变。</summary>
        public float CellSize => IsInitialized ? initializedCellSize : cellSize;

        /// <summary>棋盘最小 X/Z 角落的位置，格子中心在此基础上偏移半格。</summary>
        public Vector3 Origin => transform.position;

        /// <summary>运行时加载的地图资产，编辑器烘焙成功后自动关联。</summary>
        public MapData SourceMap { get => sourceMap; internal set => sourceMap = value; }

        /// <summary>场景中的编辑草稿，与运行时棋盘独立。</summary>
        internal GridMapDraft Draft => draft;

        /// <summary>优先复制关联地图；未关联时使用 Inspector 尺寸创建默认空棋盘。</summary>
        private void Awake()
        {
            if (IsInitialized)
                return;
            if (sourceMap != null)
                Initialize(sourceMap);
            else
                Initialize(width, height, cellSize);
        }

        /// <summary>限制 Inspector 的棋盘尺寸配置，不改变已初始化的数据。</summary>
        private void OnValidate()
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            cellSize = IsFinite(cellSize) ? Mathf.Max(0.01f, cellSize) : 1f;
        }

        /// <summary>一次性创建默认棋盘；不会重置已有格子或占用数据。</summary>
        /// <param name="width">沿世界 X 轴的格数，必须大于 0。</param>
        /// <param name="height">沿世界 Z 轴的格数，必须大于 0。</param>
        /// <param name="cellSize">每格的世界尺寸，必须为有限正数。</param>
        /// <exception cref="ArgumentOutOfRangeException">宽、高或格子尺寸无效。</exception>
        /// <exception cref="InvalidOperationException">棋盘已经初始化，或 Transform 存在旋转、缩放。</exception>
        public void Initialize(int width, int height, float cellSize = 1f)
        {
            if (IsInitialized)
                throw new InvalidOperationException("棋盘已经初始化，不能覆盖已有数据。");
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "棋盘宽度必须大于 0。");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "棋盘高度必须大于 0。");
            if (!IsFinite(cellSize) || cellSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(cellSize), "格子尺寸必须为有限正数。");
            EnsureSupportedTransform();

            var newCells = new GridCell[width, height];
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                newCells[x, y] = new GridCell(new Vector2Int(x, y));

            CommitInitialization(newCells, cellSize);
        }

        /// <summary>校验并复制地图资产，创建不共享资产数据且没有单位占用的棋盘。</summary>
        /// <param name="mapData">有效的烘焙地图资产，资产位置不影响棋盘原点。</param>
        /// <exception cref="ArgumentNullException">地图资产为空。</exception>
        /// <exception cref="ArgumentException">地图数据不完整或包含非法属性。</exception>
        /// <exception cref="InvalidOperationException">已经初始化，或 Transform 存在旋转、缩放。</exception>
        public void Initialize(MapData mapData)
        {
            if (IsInitialized)
                throw new InvalidOperationException("棋盘已经初始化，不能覆盖已有数据。");
            if (mapData == null)
                throw new ArgumentNullException(nameof(mapData));
            if (!mapData.TryValidate(out string error))
                throw new ArgumentException(error, nameof(mapData));

            EnsureSupportedTransform();
            var newCells = new GridCell[mapData.Width, mapData.Height];
            for (int y = 0; y < mapData.Height; y++)
            for (int x = 0; x < mapData.Width; x++)
            {
                mapData.TryGetCell(new Vector2Int(x, y), out MapCellData data);
                newCells[x, y] = new GridCell(data);
            }
            CommitInitialization(newCells, mapData.CellSize);
        }

        /// <summary>在全部格子创建成功后统一提交棋盘尺寸和运行时数组。</summary>
        /// <param name="newCells">已经创建且校验完毕的棋盘格子。</param>
        /// <param name="newCellSize">新的格子世界尺寸。</param>
        private void CommitInitialization(GridCell[,] newCells, float newCellSize)
        {
            width = newCells.GetLength(0);
            height = newCells.GetLength(1);
            cellSize = newCellSize;
            initializedCellSize = newCellSize;
            cells = newCells;
        }

        /// <summary>更新场景草稿的尺寸，保留重叠格子，不改变运行时数组。</summary>
        /// <param name="newWidth">新宽度。</param>
        /// <param name="newHeight">新高度。</param>
        /// <param name="newCellSize">新的有限正数格子尺寸。</param>
        /// <exception cref="ArgumentOutOfRangeException">尺寸无效。</exception>
        internal void ConfigureDraft(int newWidth, int newHeight, float newCellSize)
        {
            if (!IsFinite(newCellSize) || newCellSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(newCellSize), "格子尺寸必须为有限正数。");
            if (draft == null)
                draft = new GridMapDraft(newWidth, newHeight);
            else
                draft.Resize(newWidth, newHeight);
            width = newWidth;
            height = newHeight;
            cellSize = newCellSize;
        }

        /// <summary>从已校验资产创建独立编辑草稿，掩体将在下次烘焙时重算。</summary>
        /// <param name="mapData">要导入的地图资产。</param>
        /// <exception cref="ArgumentException">资产为空或数据无效。</exception>
        internal void ImportDraft(MapData mapData)
        {
            if (mapData == null || !mapData.TryValidate(out _))
                throw new ArgumentException("请选择有效的地图资产。", nameof(mapData));
            var imported = new GridMapDraft(mapData.Width, mapData.Height);
            for (int y = 0; y < mapData.Height; y++)
            for (int x = 0; x < mapData.Width; x++)
            {
                var position = new Vector2Int(x, y);
                mapData.TryGetCell(position, out MapCellData cell);
                imported.TrySetCell(position, cell.MoveCost, cell.BlockState);
            }

            draft = imported;
            width = mapData.Width;
            height = mapData.Height;
            cellSize = mapData.CellSize;
        }

        /// <summary>查询棋盘中的格子，不暴露内部数组。</summary>
        /// <param name="coordinates">逻辑坐标，x/y 对应世界 X/Z。</param>
        /// <param name="cell">成功时为格子，失败时为 null。</param>
        /// <returns>棋盘已初始化且坐标在范围内时返回 true。</returns>
        public bool TryGetCell(Vector2Int coordinates, out GridCell cell)
        {
            cell = null;
            if (!IsInitialized || coordinates.x < 0 || coordinates.y < 0 ||
                coordinates.x >= Width || coordinates.y >= Height)
                return false;

            cell = cells[coordinates.x, coordinates.y];
            return true;
        }

        /// <summary>按北、东、南、西顺序返回棋盘内的邻格，不过滤障碍和占用。</summary>
        /// <param name="coordinates">要查询的格子坐标。</param>
        /// <returns>只读邻格列表；棋盘未初始化或坐标越界时为空。</returns>
        public IReadOnlyList<GridCell> GetNeighbors(Vector2Int coordinates)
        {
            var neighbors = new List<GridCell>(4);
            if (TryGetCell(coordinates, out _))
            {
                foreach (Vector2Int offset in NeighborOffsets)
                {
                    if (TryGetCell(coordinates + offset, out GridCell neighbor))
                        neighbors.Add(neighbor);
                }
            }

            return neighbors.AsReadOnly();
        }

        /// <summary>将有效格子坐标转换为世界空间的格子中心。</summary>
        /// <param name="coordinates">棋盘内的逻辑坐标。</param>
        /// <returns>位于棋盘高度上的格子中心。</returns>
        /// <exception cref="InvalidOperationException">棋盘尚未初始化，或 Transform 存在旋转、缩放。</exception>
        /// <exception cref="ArgumentOutOfRangeException">格子坐标超出棋盘。</exception>
        public Vector3 GridToWorld(Vector2Int coordinates)
        {
            EnsureInitialized();
            EnsureSupportedTransform();
            if (!TryGetCell(coordinates, out _))
                throw new ArgumentOutOfRangeException(nameof(coordinates), "格子坐标超出棋盘。");

            return Origin + new Vector3((coordinates.x + 0.5f) * CellSize, 0,
                (coordinates.y + 0.5f) * CellSize);
        }

        /// <summary>忽略高度，将世界位置向下取整映射到格子；最大 X/Z 边界属于棋盘外。</summary>
        /// <param name="worldPosition">待查询的世界位置，仅使用 X 和 Z。</param>
        /// <param name="coordinates">成功时为逻辑坐标，失败时为默认值。</param>
        /// <returns>棋盘已初始化、Transform 有效且位置在棋盘内时返回 true。</returns>
        public bool TryWorldToGrid(Vector3 worldPosition, out Vector2Int coordinates)
        {
            coordinates = default;
            if (!IsInitialized || !HasSupportedTransform() ||
                !IsFinite(worldPosition.x) || !IsFinite(worldPosition.z))
                return false;

            Vector3 relative = worldPosition - Origin;
            float x = relative.x / CellSize;
            float y = relative.z / CellSize;
            if (!IsFinite(x) || !IsFinite(y) || x < 0 || y < 0 || x >= Width || y >= Height)
                return false;

            coordinates = new Vector2Int(Mathf.FloorToInt(x), Mathf.FloorToInt(y));
            return true;
        }

        /// <summary>修改障碍并同步代价；全障碍设为 -1，解除后恢复为 1，不能阻挡有单位的格子。</summary>
        /// <param name="coordinates">目标格子坐标。</param>
        /// <param name="state">有效的障碍枚举值。</param>
        /// <returns>修改成功时返回 true；无效请求不会改变数据。</returns>
        public bool TrySetBlockState(Vector2Int coordinates, BlockState state)
        {
            if ((uint)state > (uint)BlockState.FullBlocked ||
                !TryGetCell(coordinates, out GridCell cell) ||
                (state == BlockState.FullBlocked && cell.Occupant != null))
                return false;

            cell.SetBlockState(state);
            return true;
        }

        /// <summary>修改本格某个方向的掩体，不同步邻格，也不影响通行。</summary>
        /// <param name="coordinates">目标格子坐标。</param>
        /// <param name="direction">北为 +Z、东为 +X 的四方向之一。</param>
        /// <param name="cover">有效的掩体等级。</param>
        /// <returns>修改成功时返回 true；无效请求不会改变数据。</returns>
        public bool TrySetCover(Vector2Int coordinates, GridDirection direction, CoverType cover)
        {
            if ((uint)direction > (uint)GridDirection.West || (uint)cover > (uint)CoverType.FullCover ||
                !TryGetCell(coordinates, out GridCell cell))
                return false;

            cell.SetCover(direction, cover);

            return true;
        }

        /// <summary>设置进入代价，不改变障碍状态；全障碍只接受 -1，半障碍不额外加价。</summary>
        /// <param name="coordinates">目标格子坐标。</param>
        /// <param name="moveCost">可通行格必须大于或等于 1，全障碍必须为 -1。</param>
        /// <returns>修改成功时返回 true；无效请求不会改变数据。</returns>
        public bool TrySetMoveCost(Vector2Int coordinates, int moveCost)
        {
            if (!TryGetCell(coordinates, out GridCell cell) ||
                (cell.BlockState == BlockState.FullBlocked ? moveCost != -1 : moveCost < 1))
                return false;

            cell.SetMoveCost(moveCost);
            return true;
        }

        /// <summary>把尚未放置的单位登记到空闲可通行格，只更新占用数据。</summary>
        /// <param name="unit">要登记的有效单位，不能已占据本棋盘其他格子。</param>
        /// <param name="coordinates">目标格子坐标。</param>
        /// <returns>放置成功时返回 true；失败时不改变任何格子的占用。</returns>
        public bool TryPlaceUnit(Unit unit, Vector2Int coordinates)
        {
            if (unit == null || !TryGetCell(coordinates, out GridCell cell) ||
                cell.BlockState == BlockState.FullBlocked || cell.Occupant != null ||
                FindUnitCell(unit) != null)
                return false;

            cell.Occupant = unit;
            return true;
        }

        /// <summary>清除指定单位的逻辑占用，不销毁或移动单位物体。</summary>
        /// <param name="unit">要从本棋盘移除的有效单位。</param>
        /// <returns>找到并移除单位时返回 true，否则保持数据不变。</returns>
        public bool TryRemoveUnit(Unit unit)
        {
            GridCell cell = FindUnitCell(unit);
            if (cell == null)
                return false;

            cell.Occupant = null;
            return true;
        }

        /// <summary>原子地转移单位占用；不移动 Transform、不播放动画、不验证路径或移动力。</summary>
        /// <param name="unit">已登记在本棋盘的有效单位。</param>
        /// <param name="destination">空闲可通行的目标格；原地转移视为成功。</param>
        /// <returns>转移成功时返回 true；失败时原格和目标格均保持不变。</returns>
        public bool TryMoveUnit(Unit unit, Vector2Int destination)
        {
            GridCell source = FindUnitCell(unit);
            if (source == null || !TryGetCell(destination, out GridCell target) ||
                target.BlockState == BlockState.FullBlocked)
                return false;
            if (source == target)
                return true;
            if (target.Occupant != null)
                return false;

            source.Occupant = null;
            target.Occupant = unit;
            return true;
        }

        /// <summary>使用 Dijkstra 计算预算内的可停留格；占用格可穿过但不可停留。</summary>
        /// <param name="start">可通行的起点，允许有单位占用；起点自身不收取代价。</param>
        /// <param name="movementBudget">非负移动力，每步支付目标格的 MoveCost。</param>
        /// <returns>包含最小代价和路径信息的独立快照，不包含起点和其他占用格。</returns>
        /// <exception cref="InvalidOperationException">棋盘尚未初始化。</exception>
        /// <exception cref="ArgumentOutOfRangeException">预算为负数或起点越界。</exception>
        /// <exception cref="ArgumentException">起点是全障碍格。</exception>
        public GridReachabilityResult GetReachableCells(Vector2Int start, int movementBudget)
        {
            EnsureInitialized();
            if (movementBudget < 0)
                throw new ArgumentOutOfRangeException(nameof(movementBudget), "移动力不能为负数。");
            if (!TryGetCell(start, out GridCell startCell))
                throw new ArgumentOutOfRangeException(nameof(start), "起点超出棋盘。");
            if (startCell.BlockState == BlockState.FullBlocked)
                throw new ArgumentException("起点不能是全障碍格。", nameof(start));

            var costs = new Dictionary<Vector2Int, int> { [start] = 0 };
            var predecessors = new Dictionary<Vector2Int, Vector2Int>();
            var queue = new GridSearchQueue();
            queue.Enqueue(start, 0);

            while (queue.TryDequeue(out Vector2Int current, out int currentCost))
            {
                if (costs[current] != currentCost)
                    continue;

                foreach (Vector2Int offset in NeighborOffsets)
                {
                    Vector2Int next = current + offset;
                    if (!TryGetCell(next, out GridCell neighbor) ||
                        neighbor.BlockState == BlockState.FullBlocked ||
                        neighbor.MoveCost <= 0 ||
                        neighbor.MoveCost > movementBudget - currentCost)
                        continue;

                    int nextCost = currentCost + neighbor.MoveCost;
                    if (costs.TryGetValue(next, out int knownCost) && nextCost >= knownCost)
                        continue;

                    costs[next] = nextCost;
                    predecessors[next] = current;
                    queue.Enqueue(next, nextCost);
                }
            }

            var reachable = new Dictionary<Vector2Int, int>();
            foreach (KeyValuePair<Vector2Int, int> pair in costs)
            {
                if (pair.Key != start && cells[pair.Key.x, pair.Key.y].Occupant == null)
                    reachable.Add(pair.Key, pair.Value);
            }

            return new GridReachabilityResult(start, movementBudget, reachable, predecessors);
        }

        /// <summary>在本棋盘中查找单位，避免同一单位重复占据多个格子。</summary>
        /// <param name="unit">要查询的有效单位。</param>
        /// <returns>单位占据的格子；未初始化、单位为空或未放置时返回 null。</returns>
        private GridCell FindUnitCell(Unit unit)
        {
            if (!IsInitialized || unit == null)
                return null;

            foreach (GridCell cell in cells)
            {
                if (cell.Occupant == unit)
                    return cell;
            }

            return null;
        }

        /// <summary>确保需要完整棋盘数据的查询在初始化后执行。</summary>
        /// <exception cref="InvalidOperationException">棋盘尚未初始化。</exception>
        private void EnsureInitialized()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("棋盘尚未初始化。");
        }

        /// <summary>确认管理器及其父级未对棋盘施加旋转或缩放。</summary>
        /// <returns>世界旋转为零且各级局部缩放为 1 时返回 true。</returns>
        internal bool HasSupportedTransform()
        {
            if (Quaternion.Angle(transform.rotation, Quaternion.identity) > 0.001f)
                return false;
            for (Transform current = transform; current != null; current = current.parent)
            {
                if ((current.localScale - Vector3.one).sqrMagnitude > 0.000001f)
                    return false;
            }

            return true;
        }

        /// <summary>拒绝当前版本不支持的棋盘旋转和缩放。</summary>
        /// <exception cref="InvalidOperationException">Transform 存在旋转或缩放。</exception>
        private void EnsureSupportedTransform()
        {
            if (!HasSupportedTransform())
                throw new InvalidOperationException("棋盘必须沿世界轴排列，且自身及父级缩放必须为 1。");
        }

        /// <summary>检查浮点数是否可用于尺寸和坐标计算。</summary>
        /// <param name="value">待检查的值。</param>
        /// <returns>值不是 NaN 或无穷大时返回 true。</returns>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        #region 调试Gizmo绘制 

            /// <summary>编辑模式显示场景草稿，运行时显示真实格子状态，不创建场景物体。</summary>
            private void OnDrawGizmos()
            {
                if (!drawGizmos || !HasSupportedTransform() || Width <= 0 || Height <= 0 ||
                    !IsFinite(CellSize) || CellSize <= 0)
                    return;

                Color previousColor = Gizmos.color;
                Gizmos.color = new Color(0.7f, 0.7f, 0.7f, 0.7f);
                Vector3 origin = Origin;
                for (int x = 0; x <= Width; x++)
                    Gizmos.DrawLine(origin + new Vector3(x * CellSize, 0, 0),
                        origin + new Vector3(x * CellSize, 0, Height * CellSize));
                for (int y = 0; y <= Height; y++)
                    Gizmos.DrawLine(origin + new Vector3(0, 0, y * CellSize),
                        origin + new Vector3(Width * CellSize, 0, y * CellSize));

                Gizmos.color = Color.white;
                Gizmos.DrawWireCube(origin + new Vector3(Width * CellSize, 0, Height * CellSize) * 0.5f,
                    new Vector3(Width * CellSize, 0, Height * CellSize));
                if (IsInitialized)
                {
                    foreach (GridCell cell in cells)
                        DrawCellGizmos(cell);
                }
                else if (draft != null)
                {
                    for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        var position = new Vector2Int(x, y);
                        if (draft.TryGetCell(position, out MapCellData cell))
                            DrawObstacleGizmos(Origin + new Vector3((x + 0.5f) * CellSize, 0,
                                (y + 0.5f) * CellSize), cell.BlockState);
                    }
                }

                Gizmos.color = previousColor;
            }

            /// <summary>以黄色标记半障碍、红色标记全障碍、蓝色球体标记占用。</summary>
            /// <param name="cell">需要绘制的运行时格子。</param>
            private void DrawCellGizmos(GridCell cell)
            {
                Vector3 center = GridToWorld(cell.Coordinates);
                DrawObstacleGizmos(center, cell.BlockState);

                if (cell.Occupant != null)
                {
                    Gizmos.color = new Color(0.2f, 0.6f, 1f);
                    Gizmos.DrawSphere(center + Vector3.up * CellSize * 0.2f, CellSize * 0.15f);
                }
            }

            /// <summary>以黄色显示半障碍、红色显示全障碍，普通格不填色。</summary>
            /// <param name="center">格子的世界中心。</param>
            /// <param name="state">需要显示的障碍状态。</param>
            private void DrawObstacleGizmos(Vector3 center, BlockState state)
            {
                if (state == BlockState.Unblocked)
                    return;
                Gizmos.color = state == BlockState.FullBlocked
                    ? new Color(1f, 0.2f, 0.2f, 0.45f) : new Color(1f, 0.8f, 0.1f, 0.45f);
                Gizmos.DrawCube(center + Vector3.up * CellSize * 0.025f,
                    new Vector3(CellSize * 0.85f, CellSize * 0.05f, CellSize * 0.85f));
            }

        #endregion
    }
}
