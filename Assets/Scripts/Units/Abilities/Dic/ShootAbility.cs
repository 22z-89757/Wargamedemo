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
            new Vector2Int(0, -1), new Vector2Int(1, 0),
            new Vector2Int(0, 1), new Vector2Int(-1, 0)
        };

        private readonly GridManager grid;
        private readonly ILineOfSightMap lineOfSightMap;
        private readonly IShootRandomSource randomSource;
        private readonly ShootSettings.Snapshot settings;
        private readonly IUnitShootVisual visual;
        private bool pendingHit, pendingGrazing, damageApplied;
        private float pendingDamage, pendingHitChance;
        private int pendingDistance;
        private Vector2Int pendingFiringOrigin;
        private Unit pendingTarget;

        public ShootResult LastResult { get; private set; }

        public ShootAbility(GridManager grid, ShootSettings settings, IShootRandomSource randomSource = null)
            : this(grid, settings, null, randomSource) { }

        public ShootAbility(GridManager grid, ShootSettings settings, IUnitShootVisual visual,
            IShootRandomSource randomSource = null) : base("Shoot", AbilityTargetType.Unit)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            this.settings = (settings ?? throw new ArgumentNullException(nameof(settings))).CreateSnapshot();
            this.visual = visual;
            lineOfSightMap = new GridLineOfSightMapModifier(grid);
            this.randomSource = randomSource ?? new UnityShootRandomSource();
            ActionPointCost = ShootRules.ActionPointCost;
            DisplayName = "射击";
        }

        public override bool IsValidTarget(AbilityTarget target) => TryReadValidTarget(target, out _, out _);

        private bool TryReadValidTarget(AbilityTarget target, out ShotParameters parameters,
            out Vector2Int firingOrigin)
        {
            parameters = default;
            firingOrigin = default;
            if (!base.IsValidTarget(target) || Owner == null || !Owner.IsAlive ||
                Owner.Faction == Faction.Neutral || target.Unit.Faction == Faction.Neutral ||
                target.Unit.Faction == Owner.Faction || !Owner.Attributes.HasAttribute(AttributeType.Aim) ||
                !Owner.Attributes.TryGetValue(AttributeType.SightRange, out float sightRange) ||
                float.IsNaN(sightRange) || float.IsInfinity(sightRange) || sightRange < 0 ||
                !target.Unit.Attributes.HasAttribute(AttributeType.Defense) ||
                !target.Unit.Attributes.HasAttribute(AttributeType.Dodge) ||
                !target.Unit.Attributes.HasAttribute(AttributeType.Armor) ||
                !target.Unit.Attributes.HasAttribute(AttributeType.Health) ||
                !grid.TryGetUnitCoordinates(Owner, out Vector2Int from) ||
                !grid.TryGetUnitCoordinates(target.Unit, out Vector2Int to))
                return false;
            parameters = new ShotParameters(from, to, Owner, target.Unit);
            return ShootRules.GetDistance(from, to) <= sightRange && TryGetFiringOrigin(from, to, out firingOrigin);
        }

        internal bool TryGetFiringOrigin(Vector2Int from, Vector2Int to, out Vector2Int firingOrigin)
        {
            if (GridLineOfSight.HasLineOfSight(lineOfSightMap, from, to))
            { firingOrigin = from; return true; }
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

        protected override bool TryCommit(AbilityTarget target, out AbilityFailReason reason)
        {
            ResetPending();
            LastResult = default;
            try
            {
                if (!TryReadValidTarget(target, out ShotParameters parameters, out Vector2Int firingOrigin))
                { reason = AbilityFailReason.InvalidTarget; return false; }
                pendingTarget = target.Unit;
                pendingFiringOrigin = firingOrigin;
                pendingDistance = ShootRules.GetDistance(parameters.From, parameters.To);
                pendingHitChance = ShootRules.GetHitChance(parameters.Aim, parameters.Defense,
                    settings.GetHitChanceModifier(pendingDistance));
                pendingHit = NextRandomValue() * 100f < pendingHitChance;
                if (pendingHit)
                {
                    pendingGrazing = NextRandomValue() * 100f < ShootRules.GetDodgeChance(parameters.Dodge);
                    pendingDamage = ShootRules.GetDamage(
                        pendingGrazing ? ShootRules.GrazingDamage : ShootRules.BaseDamage, parameters.Armor);
                }

                if (!IsActive || Component == null || Owner == null)
                { reason = AbilityFailReason.NotGranted; ResetPending(); return false; }
                if (!Owner.IsTurnActive && !CanActivateOutOfTurn)
                { reason = AbilityFailReason.NotYourTurn; ResetPending(); return false; }
                if (!CanActivate(target, out reason)) { ResetPending(); return false; }
                if (!TryReadValidTarget(target, out ShotParameters current, out Vector2Int currentOrigin) ||
                    !parameters.Equals(current) || currentOrigin != pendingFiringOrigin)
                { reason = AbilityFailReason.Custom; ResetPending(); return false; }
            }
            catch
            {
                reason = AbilityFailReason.Custom;
                ResetPending();
                return false;
            }
            return base.TryCommit(target, out reason);
        }

        public ShootPreview GetPreview(Unit target)
        {
            AbilityTarget abilityTarget = AbilityTarget.ForUnit(target);
            AbilityFailReason reason = AbilityFailReason.NotGranted;
            bool canFire = Component != null && Component.CanActivate(this, abilityTarget, out reason);
            bool validTarget = TryReadValidTarget(abilityTarget, out ShotParameters parameters,
                out Vector2Int firingOrigin);
            if (!validTarget && reason == AbilityFailReason.None) reason = AbilityFailReason.InvalidTarget;
            int distance = validTarget ? ShootRules.GetDistance(parameters.From, parameters.To) : 0;
            float hitChance = validTarget
                ? ShootRules.GetHitChance(parameters.Aim, parameters.Defense, settings.GetHitChanceModifier(distance))
                : 0f;
            return new ShootPreview(target, distance, hitChance, firingOrigin, validTarget, canFire, reason);
        }

        protected override void OnActivate(AbilityTarget target)
        {
            if (visual != null)
            {
                visual.PlayShot(grid, pendingTarget, ReadOwnerCoordinates(), pendingFiringOrigin,
                    ApplyPendingResult, cancelled => EndAbility(cancelled));
                return;
            }
            ApplyPendingResult();
            EndAbility();
        }

        protected override void OnEnded(bool cancelled)
        {
            visual?.StopShot(false);
            ResetPending();
        }

        private Vector2Int ReadOwnerCoordinates()
        {
            if (Owner == null || !grid.TryGetUnitCoordinates(Owner, out Vector2Int coordinates))
                throw new InvalidOperationException("射击单位已离开棋盘。");
            return coordinates;
        }

        private void ApplyPendingResult()
        {
            if (damageApplied || pendingTarget == null) return;
            damageApplied = true;
            Unit target = pendingTarget;
            string targetName = target.DisplayName;
            Vector3 targetPosition = target.transform.position;
            if (pendingHit && target.IsAlive)
                target.Attributes.ApplyBaseDelta(AttributeType.Health, -pendingDamage);
            LastResult = new ShootResult(target, targetName, targetPosition, pendingHit, pendingGrazing,
                pendingDamage, pendingDistance, pendingHitChance, pendingFiringOrigin);
        }

        private void ResetPending()
        {
            pendingHit = pendingGrazing = damageApplied = false;
            pendingDamage = pendingHitChance = 0f;
            pendingDistance = 0;
            pendingFiringOrigin = default;
            pendingTarget = null;
        }

        private readonly struct ShotParameters : IEquatable<ShotParameters>
        {
            public readonly Vector2Int From, To;
            public readonly float SightRange, Aim, Defense, Dodge, Armor, TargetHealth, ActionPoints;

            public ShotParameters(Vector2Int from, Vector2Int to, Unit owner, Unit target)
            {
                From = from; To = to;
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
