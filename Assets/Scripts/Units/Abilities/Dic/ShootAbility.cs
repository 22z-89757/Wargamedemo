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
        private readonly GridManager grid;
        private readonly IShootRandomSource randomSource;
        private readonly ShotEvaluator evaluator;
        private readonly IUnitShootVisual visual;
        private bool pendingHit, pendingGrazing, damageApplied;
        private float pendingDamage;
        private HitChanceBreakdown pendingBreakdown;
        private int pendingDistance;
        private Vector2Int pendingFiringOrigin;
        private Unit pendingTarget;
        private int activationVersion;

        public ShootResult LastResult { get; private set; }

        public ShootAbility(GridManager grid, ShootSettings settings, IShootRandomSource randomSource = null)
            : this(grid, settings, null, randomSource) { }

        public ShootAbility(GridManager grid, ShootSettings settings, IUnitShootVisual visual,
            IShootRandomSource randomSource = null) : base("Shoot", AbilityTargetType.Unit)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            ShootSettings.Snapshot snapshot =
                (settings ?? throw new ArgumentNullException(nameof(settings))).CreateSnapshot();
            evaluator = new ShotEvaluator(grid, snapshot);
            this.visual = visual;
            this.randomSource = randomSource ?? new UnityShootRandomSource();
            ActionPointCost = ShootRules.ActionPointCost;
            DisplayName = "射击";
        }

        public override bool IsValidTarget(AbilityTarget target) => TryEvaluateShot(target, out _);

        private bool TryEvaluateShot(AbilityTarget target, out ShotEvaluation evaluation)
        {
            evaluation = default;
            return base.IsValidTarget(target) &&
                evaluator.TryEvaluateCurrent(Owner, target.Unit, out evaluation);
        }

        internal bool TryEvaluateFrom(Vector2Int from, Unit target, out ShotEvaluation evaluation) =>
            evaluator.TryEvaluate(Owner, from, target, out evaluation);

        internal bool TryGetFiringOrigin(Vector2Int from, Vector2Int to, out Vector2Int firingOrigin)
        {
            return evaluator.TryGetFiringOrigin(from, to, out firingOrigin);
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
                int version = ++activationVersion;
                visual.PlayShot(grid, pendingTarget, ReadOwnerCoordinates(), pendingFiringOrigin,
                    () =>
                    {
                        if (version == activationVersion) ApplyPendingResult();
                    },
                    cancelled =>
                    {
                        if (version == activationVersion) EndAbility(cancelled);
                    });
                return;
            }
            ApplyPendingResult();
            EndAbility();
        }

        protected override void OnEnded(bool cancelled)
        {
            activationVersion++;
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
