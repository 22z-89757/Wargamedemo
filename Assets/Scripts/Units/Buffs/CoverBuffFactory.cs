using System;
using Wargame.Battle;
using Wargame.GridSystem;

namespace Wargame.Units
{
    /// <summary>按局内不可变配置创建环境掩体 Buff。</summary>
    public interface ICoverBuffFactory
    {
        CoverBuff Create(GridDirection direction, CoverType coverType);
    }

    public sealed class ConfiguredCoverBuffFactory : ICoverBuffFactory
    {
        private readonly CoverEffectSettings.Snapshot settings;

        public ConfiguredCoverBuffFactory(CoverEffectSettings.Snapshot settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public CoverBuff Create(GridDirection direction, CoverType coverType) =>
            new CoverBuff(direction, coverType, settings.GetHitChancePenalty(coverType));
    }
}
