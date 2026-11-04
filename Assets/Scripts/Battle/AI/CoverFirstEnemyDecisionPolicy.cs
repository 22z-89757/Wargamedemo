using System;
using System.Collections.Generic;
using UnityEngine;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Battle.AI
{
    /// <summary>确定性的掩体优先策略；每次只规划一个公共能力行动。</summary>
    internal sealed class CoverFirstEnemyDecisionPolicy : IEnemyDecisionPolicy
    {
        public EnemyActionPlan Decide(EnemyDecisionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            Unit actor = context.Actor;
            GridManager grid = context.Grid;
            if (!IsValidActor(actor, grid, out Vector2Int start))
                return EnemyActionPlan.NoAction(actor);

            MoveAbility move = actor.Abilities.GetAbility<MoveAbility>();
            ShootAbility shoot = actor.Abilities.GetAbility<ShootAbility>();
            bool canPrepareShoot = shoot != null &&
                actor.Abilities.CanPrepareTarget(shoot, out _);

            List<MoveCandidate> destinations = GetOneActionPointDestinations(actor, move, grid, start);
            if (canPrepareShoot && TryChooseCoverAction(context, shoot, start, destinations,
                    out EnemyActionPlan coverPlan))
                return coverPlan;

            if (canPrepareShoot && TryChooseCurrentShot(context, shoot, start,
                    out EnemyActionPlan shotPlan))
                return shotPlan;

            if (move != null && TryChooseFiringPosition(context, shoot, destinations,
                    out EnemyActionPlan firingMove))
                return firingMove;

            if (move != null && TryChooseApproach(context, start, destinations,
                    out EnemyActionPlan approachMove))
                return approachMove;

            return EnemyActionPlan.NoAction(actor);
        }

        private static bool IsValidActor(Unit actor, GridManager grid, out Vector2Int start)
        {
            start = default;
            return actor != null && actor.IsAlive && actor.isActiveAndEnabled &&
                actor.Faction == Faction.Enemy && actor.IsTurnActive &&
                grid.TryGetUnitCoordinates(actor, out start);
        }

        private static List<MoveCandidate> GetOneActionPointDestinations(Unit actor,
            MoveAbility move, GridManager grid, Vector2Int start)
        {
            var result = new List<MoveCandidate>();
            if (move == null || move.BudgetPerActionPoint <= 0 ||
                !actor.Abilities.CanPrepareTarget(move, out _))
                return result;

            GridReachabilityResult reachable = grid.GetReachableCells(
                start, move.BudgetPerActionPoint, actor);
            foreach (KeyValuePair<Vector2Int, int> pair in reachable.ReachableCosts)
            {
                if (!move.TryGetMovePlan(pair.Key, out MovePlan plan, out _) ||
                    plan.ActionPointCost != 1)
                    continue;
                result.Add(new MoveCandidate(pair.Key, pair.Value, plan.ActionPointCost));
            }
            return result;
        }

        private static bool TryChooseCoverAction(EnemyDecisionContext context,
            ShootAbility shoot, Vector2Int start, IReadOnlyList<MoveCandidate> destinations,
            out EnemyActionPlan plan)
        {
            CoverCandidate best = default;
            bool found = false;
            EvaluateCoverCell(context, shoot, start, 0, 0, true, ref found, ref best);

            float actionPoints = context.Actor.Attributes.GetValue(AttributeType.ActionPoints);
            for (int i = 0; i < destinations.Count; i++)
            {
                MoveCandidate destination = destinations[i];
                if (actionPoints < destination.ActionPointCost + shoot.ActionPointCost)
                    continue;
                EvaluateCoverCell(context, shoot, destination.Coordinates,
                    destination.MovementCost, destination.ActionPointCost, false,
                    ref found, ref best);
            }

            if (!found)
            {
                plan = null;
                return false;
            }

            plan = best.IsCurrent
                ? EnemyActionPlan.Shoot(context.Actor, best.Target)
                : EnemyActionPlan.Move(context.Actor, best.Coordinates);
            return true;
        }

        private static void EvaluateCoverCell(EnemyDecisionContext context,
            ShootAbility shoot, Vector2Int coordinates, int movementCost, int actionPointCost,
            bool isCurrent, ref bool found, ref CoverCandidate best)
        {
            if (!context.Grid.TryGetCell(coordinates, out GridCell cell)) return;
            for (int targetIndex = 0; targetIndex < context.Players.Count; targetIndex++)
            {
                Unit target = context.Players[targetIndex];
                if (!IsValidTarget(target, context.Grid, out Vector2Int targetCell) ||
                    !shoot.TryEvaluateFrom(coordinates, target, out ShotEvaluation evaluation))
                    continue;

                if (isCurrent && !context.Actor.Abilities.CanActivate(shoot,
                        AbilityTarget.ForUnit(target), out _))
                    continue;

                CoverType cover = CoverDirectionRules.GetStrongestCover(cell, targetCell);
                if (cover == CoverType.NoCover) continue;
                EnemyActionPlan candidatePlan = isCurrent
                    ? EnemyActionPlan.Shoot(context.Actor, target)
                    : EnemyActionPlan.Move(context.Actor, coordinates);
                if (context.IsExcluded(candidatePlan)) continue;

                var candidate = new CoverCandidate(coordinates, target, cover,
                    evaluation.Breakdown.FinalChance, movementCost, actionPointCost,
                    targetIndex, isCurrent);
                if (!found || CoverCandidate.IsBetter(candidate, best))
                {
                    best = candidate;
                    found = true;
                }
            }
        }

        private static bool TryChooseCurrentShot(EnemyDecisionContext context,
            ShootAbility shoot, Vector2Int start, out EnemyActionPlan plan)
        {
            Unit bestTarget = null;
            float bestChance = float.NegativeInfinity;
            int bestIndex = int.MaxValue;
            for (int i = 0; i < context.Players.Count; i++)
            {
                Unit target = context.Players[i];
                if (!IsValidTarget(target, context.Grid, out _) ||
                    !context.Actor.Abilities.CanActivate(shoot,
                        AbilityTarget.ForUnit(target), out _) ||
                    !shoot.TryEvaluateFrom(start, target, out ShotEvaluation evaluation))
                    continue;
                EnemyActionPlan candidatePlan = EnemyActionPlan.Shoot(context.Actor, target);
                if (context.IsExcluded(candidatePlan)) continue;
                float chance = evaluation.Breakdown.FinalChance;
                if (bestTarget == null || chance > bestChance ||
                    (chance == bestChance && i < bestIndex))
                {
                    bestTarget = target;
                    bestChance = chance;
                    bestIndex = i;
                }
            }

            plan = bestTarget != null ? EnemyActionPlan.Shoot(context.Actor, bestTarget) : null;
            return plan != null;
        }

        private static bool TryChooseFiringPosition(EnemyDecisionContext context,
            ShootAbility shoot, IReadOnlyList<MoveCandidate> destinations,
            out EnemyActionPlan plan)
        {
            plan = null;
            if (shoot == null || destinations.Count == 0) return false;
            FiringPositionCandidate best = default;
            bool found = false;
            for (int i = 0; i < destinations.Count; i++)
            {
                MoveCandidate destination = destinations[i];
                EnemyActionPlan movePlan = EnemyActionPlan.Move(context.Actor, destination.Coordinates);
                if (context.IsExcluded(movePlan) ||
                    !context.Grid.TryGetCell(destination.Coordinates, out GridCell cell))
                    continue;

                for (int targetIndex = 0; targetIndex < context.Players.Count; targetIndex++)
                {
                    Unit target = context.Players[targetIndex];
                    if (!IsValidTarget(target, context.Grid, out Vector2Int targetCell) ||
                        !shoot.TryEvaluateFrom(destination.Coordinates, target,
                            out ShotEvaluation evaluation) ||
                        CoverDirectionRules.GetStrongestCover(cell, targetCell) != CoverType.NoCover)
                        continue;
                    var candidate = new FiringPositionCandidate(destination.Coordinates,
                        evaluation.Breakdown.FinalChance, destination.MovementCost,
                        targetIndex);
                    if (!found || FiringPositionCandidate.IsBetter(candidate, best))
                    {
                        best = candidate;
                        found = true;
                    }
                }
            }

            if (found) plan = EnemyActionPlan.Move(context.Actor, best.Coordinates);
            return found;
        }

        private static bool TryChooseApproach(EnemyDecisionContext context,
            Vector2Int start, IReadOnlyList<MoveCandidate> destinations,
            out EnemyActionPlan plan)
        {
            plan = null;
            int currentDistance = GetNearestPlayerDistance(context, start);
            if (currentDistance == int.MaxValue) return false;

            MoveCandidate best = default;
            int bestDistance = currentDistance;
            bool found = false;
            for (int i = 0; i < destinations.Count; i++)
            {
                MoveCandidate candidate = destinations[i];
                EnemyActionPlan movePlan = EnemyActionPlan.Move(context.Actor, candidate.Coordinates);
                if (context.IsExcluded(movePlan)) continue;
                int distance = GetNearestPlayerDistance(context, candidate.Coordinates);
                if (distance >= currentDistance) continue;
                if (!found || distance < bestDistance ||
                    (distance == bestDistance && MoveCandidate.IsBetter(candidate, best)))
                {
                    best = candidate;
                    bestDistance = distance;
                    found = true;
                }
            }

            if (found) plan = EnemyActionPlan.Move(context.Actor, best.Coordinates);
            return found;
        }

        private static int GetNearestPlayerDistance(EnemyDecisionContext context,
            Vector2Int from)
        {
            int nearest = int.MaxValue;
            for (int i = 0; i < context.Players.Count; i++)
            {
                Unit target = context.Players[i];
                if (!IsValidTarget(target, context.Grid, out Vector2Int targetCell)) continue;
                nearest = Math.Min(nearest, ShootRules.GetDistance(from, targetCell));
            }
            return nearest;
        }

        private static bool IsValidTarget(Unit target, GridManager grid,
            out Vector2Int coordinates)
        {
            coordinates = default;
            return target != null && target.IsAlive && target.isActiveAndEnabled &&
                target.Faction == Faction.Player && grid.TryGetUnitCoordinates(target, out coordinates);
        }

        private readonly struct MoveCandidate
        {
            public MoveCandidate(Vector2Int coordinates, int movementCost, int actionPointCost)
            {
                Coordinates = coordinates;
                MovementCost = movementCost;
                ActionPointCost = actionPointCost;
            }

            public Vector2Int Coordinates { get; }
            public int MovementCost { get; }
            public int ActionPointCost { get; }

            public static bool IsBetter(MoveCandidate left, MoveCandidate right) =>
                left.MovementCost < right.MovementCost ||
                (left.MovementCost == right.MovementCost &&
                 (left.Coordinates.x < right.Coordinates.x ||
                  (left.Coordinates.x == right.Coordinates.x &&
                   left.Coordinates.y < right.Coordinates.y)));
        }

        private readonly struct CoverCandidate
        {
            public CoverCandidate(Vector2Int coordinates, Unit target, CoverType cover,
                float hitChance, int movementCost, int actionPointCost,
                int targetIndex, bool isCurrent)
            {
                Coordinates = coordinates;
                Target = target;
                Cover = cover;
                HitChance = hitChance;
                MovementCost = movementCost;
                ActionPointCost = actionPointCost;
                TargetIndex = targetIndex;
                IsCurrent = isCurrent;
            }

            public Vector2Int Coordinates { get; }
            public Unit Target { get; }
            public CoverType Cover { get; }
            public float HitChance { get; }
            public int MovementCost { get; }
            public int ActionPointCost { get; }
            public int TargetIndex { get; }
            public bool IsCurrent { get; }

            public static bool IsBetter(CoverCandidate left, CoverCandidate right)
            {
                if (left.Cover != right.Cover) return left.Cover > right.Cover;
                if (left.HitChance != right.HitChance) return left.HitChance > right.HitChance;
                if (left.MovementCost != right.MovementCost)
                    return left.MovementCost < right.MovementCost;
                if (left.TargetIndex != right.TargetIndex)
                    return left.TargetIndex < right.TargetIndex;
                if (left.Coordinates.x != right.Coordinates.x)
                    return left.Coordinates.x < right.Coordinates.x;
                return left.Coordinates.y < right.Coordinates.y;
            }
        }

        private readonly struct FiringPositionCandidate
        {
            public FiringPositionCandidate(Vector2Int coordinates, float hitChance,
                int movementCost, int targetIndex)
            {
                Coordinates = coordinates;
                HitChance = hitChance;
                MovementCost = movementCost;
                TargetIndex = targetIndex;
            }

            public Vector2Int Coordinates { get; }
            public float HitChance { get; }
            public int MovementCost { get; }
            public int TargetIndex { get; }

            public static bool IsBetter(FiringPositionCandidate left,
                FiringPositionCandidate right)
            {
                if (left.HitChance != right.HitChance) return left.HitChance > right.HitChance;
                if (left.MovementCost != right.MovementCost)
                    return left.MovementCost < right.MovementCost;
                if (left.TargetIndex != right.TargetIndex)
                    return left.TargetIndex < right.TargetIndex;
                if (left.Coordinates.x != right.Coordinates.x)
                    return left.Coordinates.x < right.Coordinates.x;
                return left.Coordinates.y < right.Coordinates.y;
            }
        }
    }
}
