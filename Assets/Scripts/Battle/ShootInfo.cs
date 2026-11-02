using UnityEngine;

namespace Wargame.Units
{
    /// <summary>射击预览的只读数据，不执行战斗状态修改。</summary>
    public readonly struct ShootPreview
    {
        public Unit Target { get; }
        public string TargetName { get; }
        public float TargetHealth { get; }
        public int Distance { get; }
        public float HitChance { get; }
        public Vector2Int FiringOrigin { get; }
        public bool IsAvailable { get; }
        public bool CanFire { get; }
        public AbilityFailReason FailureReason { get; }

        internal ShootPreview(Unit target, int distance, float hitChance, Vector2Int firingOrigin,
            bool isAvailable, bool canFire, AbilityFailReason failureReason)
        {
            Target = target;
            TargetName = target != null ? target.DisplayName : string.Empty;
            TargetHealth = target != null && target.Attributes.TryGetValue(AttributeType.Health, out float health) ? health : 0f;
            Distance = distance;
            HitChance = hitChance;
            FiringOrigin = firingOrigin;
            IsAvailable = isAvailable;
            CanFire = canFire;
            FailureReason = failureReason;
        }
    }

    /// <summary>一次射击的结果。</summary>
    public readonly struct ShootResult
    {
        public Unit Target { get; }
        public string TargetName { get; }
        public Vector3 TargetPosition { get; }
        public bool Hit { get; }
        public bool Grazing { get; }
        public float Damage { get; }
        public int Distance { get; }
        public float HitChance { get; }
        public Vector2Int FiringOrigin { get; }

        internal ShootResult(Unit target, string targetName, Vector3 targetPosition, bool hit, bool grazing,
            float damage, int distance, float hitChance, Vector2Int firingOrigin)
        {
            Target = target;
            TargetName = targetName ?? string.Empty;
            TargetPosition = targetPosition;
            Hit = hit;
            Grazing = grazing;
            Damage = damage;
            Distance = distance;
            HitChance = hitChance;
            FiringOrigin = firingOrigin;
        }
    }

    /// <summary>射击随机值来源；返回值应处于 [0, 1) 区间，便于测试确定性注入。</summary>
    public interface IShootRandomSource
    {
        float NextValue();
    }

    /// <summary>射击属性公式，供预览与结算共用。</summary>
    public static class ShootRules
    {
        public const int ActionPointCost = 1;
        public const int BaseDamage = 4;
        public const int GrazingDamage = 2;

        public static int GetDistance(Vector2Int from, Vector2Int to)
        {
            double dx = (double)to.x - from.x;
            double dy = (double)to.y - from.y;
            return (int)System.Math.Floor(System.Math.Sqrt(dx * dx + dy * dy));
        }

        public static float GetHitChance(float aim, float defense, float distanceModifier) =>
            Mathf.Clamp(aim - defense + distanceModifier, 0f, 100f);

        public static float GetDodgeChance(float dodge) => Mathf.Clamp(dodge, 0f, 100f);

        public static float GetDamage(int damage, float armor) => Mathf.Max(0f, damage - armor);
    }
}
