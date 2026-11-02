using System.Collections.Generic;
using Wargame.Units;

namespace Wargame.Battle
{
    /// <summary>把 AbilityComponent 的领域状态转换为 UI 快照，不复制能力规则。</summary>
    public static class BattleUiAbilityAdapter
    {
        public static AbilityUiState Read(Unit owner, AbilityBase ability)
        {
            if (ability == null)
                return new AbilityUiState(null, "未知操作", 0, false, AbilityFailReason.NotGranted, "未获得");

            AbilityTarget target = DefaultTarget(ability, owner);
            AbilityFailReason reason = AbilityFailReason.NotGranted;
            bool canActivate = false;
            if (owner != null)
                canActivate = ability is ShootAbility
                    ? owner.Abilities.CanPrepareTarget(ability, out reason)
                    : owner.Abilities.CanActivate(ability, target, out reason);
            int cost = ability.GetActionPointCost(target);
            return new AbilityUiState(ability, BattleUiText.AbilityName(ability), cost, canActivate,
                canActivate ? AbilityFailReason.None : reason,
                BattleUiText.AbilityStatus(canActivate, canActivate ? AbilityFailReason.None : reason));
        }

        public static IReadOnlyList<AbilityUiState> ReadAll(Unit owner)
        {
            var result = new List<AbilityUiState>();
            if (owner == null) return result;
            foreach (AbilityBase ability in owner.Abilities.Abilities)
                if (!(ability is MoveAbility)) result.Add(Read(owner, ability));
            return result;
        }

        private static AbilityTarget DefaultTarget(AbilityBase ability, Unit owner)
        {
            switch (ability.TargetType)
            {
                case AbilityTargetType.Self: return AbilityTarget.Self(owner);
                default: return AbilityTarget.None;
            }
        }
    }
}
