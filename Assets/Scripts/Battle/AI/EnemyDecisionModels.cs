using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Battle.AI
{
    internal enum EnemyActionKind
    {
        None,
        Move,
        Shoot
    }

    /// <summary>敌人策略返回的只读单步行动；能力提交由回合执行器负责。</summary>
    internal sealed class EnemyActionPlan
    {
        private EnemyActionPlan(Unit actor, EnemyActionKind kind, Unit target,
            Vector2Int destination)
        {
            Actor = actor != null ? actor : throw new ArgumentNullException(nameof(actor));
            Kind = kind;
            Target = target;
            Destination = destination;
        }

        public Unit Actor { get; }
        public EnemyActionKind Kind { get; }
        public Unit Target { get; }
        public Vector2Int Destination { get; }

        public static EnemyActionPlan NoAction(Unit actor) =>
            new EnemyActionPlan(actor, EnemyActionKind.None, null, default);

        public static EnemyActionPlan Move(Unit actor, Vector2Int destination) =>
            new EnemyActionPlan(actor, EnemyActionKind.Move, null, destination);

        public static EnemyActionPlan Shoot(Unit actor, Unit target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return new EnemyActionPlan(actor, EnemyActionKind.Shoot, target, default);
        }

        public EnemyActionSignature GetSignature() => new EnemyActionSignature(this);
    }

    /// <summary>一次提交尝试的稳定身份，用于排除未改变状态下已经失败的计划。</summary>
    internal readonly struct EnemyActionSignature : IEquatable<EnemyActionSignature>
    {
        public EnemyActionSignature(EnemyActionPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            ActorId = plan.Actor.UnitId;
            Kind = plan.Kind;
            TargetId = plan.Target != null ? plan.Target.UnitId : 0;
            Destination = plan.Destination;
        }

        public int ActorId { get; }
        public EnemyActionKind Kind { get; }
        public int TargetId { get; }
        public Vector2Int Destination { get; }

        public bool Equals(EnemyActionSignature other) => ActorId == other.ActorId &&
            Kind == other.Kind && TargetId == other.TargetId && Destination == other.Destination;

        public override bool Equals(object obj) => obj is EnemyActionSignature other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ActorId, Kind, TargetId, Destination);
    }

    /// <summary>单次决策的输入快照；集合均在构造时复制，策略不能改写调用方状态。</summary>
    internal sealed class EnemyDecisionContext
    {
        private readonly HashSet<EnemyActionSignature> excludedActions;

        public EnemyDecisionContext(Unit actor, IEnumerable<Unit> players, GridManager grid,
            IEnumerable<EnemyActionSignature> excludedActions = null)
        {
            Actor = actor != null ? actor : throw new ArgumentNullException(nameof(actor));
            Grid = grid != null ? grid : throw new ArgumentNullException(nameof(grid));
            if (players == null) throw new ArgumentNullException(nameof(players));
            Players = new ReadOnlyCollection<Unit>(new List<Unit>(players));
            this.excludedActions = excludedActions == null
                ? new HashSet<EnemyActionSignature>()
                : new HashSet<EnemyActionSignature>(excludedActions);
        }

        public Unit Actor { get; }
        public IReadOnlyList<Unit> Players { get; }
        public GridManager Grid { get; }

        public bool IsExcluded(EnemyActionPlan plan) => plan != null &&
            excludedActions.Contains(plan.GetSignature());
    }

    internal interface IEnemyDecisionPolicy
    {
        EnemyActionPlan Decide(EnemyDecisionContext context);
    }
}
