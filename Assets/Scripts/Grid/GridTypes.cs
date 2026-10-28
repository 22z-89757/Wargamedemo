namespace Wargame.GridSystem
{
    /// <summary>格子在某个方向提供的掩体等级，不决定通行。</summary>
    public enum CoverType
    {
        /// <summary>无掩体。</summary>
        NoCover,
        /// <summary>半掩体。</summary>
        HalfCover,
        /// <summary>全掩体。</summary>
        FullCover
    }

    /// <summary>格子内部的障碍状态，与四边掩体独立。</summary>
    public enum BlockState
    {
        /// <summary>无障碍，可以通行。</summary>
        Unblocked,
        /// <summary>半障碍，可以通行，供后续翻越表现使用。</summary>
        HalfBlocked,
        /// <summary>全障碍，禁止通行。</summary>
        FullBlocked
    }

    /// <summary>棋盘四方向：北为世界 +Z，东为世界 +X。</summary>
    public enum GridDirection
    {
        /// <summary>北，对应逻辑坐标 y + 1。</summary>
        North,
        /// <summary>东，对应逻辑坐标 x + 1。</summary>
        East,
        /// <summary>南，对应逻辑坐标 y - 1。</summary>
        South,
        /// <summary>西，对应逻辑坐标 x - 1。</summary>
        West
    }
}
