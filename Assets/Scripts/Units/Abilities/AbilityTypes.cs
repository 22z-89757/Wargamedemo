using UnityEngine;

namespace Wargame.Units
{
    /// <summary>能力需要的目标类型。</summary>
    public enum AbilityTargetType
    {
        /// <summary>不需要目标，例如警戒。</summary>
        None,
        /// <summary>以自身为目标，例如蹲伏。</summary>
        Self,
        /// <summary>以某个单位为目标，例如射击。</summary>
        Unit,
        /// <summary>以棋盘格为目标，例如移动、投掷手雷。</summary>
        Cell
    }

    /// <summary>能力无法激活的原因，可直接用于 UI 提示。</summary>
    public enum AbilityFailReason
    {
        /// <summary>可以激活。</summary>
        None,
        /// <summary>能力未授予给该组件。</summary>
        NotGranted,
        /// <summary>单位不在自己的回合，且能力不允许回合外激活。</summary>
        NotYourTurn,
        /// <summary>能力已在激活中。</summary>
        AlreadyActive,
        /// <summary>另一个主动能力尚未结束。</summary>
        AnotherAbilityActive,
        /// <summary>单位拥有阻挡标记。</summary>
        BlockedByTag,
        /// <summary>单位缺少必需标记。</summary>
        MissingRequiredTag,
        /// <summary>冷却中。</summary>
        OnCooldown,
        /// <summary>使用次数耗尽。</summary>
        NoCharges,
        /// <summary>行动点不足。</summary>
        NotEnoughActionPoints,
        /// <summary>目标无效。</summary>
        InvalidTarget,
        /// <summary>子类自定义的条件不满足。</summary>
        Custom
    }

    /// <summary>能力激活时选中的目标。</summary>
    public readonly struct AbilityTarget
    {
        private AbilityTarget(AbilityTargetType type, Unit unit, Vector2Int cell)
        {
            Type = type;
            Unit = unit;
            Cell = cell;
        }

        /// <summary>无目标。</summary>
        public static AbilityTarget None => new AbilityTarget(AbilityTargetType.None, null, default);

        /// <summary>以指定单位自身为目标。</summary>
        public static AbilityTarget Self(Unit self) => new AbilityTarget(AbilityTargetType.Self, self, default);

        /// <summary>以单位为目标。</summary>
        public static AbilityTarget ForUnit(Unit unit) => new AbilityTarget(AbilityTargetType.Unit, unit, default);

        /// <summary>以棋盘格为目标。</summary>
        public static AbilityTarget ForCell(Vector2Int cell) => new AbilityTarget(AbilityTargetType.Cell, null, cell);

        /// <summary>目标类型。</summary>
        public AbilityTargetType Type { get; }

        /// <summary>目标单位，Unit/Self 类型有效。</summary>
        public Unit Unit { get; }

        /// <summary>目标格坐标，Cell 类型有效。</summary>
        public Vector2Int Cell { get; }
    }
}
