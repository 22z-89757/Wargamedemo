namespace Wargame.Units
{
    /// <summary>目标已有同 BuffId 的 Buff 时，新 Buff 的处理方式。以新 Buff 的策略为准。</summary>
    public enum BuffStackPolicy
    {
        /// <summary>已存在时拒绝新 Buff。</summary>
        Unique,
        /// <summary>保留旧实例，只把剩余回合重置为完整持续时间。</summary>
        Refresh,
        /// <summary>移除旧实例，改用新实例。</summary>
        Replace,
        /// <summary>旧实例层数加一（不超过上限）并刷新持续时间，修饰效果随层数缩放。</summary>
        Stack,
        /// <summary>新旧实例并存，各自独立计时。</summary>
        Independent
    }

    /// <summary>施加 Buff 的结果。</summary>
    public enum BuffApplyResult
    {
        /// <summary>作为新实例生效。</summary>
        Applied,
        /// <summary>旧实例刷新了持续时间，新实例未使用。</summary>
        Refreshed,
        /// <summary>旧实例层数增加，新实例未使用。</summary>
        Stacked,
        /// <summary>旧实例被移除，新实例生效。</summary>
        Replaced,
        /// <summary>被拒绝：目标无效、被标记阻挡或策略不允许。</summary>
        Rejected
    }

    /// <summary>Buff 被移除的原因。</summary>
    public enum BuffRemoveReason
    {
        /// <summary>持续时间耗尽。</summary>
        Expired,
        /// <summary>被同类 Buff 替换。</summary>
        Replaced,
        /// <summary>被驱散或由代码主动移除。</summary>
        Dispelled
    }
}
