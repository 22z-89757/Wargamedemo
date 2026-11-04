using System;
using Wargame.GridSystem;

namespace Wargame.Units
{
    /// <summary>
    /// 棋盘环境授予的单方向掩体状态。它不修改 Defense；需要方向信息的规则按 Direction 查询。
    /// </summary>
    public sealed class CoverBuff : BuffBase, IHitChanceModifierSource
    {
        private const string ModifierGroup = "cover";

        public CoverBuff(GridDirection direction, CoverType coverType, float hitChancePenalty)
            : base(GetBuffId(direction), Permanent, BuffStackPolicy.Replace)
        {
            if ((uint)direction > (uint)GridDirection.West)
                throw new ArgumentOutOfRangeException(nameof(direction));
            if (coverType != CoverType.HalfCover && coverType != CoverType.FullCover)
                throw new ArgumentOutOfRangeException(nameof(coverType), "CoverBuff 只接受半掩体或全掩体。 ");
            if (float.IsNaN(hitChancePenalty) || float.IsInfinity(hitChancePenalty) || hitChancePenalty < 0f)
                throw new ArgumentOutOfRangeException(nameof(hitChancePenalty), "掩体命中率减值必须是有限的非负数。 ");

            Direction = direction;
            CoverType = coverType;
            HitChancePenalty = hitChancePenalty;
            DisplayName = coverType == CoverType.FullCover ? "全掩体" : "半掩体";
            AddGrantedTag(GameplayTag.InCover);
        }

        public GridDirection Direction { get; }
        public CoverType CoverType { get; }
        public float HitChancePenalty { get; }

        public void CollectHitChanceModifiers(in HitChanceContext context,
            System.Collections.Generic.ICollection<HitChanceModifierCandidate> candidates)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (!IsActive || Owner != context.Target || context.AttackKind != AttackKind.Ranged ||
                !context.Includes(Direction))
                return;

            candidates.Add(new HitChanceModifierCandidate(
                CoverType == CoverType.FullCover ? "cover.full" : "cover.half",
                DisplayName,
                -HitChancePenalty,
                ModifierGroup,
                HitChanceModifierAggregation.StrongestPenalty));
        }

        public static string GetBuffId(GridDirection direction)
        {
            if ((uint)direction > (uint)GridDirection.West)
                throw new ArgumentOutOfRangeException(nameof(direction));
            return "EnvironmentCover." + direction;
        }
    }
}
