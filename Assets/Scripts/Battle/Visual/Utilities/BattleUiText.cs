using System;
using Wargame.Units;
using UnitFaction = Wargame.Units.Faction;

namespace Wargame.Battle
{
    /// <summary>战斗 UI 的固定简体中文文案入口。</summary>
    public static class BattleUiText
    {
        public static string Faction(UnitFaction faction) => faction == UnitFaction.Player ? "玩家回合" :
            faction == UnitFaction.Enemy ? "敌人回合" : "中立回合";

        public static string AbilityName(AbilityBase ability)
        {
            if (ability == null) return "未知操作";
            if (!string.IsNullOrWhiteSpace(ability.DisplayName) && ContainsCjk(ability.DisplayName))
                return ability.DisplayName;
            switch (ability.AbilityId)
            {
                case "Move": return "移动";
                case "Shoot": return "射击";
                default: return "未命名操作";
            }
        }

        public static string AbilityStatus(bool canActivate, AbilityFailReason reason)
        {
            if (canActivate) return "可用";
            switch (reason)
            {
                case AbilityFailReason.NotYourTurn: return "非当前回合";
                case AbilityFailReason.AlreadyActive: return "执行中";
                case AbilityFailReason.AnotherAbilityActive: return "其他操作执行中";
                case AbilityFailReason.BlockedByTag: return "当前状态无法使用";
                case AbilityFailReason.MissingRequiredTag: return "缺少必要状态";
                case AbilityFailReason.OnCooldown: return "冷却中";
                case AbilityFailReason.NoCharges: return "次数耗尽";
                case AbilityFailReason.NotEnoughActionPoints: return "行动点不足";
                case AbilityFailReason.InvalidTarget: return "目标无效";
                case AbilityFailReason.NotGranted: return "未获得";
                case AbilityFailReason.Custom: return "当前条件不满足";
                default: return "不可用";
            }
        }

        public static string AbilityLine(AbilityUiState ability)
        {
            if (ability == null) return string.Empty;
            string cost = ability.ActionPointCost > 0 ? $"消耗 {ability.ActionPointCost} AP" : "无需 AP";
            string cooldown = ability.RemainingCooldown > 0 ? $"冷却 {ability.RemainingCooldown}" : "无冷却";
            string charges = ability.RemainingCharges == AbilityBase.UnlimitedCharges ? "次数不限" : $"剩余 {ability.RemainingCharges} 次";
            return $"{ability.DisplayName}    {cost}    {cooldown}    {charges}    {ability.StatusText}";
        }

        public static string ShotOutcome(ShootResult result)
        {
            string outcome = !result.Hit ? "未命中" : result.Grazing ? "擦伤" : "命中";
            return $"{result.Target.DisplayName}：{outcome}，伤害 {result.Damage:0.#}";
        }

        public static string EndTurnWarning => "仍有行动力未使用，确定要结束回合吗？";
        public static string Confirm => "确认";
        public static string Cancel => "取消";
        public static string NoSelectedUnit => "未选中玩家单位";
        public static string NoTarget => "暂无可攻击目标";
        public static string SelectTarget => "请选择目标";
        public static string HitChance(float chance) => $"命中率 {chance:0}%";

        private static bool ContainsCjk(string value)
        {
            foreach (char character in value)
                if (character >= 0x4E00 && character <= 0x9FFF) return true;
            return false;
        }
    }
}
