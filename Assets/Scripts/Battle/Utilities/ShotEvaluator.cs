using System;
using UnityEngine;
using Wargame.Battle;
using Wargame.GridSystem;

namespace Wargame.Units
{
    /// <summary>射击求值所读取的完整状态快照，用于提交前结构一致性复核。</summary>
    internal readonly struct ShotParameters : IEquatable<ShotParameters>
    {
        public ShotParameters(Vector2Int from, Vector2Int to, Unit owner, Unit target)
        {
            Owner = owner;
            Target = target;
            From = from;
            To = to;
            SightRange = owner.Attributes.GetValue(AttributeType.SightRange);
            Aim = owner.Attributes.GetValue(AttributeType.Aim);
            Defense = target.Attributes.GetValue(AttributeType.Defense);
            Dodge = target.Attributes.GetValue(AttributeType.Dodge);
            Armor = target.Attributes.GetValue(AttributeType.Armor);
            TargetHealth = target.Attributes.GetValue(AttributeType.Health);
            ActionPoints = owner.Attributes.GetValue(AttributeType.ActionPoints);
        }

        public Unit Owner { get; }
        public Unit Target { get; }
        public Vector2Int From { get; }
        public Vector2Int To { get; }
        public float SightRange { get; }
        public float Aim { get; }
        public float Defense { get; }
        public float Dodge { get; }
        public float Armor { get; }
        public float TargetHealth { get; }
        public float ActionPoints { get; }

        public bool Equals(ShotParameters other) => Owner == other.Owner && Target == other.Target &&
            From == other.From && To == other.To && SightRange == other.SightRange &&
            Aim == other.Aim && Defense == other.Defense && Dodge == other.Dodge &&
            Armor == other.Armor && TargetHealth == other.TargetHealth &&
            ActionPoints == other.ActionPoints;
    }

    /// <summary>一次无副作用射击求值的不可变结果。</summary>
    internal readonly struct ShotEvaluation : IEquatable<ShotEvaluation>
    {
        public ShotEvaluation(ShotParameters parameters, int distance,
            Vector2Int firingOrigin, HitChanceBreakdown breakdown)
        {
            Parameters = parameters;
            Distance = distance;
            FiringOrigin = firingOrigin;
            Breakdown = breakdown ?? throw new ArgumentNullException(nameof(breakdown));
        }

        public ShotParameters Parameters { get; }
        public int Distance { get; }
        public Vector2Int FiringOrigin { get; }
        public HitChanceBreakdown Breakdown { get; }

        public bool Equals(ShotEvaluation other) => Parameters.Equals(other.Parameters) &&
            Distance == other.Distance && FiringOrigin == other.FiringOrigin &&
            Breakdown.StructurallyEquals(other.Breakdown);
    }

    /// <summary>使用显式攻击者坐标执行只读射击合法性与命中率求值。</summary>
    internal sealed class ShotEvaluator
    {
        private static readonly Vector2Int[] ShootLineOffsets =
        {
            new Vector2Int(0, -1), new Vector2Int(1, 0),
            new Vector2Int(0, 1), new Vector2Int(-1, 0)
        };

        private readonly GridManager grid;
        private readonly ILineOfSightMap lineOfSightMap;
        private readonly ShootSettings.Snapshot settings;

        public ShotEvaluator(GridManager grid, ShootSettings.Snapshot settings)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            lineOfSightMap = new GridLineOfSightMapModifier(grid);
        }

        public bool TryEvaluateCurrent(Unit attacker, Unit target, out ShotEvaluation evaluation)
        {
            evaluation = default;
            return attacker != null && grid.TryGetUnitCoordinates(attacker, out Vector2Int from) &&
                TryEvaluate(attacker, from, target, out evaluation);
        }

        public bool TryEvaluate(Unit attacker, Vector2Int from, Unit target,
            out ShotEvaluation evaluation)
        {
            evaluation = default;
            if (attacker == null || target == null || !attacker.IsAlive || !target.IsAlive ||
                attacker.Faction == Faction.Neutral || target.Faction == Faction.Neutral ||
                target.Faction == attacker.Faction ||
                !attacker.Attributes.HasAttribute(AttributeType.Aim) ||
                !attacker.Attributes.TryGetValue(AttributeType.SightRange, out float sightRange) ||
                float.IsNaN(sightRange) || float.IsInfinity(sightRange) || sightRange < 0 ||
                !target.Attributes.HasAttribute(AttributeType.Defense) ||
                !target.Attributes.HasAttribute(AttributeType.Dodge) ||
                !target.Attributes.HasAttribute(AttributeType.Armor) ||
                !target.Attributes.HasAttribute(AttributeType.Health) ||
                !grid.TryGetUnitCoordinates(attacker, out Vector2Int actualFrom) ||
                !grid.TryGetUnitCoordinates(target, out Vector2Int to) ||
                !grid.TryGetCell(from, out GridCell fromCell) ||
                fromCell.BlockState == BlockState.FullBlocked ||
                (from != actualFrom && fromCell.Occupant != null))
                return false;

            int distance = ShootRules.GetDistance(from, to);
            if (distance > sightRange || !TryGetFiringOrigin(from, to, out Vector2Int firingOrigin))
                return false;

            var parameters = new ShotParameters(from, to, attacker, target);
            var context = new HitChanceContext(attacker, target, from, to, AttackKind.Ranged);
            System.Collections.Generic.IReadOnlyList<HitChanceModifier> modifiers =
                HitChanceModifierResolver.Resolve(in context, target.Buffs.Buffs);
            HitChanceBreakdown breakdown = ShootRules.GetHitChanceBreakdown(parameters.Aim,
                parameters.Defense, settings.GetHitChanceModifier(distance), modifiers);
            evaluation = new ShotEvaluation(parameters, distance, firingOrigin, breakdown);
            return true;
        }

        public bool TryGetFiringOrigin(Vector2Int from, Vector2Int to,
            out Vector2Int firingOrigin)
        {
            if (GridLineOfSight.HasLineOfSight(lineOfSightMap, from, to))
            {
                firingOrigin = from;
                return true;
            }

            foreach (Vector2Int offset in ShootLineOffsets)
            {
                Vector2Int peek = from + offset;
                if (!lineOfSightMap.Contains(peek) || lineOfSightMap.BlocksLineOfSight(peek)) continue;
                if (!GridLineOfSight.HasLineOfSight(lineOfSightMap, peek, to)) continue;
                firingOrigin = peek;
                return true;
            }

            firingOrigin = default;
            return false;
        }
    }
}
