using System;
using System.Collections.Generic;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Units
{
    /// <summary>一次移动的只读报价，不提交占用、AP 或动画。</summary>
    public sealed class MovePlan
    {
        public Vector2Int Start { get; }
        public Vector2Int Destination { get; }
        public IReadOnlyList<Vector2Int> Path { get; }
        public int TotalCost { get; }
        public int ActionPointCost { get; }

        internal MovePlan(IReadOnlyList<Vector2Int> path, int totalCost, int apCost)
        {
            Path = new List<Vector2Int>(path).AsReadOnly();
            Start = path[0];
            Destination = path[path.Count - 1];
            TotalCost = totalCost;
            ActionPointCost = apCost;
        }
    }

    /// <summary>按加权路径消耗 1 或 2 AP 的移动能力；逻辑提交后才开始表现。</summary>
    public sealed class MoveAbility : AbilityBase
    {
        private readonly GridManager grid;
        private readonly UnitVisualController visual;
        private MovePlan committedPlan;
        private bool ownsMovingTag;

        public MoveAbility(GridManager grid, UnitVisualController visual = null) : base("Move", AbilityTargetType.Cell)
        {
            this.grid = grid != null ? grid : throw new ArgumentNullException(nameof(grid));
            this.visual = visual;
            DisplayName = "移动";
            AddBlockedTag(GameplayTag.Bound);
            AddBlockedTag(GameplayTag.Moving);
        }

        public int BudgetPerActionPoint => Owner != null && Owner.Attributes.TryGetValue(AttributeType.Mobility, out float value)
            ? Mathf.FloorToInt(Mathf.Clamp(value, 0, int.MaxValue / 4)) : 0;

        public GridReachabilityResult GetReachableCells()
        {
            if (Owner == null || !grid.TryGetUnitCoordinates(Owner, out Vector2Int start)) return null;
            int ap = Owner.Attributes.TryGetValue(AttributeType.ActionPoints, out float value)
                ? Mathf.FloorToInt(Mathf.Clamp(value, 0, 2)) : 0;
            return grid.GetReachableCells(start, BudgetPerActionPoint * ap, Owner);
        }

        public bool TryGetMovePlan(Vector2Int destination, out MovePlan plan, out AbilityFailReason reason)
        {
            plan = null;
            reason = AbilityFailReason.NotGranted;
            if (Component == null || !Component.CanActivate(this, AbilityTarget.ForCell(destination), out reason)) return false;
            return TryBuildPlan(destination, out plan, out reason);
        }

        public override bool CanActivate(AbilityTarget target, out AbilityFailReason reason)
        {
            if (!base.CanActivate(target, out reason)) return false;
            if (!Owner.isActiveAndEnabled || (visual != null && !visual.isActiveAndEnabled))
            { reason = AbilityFailReason.Custom; return false; }
            return TryBuildPlan(target.Cell, out _, out reason);
        }

        public override int GetActionPointCost(AbilityTarget target)
        {
            return target.Type == AbilityTargetType.Cell && TryBuildPlan(target.Cell, out MovePlan plan, out _)
                ? plan.ActionPointCost : ActionPointCost;
        }

        private bool TryBuildPlan(Vector2Int destination, out MovePlan plan, out AbilityFailReason reason)
        {
            plan = null;
            reason = AbilityFailReason.InvalidTarget;
            if (Owner == null || grid == null || !grid.TryGetUnitCoordinates(Owner, out Vector2Int start) ||
                start == destination || BudgetPerActionPoint <= 0 ||
                !grid.TryGetCell(destination, out GridCell target) || target.Occupant != null ||
                target.BlockState != BlockState.Unblocked) return false;
            var reachable = grid.GetReachableCells(start, BudgetPerActionPoint * 2, Owner);
            if (!reachable.ReachableCosts.TryGetValue(destination, out int cost) ||
                !reachable.TryGetPath(destination, out IReadOnlyList<Vector2Int> path)) return false;
            int apCost = cost <= BudgetPerActionPoint ? 1 : 2;
            if (!Owner.Attributes.TryGetValue(AttributeType.ActionPoints, out float ap) || ap < apCost)
            { reason = AbilityFailReason.NotEnoughActionPoints; return false; }
            plan = new MovePlan(path, cost, apCost);
            reason = AbilityFailReason.None;
            return true;
        }

        protected override bool TryCommit(AbilityTarget target, out AbilityFailReason reason)
        {
            if (!TryBuildPlan(target.Cell, out MovePlan plan, out reason)) return false;
            committedPlan = plan;
            if (!grid.TryMoveUnit(Owner, plan.Destination))
            { committedPlan = null; reason = AbilityFailReason.InvalidTarget; return false; }
            CommitCost(plan.ActionPointCost);
            return true;
        }

        protected override void OnActivate(AbilityTarget target)
        {
            ownsMovingTag = true;
            Owner.Tags.AddTag(GameplayTag.Moving);
            if (!IsActive) return;
            if (visual != null)
                visual.PlayMovement(grid, committedPlan.Path, cancelled => EndAbility(cancelled));
            else
            {
                Owner.transform.position = grid.GridToWorld(committedPlan.Destination);
                EndAbility();
            }
        }

        protected override void OnEnded(bool cancelled)
        {
            if (visual != null) visual.StopMovement(false);
            if (Owner != null && committedPlan != null && grid != null)
                Owner.transform.position = grid.GridToWorld(committedPlan.Destination);
            if (ownsMovingTag && Owner != null) Owner.Tags.RemoveTag(GameplayTag.Moving);
            ownsMovingTag = false;
            committedPlan = null;
        }
    }
}
