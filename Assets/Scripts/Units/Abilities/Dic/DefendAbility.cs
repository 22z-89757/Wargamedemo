using Wargame.Units;

namespace Wargame.Units
{
    /// <summary>消耗 1 AP 进入蹲伏姿态；移动时姿态 Buff 自动移除。</summary>
    public sealed class DefendAbility : AbilityBase
    {
        public DefendAbility() : base("Defend", AbilityTargetType.Self)
        {
            DisplayName = "蹲伏";
            ActionPointCost = 1;
            AddBlockedTag(GameplayTag.HunkerDown);
        }

        protected override bool TryCommit(AbilityTarget target, out AbilityFailReason reason)
        {
            BuffApplyResult result = Owner.Buffs.TryApply(new CrouchBuff());
            if (result != BuffApplyResult.Applied)
            {
                reason = AbilityFailReason.Custom;
                return false;
            }

            Commit();
            reason = AbilityFailReason.None;
            return true;
        }

        protected override void OnActivate(AbilityTarget target) => EndAbility();
    }
}
