using System;
using UnityEngine;
using Wargame.Battle;
using Wargame.GridSystem;

namespace Wargame.Units
{
    /// <summary>对已登记在棋盘上的敌方单位执行一次基于 LOS 的射击。</summary>
    public sealed class ShootAbility : AbilityBase
    {
        private static readonly Vector2Int[] ShootLineOffsets =
        {
            new Vector2Int(0, -1),
            new Vector2Int(1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(-1, 0)
        };

        private readonly GridManager grid;
        private readonly ILineOfSightMap lineOfSightMap;
        private readonly IShootRandomSource randomSource;
        private readonly ShootSettings.Snapshot settings;
        private bool pendingHit;
        private bool pendingGrazing;
        private float pendingDamage;
        private int pendingDistance;
        private float pendingHitChance;

        public ShootResult LastResult { get; private set; }

        public ShootAbility(GridManager grid, ShootSettings settings, IShootRandomSource randomSource = null) : base("Shoot", AbilityTargetType.Unit)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            this.settings = (settings ?? throw new ArgumentNullException(nameof(settings))).CreateSnapshot();
            lineOfSightMap = new GridLineOfSightMapModifier(grid);
            this.randomSource = randomSource ?? new UnityShootRandomSource();
            ActionPointCost = ShootRules.ActionPointCost;
            DisplayName = "射击";
        }

        public override bool IsValidTarget(AbilityTarget target)
        {
            if (!base.IsValidTarget(target) || Owner == null || !Owner.IsAlive ||
                Owner.Faction == Faction.Neutral || target.Unit.Faction == Faction.Neutral ||
                target.Unit.Faction == Owner.Faction ||
                !Owner.Attributes.HasAttribute(AttributeType.Aim) ||
                !Owner.Attributes.TryGetValue(AttributeType.SightRange, out float sightRange) ||
                float.IsNaN(sightRange) || float.IsInfinity(sightRange) || sightRange < 0 ||
                !target.Unit.Attributes.HasAttribute(AttributeType.Defense) ||
                !target.Unit.Attributes.HasAttribute(AttributeType.Dodge) ||
                !target.Unit.Attributes.HasAttribute(AttributeType.Armor) ||
                !target.Unit.Attributes.HasAttribute(AttributeType.Health) ||
                !grid.TryGetUnitCoordinates(Owner, out Vector2Int from) ||
                !grid.TryGetUnitCoordinates(target.Unit, out Vector2Int to))
                return false;

            return ShootRules.GetDistance(from, to) <= sightRange &&
                HasShootingLineOfSight(from, to);
        }

        private bool HasShootingLineOfSight(Vector2Int from, Vector2Int to)
        {
            if (GridLineOfSight.HasLineOfSight(lineOfSightMap, from, to))
                return true;

            foreach (Vector2Int offset in ShootLineOffsets)
            {
                Vector2Int peek = from + offset;
                if (!lineOfSightMap.Contains(peek) || lineOfSightMap.BlocksLineOfSight(peek))
                    continue;
                if (GridLineOfSight.HasLineOfSight(lineOfSightMap, peek, to))
                    return true;
            }

            return false;
        }

        protected override bool TryCommit(AbilityTarget target, out AbilityFailReason reason)
        {
            pendingHit = pendingGrazing = false;
            pendingDamage = 0f;
            try
            {
                Unit victim = target.Unit;
                ShotParameters parameters = ReadParameters(victim);
                pendingDistance = ShootRules.GetDistance(parameters.From, parameters.To);
                pendingHitChance = ShootRules.GetHitChance(parameters.Aim, parameters.Defense,
                    settings.GetHitChanceModifier(pendingDistance));
                pendingHit = NextRandomValue() * 100f < pendingHitChance;
                if (pendingHit)
                {
                    pendingGrazing = NextRandomValue() * 100f < ShootRules.GetDodgeChance(parameters.Dodge);
                    int baseDamage = pendingGrazing ? ShootRules.GrazingDamage : ShootRules.BaseDamage;
                    pendingDamage = ShootRules.GetDamage(baseDamage, parameters.Armor);
                }

                // TryActivate 此时已持有能力锁，不能用组件 CanActivate 重查自身的 IsActive。
                if (!IsActive || Component == null || Owner == null)
                { reason = AbilityFailReason.NotGranted; return false; }
                if (!Owner.IsTurnActive && !CanActivateOutOfTurn)
                { reason = AbilityFailReason.NotYourTurn; return false; }
                if (!CanActivate(target, out reason)) return false;
                if (!parameters.Equals(ReadParameters(victim)))
                { reason = AbilityFailReason.Custom; return false; }
            }
            catch
            {
                reason = AbilityFailReason.Custom;
                return false;
            }

            return base.TryCommit(target, out reason);
        }

        public ShootPreview GetPreview(Unit target)
        {
            AbilityTarget abilityTarget = AbilityTarget.ForUnit(target);
            AbilityFailReason reason;
            bool canFire;
            if (Component == null)
            {
                canFire = false;
                reason = AbilityFailReason.NotGranted;
            }
            else
            {
                canFire = Component.CanActivate(this, abilityTarget, out reason);
            }
            bool validTarget = IsValidTarget(abilityTarget);
            if (!validTarget && reason == AbilityFailReason.None) reason = AbilityFailReason.InvalidTarget;

            float hitChance = 0f;
            int distance = 0;
            if (validTarget)
            {
                ShotParameters parameters = ReadParameters(target);
                distance = ShootRules.GetDistance(parameters.From, parameters.To);
                hitChance = ShootRules.GetHitChance(parameters.Aim, parameters.Defense, settings.GetHitChanceModifier(distance));
            }

            return new ShootPreview(target, distance, hitChance, validTarget, canFire, reason);
        }

        protected override void OnActivate(AbilityTarget target)
        {
            Unit victim = target.Unit;
            if (pendingHit) victim.Attributes.ApplyBaseDelta(AttributeType.Health, -pendingDamage);
            LastResult = new ShootResult(victim, pendingHit, pendingGrazing, pendingDamage, pendingDistance, pendingHitChance);
            pendingHit = pendingGrazing = false;
            pendingDamage = 0f;
            EndAbility();
        }

        private ShotParameters ReadParameters(Unit victim)
        {
            if (!grid.TryGetUnitCoordinates(Owner, out Vector2Int from) ||
                !grid.TryGetUnitCoordinates(victim, out Vector2Int to))
                throw new InvalidOperationException("射击单位已离开棋盘。");
            return new ShotParameters(from, to, Owner, victim);
        }

        private readonly struct ShotParameters : IEquatable<ShotParameters>
        {
            public readonly Vector2Int From, To;
            public readonly float SightRange, Aim, Defense, Dodge, Armor, TargetHealth, ActionPoints;

            public ShotParameters(Vector2Int from, Vector2Int to, Unit owner, Unit target)
            {
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

            public bool Equals(ShotParameters other) => From == other.From && To == other.To &&
                SightRange == other.SightRange && Aim == other.Aim && Defense == other.Defense &&
                Dodge == other.Dodge && Armor == other.Armor && TargetHealth == other.TargetHealth &&
                ActionPoints == other.ActionPoints;
        }

        private float NextRandomValue()
        {
            float value = randomSource.NextValue();
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException("射击随机值必须为有限数。");
            return Mathf.Clamp(value, 0f, 0.99999994f);
        }

        private sealed class UnityShootRandomSource : IShootRandomSource
        {
            public float NextValue() => UnityEngine.Random.value;
        }
    }
}
