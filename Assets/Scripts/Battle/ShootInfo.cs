using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Units
{
    /// <summary>命中率计算中的一个具名百分点修正。</summary>
    public readonly struct HitChanceModifier : IEquatable<HitChanceModifier>
    {
        public HitChanceModifier(string id, string displayName, float value)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("命中率修正 ID 不能为空。", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("命中率修正显示名不能为空。", nameof(displayName));
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "命中率修正必须是有限数。 ");
            Id = id;
            DisplayName = displayName;
            Value = value;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public float Value { get; }

        public bool Equals(HitChanceModifier other) => Id == other.Id &&
            DisplayName == other.DisplayName && Value == other.Value;
        public override bool Equals(object obj) => obj is HitChanceModifier other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, DisplayName, Value);
    }

    /// <summary>不可变命中率明细；业务层提供数据，界面可选择展示公式或只读最终值。</summary>
    public sealed class HitChanceBreakdown
    {
        private static readonly IReadOnlyList<HitChanceModifier> EmptyModifiers =
            new ReadOnlyCollection<HitChanceModifier>(Array.Empty<HitChanceModifier>());

        internal HitChanceBreakdown(float baseChance, IEnumerable<HitChanceModifier> modifiers)
        {
            BaseChance = Mathf.Clamp(baseChance, 0f, 100f);
            if (modifiers == null)
            {
                Modifiers = EmptyModifiers;
                FinalChance = BaseChance;
                return;
            }

            var copy = new List<HitChanceModifier>();
            float result = BaseChance;
            foreach (HitChanceModifier modifier in modifiers)
            {
                copy.Add(modifier);
                result += modifier.Value;
            }
            Modifiers = new ReadOnlyCollection<HitChanceModifier>(copy);
            FinalChance = Mathf.Clamp(result, 0f, 100f);
        }

        public float BaseChance { get; }
        public IReadOnlyList<HitChanceModifier> Modifiers { get; }
        public float FinalChance { get; }

        public bool StructurallyEquals(HitChanceBreakdown other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || BaseChance != other.BaseChance || FinalChance != other.FinalChance ||
                Modifiers.Count != other.Modifiers.Count)
                return false;
            for (int i = 0; i < Modifiers.Count; i++)
                if (!Modifiers[i].Equals(other.Modifiers[i]))
                    return false;
            return true;
        }
    }

    /// <summary>射击预览的只读数据，包含可解释命中率明细且不执行战斗状态修改。</summary>
    public readonly struct ShootPreview
    {
        public Unit Target { get; }
        public string TargetName { get; }
        public float TargetHealth { get; }
        public int Distance { get; }
        public HitChanceBreakdown Breakdown { get; }
        public float HitChance => Breakdown?.FinalChance ?? 0f;
        public Vector2Int FiringOrigin { get; }
        public bool IsAvailable { get; }
        public bool CanFire { get; }
        public AbilityFailReason FailureReason { get; }

        internal ShootPreview(Unit target, int distance, HitChanceBreakdown breakdown, Vector2Int firingOrigin,
            bool isAvailable, bool canFire, AbilityFailReason failureReason)
        {
            Target = target;
            TargetName = target != null ? target.DisplayName : string.Empty;
            TargetHealth = target != null && target.Attributes.TryGetValue(AttributeType.Health, out float health) ? health : 0f;
            Distance = distance;
            Breakdown = breakdown;
            FiringOrigin = firingOrigin;
            IsAvailable = isAvailable;
            CanFire = canFire;
            FailureReason = failureReason;
        }
    }

    /// <summary>一次射击的结果；Breakdown 是提交时命中率的不可变快照。</summary>
    public readonly struct ShootResult
    {
        public Unit Target { get; }
        public string TargetName { get; }
        public Vector3 TargetPosition { get; }
        public bool Hit { get; }
        public bool Grazing { get; }
        public float Damage { get; }
        public int Distance { get; }
        public HitChanceBreakdown Breakdown { get; }
        public float HitChance => Breakdown?.FinalChance ?? 0f;
        public Vector2Int FiringOrigin { get; }

        internal ShootResult(Unit target, string targetName, Vector3 targetPosition, bool hit, bool grazing,
            float damage, int distance, HitChanceBreakdown breakdown, Vector2Int firingOrigin)
        {
            Target = target;
            TargetName = targetName ?? string.Empty;
            TargetPosition = targetPosition;
            Hit = hit;
            Grazing = grazing;
            Damage = damage;
            Distance = distance;
            Breakdown = breakdown;
            FiringOrigin = firingOrigin;
        }

        internal ShootResult(Unit target, string targetName, Vector3 targetPosition, bool hit, bool grazing,
            float damage, int distance, float hitChance, Vector2Int firingOrigin)
            : this(target, targetName, targetPosition, hit, grazing, damage, distance,
                new HitChanceBreakdown(hitChance, null), firingOrigin) { }
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
            GetHitChanceBreakdown(aim, defense, distanceModifier).FinalChance;

        public static HitChanceBreakdown GetHitChanceBreakdown(float aim, float defense,
            float distanceModifier, IEnumerable<HitChanceModifier> modifiers = null)
        {
            float baseChance = Mathf.Clamp(aim - defense + distanceModifier, 0f, 100f);
            return new HitChanceBreakdown(baseChance, modifiers);
        }

        public static float GetDodgeChance(float dodge) => Mathf.Clamp(dodge, 0f, 100f);

        public static float GetDamage(int damage, float armor) => Mathf.Max(0f, damage - armor);
    }
}
