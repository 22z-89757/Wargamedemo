using System;
using UnityEngine;
using Wargame.Battle;
using Wargame.GridSystem;

namespace Wargame.Units
{
    /// <summary>
    /// 对已登记在棋盘上的敌方单位执行一次基于 LOS 的射击；方向性掩体只修正命中率，
    /// 来向始终使用双方实际占格，不使用探头射线起点。
    /// </summary>
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
        private float pendingDamage;
        private HitChanceBreakdown pendingBreakdown;
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

        public override bool IsValidTarget(AbilityTarget target) => TryEvaluateShot(target, out _);

        private bool TryEvaluateShot(AbilityTarget target, out ShotEvaluation evaluation)
        {
            evaluation = default;
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
            int distance = ShootRules.GetDistance(from, to);
            if (distance > sightRange || !TryGetFiringOrigin(from, to, out Vector2Int firingOrigin))
                return false;

            var parameters = new ShotParameters(from, to, Owner, target.Unit);
            var context = new HitChanceContext(Owner, target.Unit, from, to, AttackKind.Ranged);
            System.Collections.Generic.IReadOnlyList<HitChanceModifier> modifiers =
                HitChanceModifierResolver.Resolve(in context, target.Unit.Buffs.Buffs);
            HitChanceBreakdown breakdown = ShootRules.GetHitChanceBreakdown(parameters.Aim,
                parameters.Defense, settings.GetHitChanceModifier(distance), modifiers);
            evaluation = new ShotEvaluation(parameters, distance, firingOrigin, breakdown);
            return true;
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
                if (!TryEvaluateShot(target, out ShotEvaluation evaluation))
                { reason = AbilityFailReason.InvalidTarget; return false; }
                pendingTarget = target.Unit;
                pendingFiringOrigin = evaluation.FiringOrigin;
                pendingDistance = evaluation.Distance;
                pendingBreakdown = evaluation.Breakdown;
                pendingHit = NextRandomValue() * 100f < pendingBreakdown.FinalChance;
                if (pendingHit)
                {
                    pendingGrazing = NextRandomValue() * 100f <
                        ShootRules.GetDodgeChance(evaluation.Parameters.Dodge);
                    pendingDamage = ShootRules.GetDamage(
                        pendingGrazing ? ShootRules.GrazingDamage : ShootRules.BaseDamage,
                        evaluation.Parameters.Armor);
                }

                if (!IsActive || Component == null || Owner == null)
                { reason = AbilityFailReason.NotGranted; ResetPending(); return false; }
                if (!Owner.IsTurnActive && !CanActivateOutOfTurn)
                { reason = AbilityFailReason.NotYourTurn; ResetPending(); return false; }
                if (!CanActivate(target, out reason)) { ResetPending(); return false; }
                if (!TryEvaluateShot(target, out ShotEvaluation current) || !evaluation.Equals(current))
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
            bool validTarget = TryEvaluateShot(abilityTarget, out ShotEvaluation evaluation);
            if (!validTarget && reason == AbilityFailReason.None) reason = AbilityFailReason.InvalidTarget;
            return new ShootPreview(target,
                validTarget ? evaluation.Distance : 0,
                validTarget ? evaluation.Breakdown : ShootRules.GetHitChanceBreakdown(0f, 0f, 0f),
                validTarget ? evaluation.FiringOrigin : default,
                validTarget, canFire, reason);
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
                pendingDamage, pendingDistance, pendingBreakdown, pendingFiringOrigin);
        }

        private void ResetPending()
        {
            pendingHit = pendingGrazing = damageApplied = false;
            pendingDamage = 0f;
            pendingBreakdown = null;
            pendingDistance = 0;
            pendingFiringOrigin = default;
            pendingTarget = null;
        }

        private readonly struct ShotParameters : IEquatable<ShotParameters>
        {
            public readonly Unit Owner, Target;
            public readonly Vector2Int From, To;
            public readonly float SightRange, Aim, Defense, Dodge, Armor, TargetHealth, ActionPoints;

            public ShotParameters(Vector2Int from, Vector2Int to, Unit owner, Unit target)
            {
                Owner = owner; Target = target;
                From = from; To = to;
                SightRange = owner.Attributes.GetValue(AttributeType.SightRange);
                Aim = owner.Attributes.GetValue(AttributeType.Aim);
                Defense = target.Attributes.GetValue(AttributeType.Defense);
                Dodge = target.Attributes.GetValue(AttributeType.Dodge);
                Armor = target.Attributes.GetValue(AttributeType.Armor);
                TargetHealth = target.Attributes.GetValue(AttributeType.Health);
                ActionPoints = owner.Attributes.GetValue(AttributeType.ActionPoints);
            }

            public bool Equals(ShotParameters other) => Owner == other.Owner && Target == other.Target &&
                From == other.From && To == other.To &&
                SightRange == other.SightRange && Aim == other.Aim && Defense == other.Defense &&
                Dodge == other.Dodge && Armor == other.Armor && TargetHealth == other.TargetHealth &&
                ActionPoints == other.ActionPoints;
        }

        private readonly struct ShotEvaluation : IEquatable<ShotEvaluation>
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
