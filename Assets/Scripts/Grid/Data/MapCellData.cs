using System;
using UnityEngine;

namespace Wargame.GridSystem
{
    /// <summary>可序列化的只读地图格子数据；GridCell 继承这些字段，在独立副本上更新局内状态。</summary>
    [Serializable]
    public class MapCellData : ISerializationCallbackReceiver
    {
        [SerializeField, Tooltip("格子逻辑坐标，x/y 对应世界 X/Z。")]
        private Vector2Int coordinates;
        [SerializeField, Tooltip("全障碍固定为 -1，表示不可通行；其余格子的进入代价至少为 1。")]
        protected int moveCost;
        [SerializeField, Tooltip("格子内部的障碍状态。")]
        protected BlockState blockState;
        [SerializeField, Tooltip("本格北侧的掩体。")]
        protected CoverType northCover;
        [SerializeField, Tooltip("本格东侧的掩体。")]
        protected CoverType eastCover;
        [SerializeField, Tooltip("本格南侧的掩体。")]
        protected CoverType southCover;
        [SerializeField, Tooltip("本格西侧的掩体。")]
        protected CoverType westCover;

        /// <summary>格子的逻辑坐标。</summary>
        public Vector2Int Coordinates => coordinates;
        /// <summary>进入格子的移动代价；全障碍固定为 -1，其余格子至少为 1。</summary>
        public int MoveCost => moveCost;
        /// <summary>格子的障碍状态。</summary>
        public BlockState BlockState => blockState;
        /// <summary>北侧（+Z）掩体。</summary>
        public CoverType NorthCover => northCover;
        /// <summary>东侧（+X）掩体。</summary>
        public CoverType EastCover => eastCover;
        /// <summary>南侧（-Z）掩体。</summary>
        public CoverType SouthCover => southCover;
        /// <summary>西侧（-X）掩体。</summary>
        public CoverType WestCover => westCover;

        /// <summary>创建格子快照；整体有效性在写入地图资产时统一校验。</summary>
        /// <param name="coordinates">逻辑坐标。</param>
        /// <param name="moveCost">可通行格的正数进入代价；全障碍会自动固定为 -1。</param>
        /// <param name="blockState">格子障碍状态。</param>
        /// <param name="northCover">北侧掩体。</param>
        /// <param name="eastCover">东侧掩体。</param>
        /// <param name="southCover">南侧掩体。</param>
        /// <param name="westCover">西侧掩体。</param>
        public MapCellData(Vector2Int coordinates, int moveCost, BlockState blockState,
            CoverType northCover = CoverType.NoCover, CoverType eastCover = CoverType.NoCover,
            CoverType southCover = CoverType.NoCover, CoverType westCover = CoverType.NoCover)
        {
            this.coordinates = coordinates;
            this.moveCost = NormalizeMoveCost(moveCost, blockState);
            this.blockState = blockState;
            this.northCover = northCover;
            this.eastCover = eastCover;
            this.southCover = southCover;
            this.westCover = westCover;
        }

        /// <summary>复制全部地图属性，不共享原对象，也不复制派生类的单位占用等状态。</summary>
        /// <param name="source">需要复制的地图格子或运行时格子。</param>
        /// <exception cref="ArgumentNullException">源格子为空。</exception>
        public MapCellData(MapCellData source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            coordinates = source.Coordinates;
            moveCost = NormalizeMoveCost(source.MoveCost, source.BlockState);
            blockState = source.BlockState;
            northCover = source.NorthCover;
            eastCover = source.EastCover;
            southCover = source.SouthCover;
            westCover = source.WestCover;
        }

        /// <summary>检查代价及枚举值是否合法，坐标由地图整体校验。</summary>
        /// <returns>所有格子属性均有效时返回 true。</returns>
        internal bool HasValidValues()
        {
            return (blockState == BlockState.FullBlocked ? moveCost == -1 : moveCost >= 1) &&
                (uint)blockState <= (uint)BlockState.FullBlocked &&
                (uint)northCover <= (uint)CoverType.FullCover &&
                (uint)eastCover <= (uint)CoverType.FullCover &&
                (uint)southCover <= (uint)CoverType.FullCover &&
                (uint)westCover <= (uint)CoverType.FullCover;
        }

        /// <summary>保存前统一全障碍代价，确保序列化文件使用 -1 表示不可通行。</summary>
        public void OnBeforeSerialize()
        {
            moveCost = NormalizeMoveCost(moveCost, blockState);
        }

        /// <summary>读取旧地图或草稿时，将全障碍原有的正数代价统一为 -1。</summary>
        public void OnAfterDeserialize()
        {
            moveCost = NormalizeMoveCost(moveCost, blockState);
        }

        /// <summary>统一全障碍的不可通行代价，保留其他状态的输入以供校验。</summary>
        /// <param name="value">原移动代价。</param>
        /// <param name="state">格子的障碍状态。</param>
        /// <returns>全障碍返回 -1，其他状态返回原值。</returns>
        protected static int NormalizeMoveCost(int value, BlockState state)
        {
            return state == BlockState.FullBlocked ? -1 : value;
        }
    }
}
