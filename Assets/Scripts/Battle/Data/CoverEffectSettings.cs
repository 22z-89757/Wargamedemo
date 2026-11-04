using System;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Battle
{
    /// <summary>棋盘掩体提供的战斗效果配置。</summary>
    [Serializable]
    public sealed class CoverEffectSettings
    {
        [Tooltip("半掩体降低的命中率百分点。")]
        public float HalfCoverHitChancePenalty = 20f;

        [Tooltip("全掩体降低的命中率百分点。")]
        public float FullCoverHitChancePenalty = 40f;

        public bool TryValidate(out string error)
        {
            error = null;
            if (!IsFinite(HalfCoverHitChancePenalty) || HalfCoverHitChancePenalty < 0f ||
                !IsFinite(FullCoverHitChancePenalty) || FullCoverHitChancePenalty < 0f)
            {
                error = "掩体命中率减值必须是有限的非负数。";
                return false;
            }
            if (FullCoverHitChancePenalty < HalfCoverHitChancePenalty)
            {
                error = "全掩体命中率减值不能低于半掩体。";
                return false;
            }
            return true;
        }

        public Snapshot CreateSnapshot()
        {
            if (!TryValidate(out string error)) throw new ArgumentException(error);
            return new Snapshot(HalfCoverHitChancePenalty, FullCoverHitChancePenalty);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>局内使用的不可变掩体效果配置。</summary>
        public sealed class Snapshot
        {
            internal Snapshot(float halfCoverHitChancePenalty, float fullCoverHitChancePenalty)
            {
                HalfCoverHitChancePenalty = halfCoverHitChancePenalty;
                FullCoverHitChancePenalty = fullCoverHitChancePenalty;
            }

            public float HalfCoverHitChancePenalty { get; }
            public float FullCoverHitChancePenalty { get; }

            public float GetHitChancePenalty(CoverType coverType)
            {
                return coverType switch
                {
                    CoverType.HalfCover => HalfCoverHitChancePenalty,
                    CoverType.FullCover => FullCoverHitChancePenalty,
                    _ => throw new ArgumentOutOfRangeException(nameof(coverType),
                        "只有半掩体和全掩体具有命中率减值。")
                };
            }
        }
    }
}
