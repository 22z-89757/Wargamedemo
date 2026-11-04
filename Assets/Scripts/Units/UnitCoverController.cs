using System;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Units
{
    /// <summary>把单位当前占格的四向掩体同步为由环境维护的 CoverBuff。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Unit))]
    public sealed class UnitCoverController : MonoBehaviour
    {
        private readonly CoverBuff[] activeCovers = new CoverBuff[4];
        private GridManager grid;
        private ICoverBuffFactory buffFactory;
        private Unit unit;
        private bool subscribed;

        public bool IsInitialized => grid != null && buffFactory != null;

        /// <summary>注入单位所属棋盘和环境 Buff 工厂；重复注入相同依赖是幂等的。</summary>
        public void Initialize(GridManager value, ICoverBuffFactory factory)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (grid != value || buffFactory != factory)
            {
                Unsubscribe();
                ClearAll();
                grid = value;
                buffFactory = factory;
            }
            unit = GetComponent<Unit>();
            if (isActiveAndEnabled)
            {
                Subscribe();
                Refresh();
            }
        }

        /// <summary>读取指定方向当前由棋盘环境提供的掩体。</summary>
        public CoverType GetCover(GridDirection direction)
        {
            ValidateDirection(direction);
            CoverBuff buff = activeCovers[(int)direction];
            return buff != null && buff.IsActive && buff.Owner == unit
                ? buff.CoverType : CoverType.NoCover;
        }

        private void OnEnable()
        {
            if (grid == null) return;
            if (unit == null) unit = GetComponent<Unit>();
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearAll();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ClearAll();
        }

        private void Subscribe()
        {
            if (subscribed || grid == null) return;
            grid.Changed += Refresh;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || grid == null) return;
            grid.Changed -= Refresh;
            subscribed = false;
        }

        private void Refresh()
        {
            if (grid == null || unit == null || !unit.IsAlive ||
                !grid.TryGetUnitCoordinates(unit, out Vector2Int coordinates) ||
                !grid.TryGetCell(coordinates, out GridCell cell))
            {
                ClearAll();
                return;
            }

            Sync(GridDirection.North, cell.NorthCover);
            Sync(GridDirection.East, cell.EastCover);
            Sync(GridDirection.South, cell.SouthCover);
            Sync(GridDirection.West, cell.WestCover);
        }

        private void Sync(GridDirection direction, CoverType expected)
        {
            int index = (int)direction;
            CoverBuff current = activeCovers[index];
            if (current != null && (!current.IsActive || current.Owner != unit))
            {
                activeCovers[index] = null;
                current = null;
            }
            if (current != null && current.CoverType == expected)
                return;

            if (expected == CoverType.NoCover)
            {
                if (current != null) unit.Buffs.Remove(current, BuffRemoveReason.Dispelled);
                activeCovers[index] = null;
                return;
            }

            CoverBuff replacement = buffFactory.Create(direction, expected);
            BuffApplyResult result = unit.Buffs.TryApply(replacement);
            if (result != BuffApplyResult.Applied && result != BuffApplyResult.Replaced)
                throw new InvalidOperationException($"无法同步 {direction} 方向的环境掩体。 ");
            activeCovers[index] = replacement;
        }

        private void ClearAll()
        {
            if (unit == null) unit = GetComponent<Unit>();
            for (int i = 0; i < activeCovers.Length; i++)
            {
                CoverBuff buff = activeCovers[i];
                if (buff != null && buff.IsActive && buff.Owner == unit)
                    unit.Buffs.Remove(buff, BuffRemoveReason.Dispelled);
                activeCovers[i] = null;
            }
        }

        private static void ValidateDirection(GridDirection direction)
        {
            if ((uint)direction > (uint)GridDirection.West)
                throw new ArgumentOutOfRangeException(nameof(direction));
        }
    }
}
