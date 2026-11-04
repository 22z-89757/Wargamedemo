using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Units
{
    /// <summary>本次命中率求值所属的攻击类型。</summary>
    public enum AttackKind
    {
        Ranged
    }

    /// <summary>攻击者相对目标实际占格所接触的目标边界方向。</summary>
    [Flags]
    public enum IncomingDirectionMask
    {
        None = 0,
        North = 1 << 0,
        East = 1 << 1,
        South = 1 << 2,
        West = 1 << 3
    }

    /// <summary>命中率效果只读上下文；来向始终由双方实际占格计算。</summary>
    public readonly struct HitChanceContext
    {
        public HitChanceContext(Unit attacker, Unit target, Vector2Int attackerCell,
            Vector2Int targetCell, AttackKind attackKind)
        {
            Attacker = attacker ?? throw new ArgumentNullException(nameof(attacker));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            AttackerCell = attackerCell;
            TargetCell = targetCell;
            AttackKind = attackKind;
            IncomingDirections = GetIncomingDirections(attackerCell, targetCell);
        }

        public Unit Attacker { get; }
        public Unit Target { get; }
        public Vector2Int AttackerCell { get; }
        public Vector2Int TargetCell { get; }
        public AttackKind AttackKind { get; }
        public IncomingDirectionMask IncomingDirections { get; }

        public bool Includes(GridDirection direction)
        {
            IncomingDirectionMask mask = direction switch
            {
                GridDirection.North => IncomingDirectionMask.North,
                GridDirection.East => IncomingDirectionMask.East,
                GridDirection.South => IncomingDirectionMask.South,
                GridDirection.West => IncomingDirectionMask.West,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };
            return (IncomingDirections & mask) != 0;
        }

        private static IncomingDirectionMask GetIncomingDirections(Vector2Int attackerCell,
            Vector2Int targetCell)
        {
            Vector2Int delta = attackerCell - targetCell;
            IncomingDirectionMask result = IncomingDirectionMask.None;
            if (delta.y > 0) result |= IncomingDirectionMask.North;
            else if (delta.y < 0) result |= IncomingDirectionMask.South;
            if (delta.x > 0) result |= IncomingDirectionMask.East;
            else if (delta.x < 0) result |= IncomingDirectionMask.West;
            return result;
        }
    }

    /// <summary>候选修正的聚合方式。</summary>
    public enum HitChanceModifierAggregation
    {
        Independent,
        StrongestPenalty
    }

    /// <summary>效果来源提供、解析器尚未聚合的命中率修正候选。</summary>
    public readonly struct HitChanceModifierCandidate
    {
        public HitChanceModifierCandidate(string id, string displayName, float value,
            string groupId = null,
            HitChanceModifierAggregation aggregation = HitChanceModifierAggregation.Independent)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("命中率修正候选 ID 不能为空。", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("命中率修正候选显示名不能为空。", nameof(displayName));
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "命中率修正候选必须是有限数。 ");
            if (aggregation == HitChanceModifierAggregation.Independent &&
                !string.IsNullOrEmpty(groupId))
                throw new ArgumentException("独立修正不能声明聚合组。", nameof(groupId));
            if (aggregation != HitChanceModifierAggregation.Independent &&
                string.IsNullOrWhiteSpace(groupId))
                throw new ArgumentException("分组修正必须声明聚合组。", nameof(groupId));
            if (aggregation == HitChanceModifierAggregation.StrongestPenalty && value > 0f)
                throw new ArgumentOutOfRangeException(nameof(value), "最强减值候选不能是正数。 ");

            Id = id;
            DisplayName = displayName;
            Value = value;
            GroupId = groupId;
            Aggregation = aggregation;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public float Value { get; }
        public string GroupId { get; }
        public HitChanceModifierAggregation Aggregation { get; }

        internal HitChanceModifier ToModifier() => new HitChanceModifier(Id, DisplayName, Value);
    }

    /// <summary>可以根据只读攻击事实提供命中率修正候选的来源。</summary>
    public interface IHitChanceModifierSource
    {
        void CollectHitChanceModifiers(in HitChanceContext context,
            ICollection<HitChanceModifierCandidate> candidates);
    }

    /// <summary>收集并确定性聚合命中率修正候选。</summary>
    public static class HitChanceModifierResolver
    {
        private static readonly IReadOnlyList<HitChanceModifier> Empty =
            new ReadOnlyCollection<HitChanceModifier>(Array.Empty<HitChanceModifier>());

        public static IReadOnlyList<HitChanceModifier> Resolve(in HitChanceContext context,
            IReadOnlyList<BuffBase> buffs)
        {
            if (buffs == null) throw new ArgumentNullException(nameof(buffs));
            var candidates = new List<HitChanceModifierCandidate>();
            for (int i = 0; i < buffs.Count; i++)
                if (buffs[i] is IHitChanceModifierSource source)
                    source.CollectHitChanceModifiers(in context, candidates);
            return ResolveCandidates(candidates);
        }

        public static IReadOnlyList<HitChanceModifier> ResolveCandidates(
            IEnumerable<HitChanceModifierCandidate> candidates)
        {
            if (candidates == null) return Empty;

            var resolved = new List<HitChanceModifier>();
            var groups = new Dictionary<string, GroupState>(StringComparer.Ordinal);
            foreach (HitChanceModifierCandidate candidate in candidates)
            {
                if (candidate.Aggregation == HitChanceModifierAggregation.Independent)
                {
                    resolved.Add(candidate.ToModifier());
                    continue;
                }

                if (!groups.TryGetValue(candidate.GroupId, out GroupState group))
                {
                    groups.Add(candidate.GroupId,
                        new GroupState(candidate.Aggregation, resolved.Count, candidate.Value));
                    resolved.Add(candidate.ToModifier());
                    continue;
                }

                if (group.Aggregation != candidate.Aggregation)
                    throw new InvalidOperationException(
                        $"命中率修正组 {candidate.GroupId} 声明了冲突的聚合策略。 ");

                if (candidate.Aggregation == HitChanceModifierAggregation.StrongestPenalty &&
                    candidate.Value < group.Value)
                {
                    resolved[group.Index] = candidate.ToModifier();
                    groups[candidate.GroupId] =
                        new GroupState(group.Aggregation, group.Index, candidate.Value);
                }
            }

            return resolved.Count == 0
                ? Empty
                : new ReadOnlyCollection<HitChanceModifier>(resolved);
        }

        private readonly struct GroupState
        {
            public GroupState(HitChanceModifierAggregation aggregation, int index, float value)
            {
                Aggregation = aggregation;
                Index = index;
                Value = value;
            }

            public HitChanceModifierAggregation Aggregation { get; }
            public int Index { get; }
            public float Value { get; }
        }
    }
}
