using System;
using System.Collections.Generic;
using UnityEngine;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Battle.AI
{
    /// <summary>按注册顺序编排敌人公共能力，并以能力结束事件作为唯一完成事实。</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyTurnController : MonoBehaviour
    {
        private readonly List<Unit> enemySnapshot = new List<Unit>();
        private readonly HashSet<EnemyActionSignature> failedActions =
            new HashSet<EnemyActionSignature>();
        private BattleTurnManager turns;
        private GridManager grid;
        private IEnemyDecisionPolicy policy;
        private Unit currentUnit;
        private Unit waitingUnit;
        private AbilityBase waitingAbility;
        private int nextEnemyIndex;
        private int turnGeneration;
        private int waitingGeneration;
        private int failedStateHash;
        private bool hasFailedState;
        private bool enemyTurnActive;
        private bool endRequested;
        private bool pumping;
        private bool pumpRequested;

        public bool IsInitialized { get; private set; }
        internal bool IsRunningEnemyTurn => enemyTurnActive;
        internal Unit CurrentUnit => currentUnit;
        internal AbilityBase WaitingAbility => waitingAbility;

        public void Initialize(BattleTurnManager turnManager, GridManager gridManager)
        {
            Initialize(turnManager, gridManager, new CoverFirstEnemyDecisionPolicy());
        }

        internal void Initialize(BattleTurnManager turnManager, GridManager gridManager,
            IEnemyDecisionPolicy decisionPolicy)
        {
            if (IsInitialized) throw new InvalidOperationException("敌方回合控制器已经初始化。");
            turns = turnManager != null ? turnManager :
                throw new ArgumentNullException(nameof(turnManager));
            grid = gridManager != null ? gridManager :
                throw new ArgumentNullException(nameof(gridManager));
            policy = decisionPolicy ?? throw new ArgumentNullException(nameof(decisionPolicy));
            turns.FactionTurnStarted += OnFactionTurnStarted;
            turns.FactionTurnEnded += OnFactionTurnEnded;
            turns.UnitUnregistered += OnUnitUnregistered;
            grid.Changed += OnGridChanged;
            IsInitialized = true;
        }

        public void Shutdown()
        {
            if (!IsInitialized) return;
            IsInitialized = false;
            turnGeneration++;
            enemyTurnActive = false;
            endRequested = true;
            pumpRequested = false;
            turns.FactionTurnStarted -= OnFactionTurnStarted;
            turns.FactionTurnEnded -= OnFactionTurnEnded;
            turns.UnitUnregistered -= OnUnitUnregistered;
            grid.Changed -= OnGridChanged;
            Unit interrupted = waitingUnit;
            ClearWaitingSubscriptions();
            if (interrupted != null) interrupted.Abilities.CancelAll();
            ResetTurnState();
            turns = null;
            grid = null;
            policy = null;
        }

        private void OnDisable()
        {
            HandleDisabled();
        }

        internal void HandleDisabled() => Shutdown();

        private void OnFactionTurnStarted(Faction faction)
        {
            if (!IsInitialized || faction != Faction.Enemy) return;
            turnGeneration++;
            enemyTurnActive = true;
            endRequested = false;
            currentUnit = null;
            nextEnemyIndex = 0;
            ClearFailures();
            enemySnapshot.Clear();
            enemySnapshot.AddRange(turns.GetUnits(Faction.Enemy));
            RequestPump();
        }

        private void OnFactionTurnEnded(Faction faction)
        {
            if (!IsInitialized || faction != Faction.Enemy) return;
            turnGeneration++;
            enemyTurnActive = false;
            endRequested = true;
            Unit interrupted = waitingUnit;
            ClearWaitingSubscriptions();
            if (interrupted != null) interrupted.Abilities.CancelAll();
            ResetTurnState();
        }

        private void RequestPump()
        {
            if (!CanContinueTurn()) return;
            if (pumping)
            {
                pumpRequested = true;
                return;
            }

            pumping = true;
            try
            {
                do
                {
                    pumpRequested = false;
                    PumpUntilWaitingOrFinished();
                }
                while (pumpRequested && CanContinueTurn());
            }
            finally
            {
                pumping = false;
            }
        }

        private void PumpUntilWaitingOrFinished()
        {
            while (CanContinueTurn() && waitingAbility == null)
            {
                if (currentUnit == null && !TrySelectNextUnit())
                {
                    EndEnemyTurnOnce();
                    return;
                }

                if (!IsActionable(currentUnit))
                {
                    FinishCurrentUnit();
                    continue;
                }

                AbilityBase existing = currentUnit.Abilities.ActiveExclusiveAbility;
                if (existing != null)
                {
                    BeginWaiting(currentUnit, existing);
                    if (existing.IsActive) return;
                    CompleteWaiting();
                    continue;
                }

                RefreshFailuresForCurrentState();
                var context = new EnemyDecisionContext(currentUnit,
                    turns.GetUnits(Faction.Player), grid, failedActions);
                EnemyActionPlan plan;
                try
                {
                    plan = policy.Decide(context);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    FinishCurrentUnit();
                    continue;
                }

                if (plan == null || plan.Kind == EnemyActionKind.None)
                {
                    FinishCurrentUnit();
                    continue;
                }

                if (plan.Actor != currentUnit || failedActions.Contains(plan.GetSignature()))
                {
                    FinishCurrentUnit();
                    continue;
                }

                if (!TryResolveAbility(plan, out AbilityBase ability, out AbilityTarget target))
                {
                    MarkFailedWithoutStateChange(plan);
                    continue;
                }

                int beforeState = ComputeDecisionStateHash();
                BeginWaiting(currentUnit, ability);
                bool activated;
                try
                {
                    activated = currentUnit.Abilities.TryActivate(ability, target, out _);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    activated = false;
                }

                if (!activated)
                {
                    ClearWaitingSubscriptions();
                    MarkFailed(plan, beforeState);
                    continue;
                }

                if (waitingAbility != null && ability.IsActive) return;
                if (waitingAbility != null) CompleteWaiting();
            }
        }

        private bool TrySelectNextUnit()
        {
            ClearFailures();
            while (nextEnemyIndex < enemySnapshot.Count)
            {
                Unit candidate = enemySnapshot[nextEnemyIndex++];
                if (!IsActionable(candidate)) continue;
                currentUnit = candidate;
                return true;
            }
            return false;
        }

        private void FinishCurrentUnit()
        {
            currentUnit = null;
            ClearFailures();
        }

        private bool TryResolveAbility(EnemyActionPlan plan, out AbilityBase ability,
            out AbilityTarget target)
        {
            ability = null;
            target = default;
            if (plan.Kind == EnemyActionKind.Move)
            {
                ability = currentUnit.Abilities.GetAbility<MoveAbility>();
                target = AbilityTarget.ForCell(plan.Destination);
            }
            else if (plan.Kind == EnemyActionKind.Shoot && plan.Target != null)
            {
                ability = currentUnit.Abilities.GetAbility<ShootAbility>();
                target = AbilityTarget.ForUnit(plan.Target);
            }
            return ability != null;
        }

        private void BeginWaiting(Unit unit, AbilityBase ability)
        {
            ClearWaitingSubscriptions();
            waitingUnit = unit;
            waitingAbility = ability;
            waitingGeneration = turnGeneration;
            unit.Abilities.AbilityEnded += OnAbilityEnded;
            unit.Died += OnWaitingUnitInvalidated;
            unit.Destroyed += OnWaitingUnitInvalidated;
        }

        private void OnAbilityEnded(AbilityBase ability, bool cancelled)
        {
            if (ability != waitingAbility || waitingGeneration != turnGeneration) return;
            CompleteWaiting();
            RequestPump();
        }

        private void CompleteWaiting()
        {
            ClearWaitingSubscriptions();
            ClearFailures();
        }

        private void ClearWaitingSubscriptions()
        {
            if (waitingUnit != null)
            {
                waitingUnit.Abilities.AbilityEnded -= OnAbilityEnded;
                waitingUnit.Died -= OnWaitingUnitInvalidated;
                waitingUnit.Destroyed -= OnWaitingUnitInvalidated;
            }
            waitingUnit = null;
            waitingAbility = null;
            waitingGeneration = 0;
        }

        private void OnWaitingUnitInvalidated(Unit unit)
        {
            if (unit != waitingUnit) return;
            ClearWaitingSubscriptions();
            FinishCurrentUnit();
            RequestPump();
        }

        private void OnUnitUnregistered(Unit unit)
        {
            if (!enemyTurnActive || unit == null) return;
            if (unit == waitingUnit)
            {
                ClearWaitingSubscriptions();
                unit.Abilities.CancelAll();
                FinishCurrentUnit();
            }
            else if (unit == currentUnit)
                FinishCurrentUnit();
            RequestPump();
        }

        private void OnGridChanged()
        {
            if (!enemyTurnActive || waitingUnit == null || IsActionable(waitingUnit)) return;
            Unit invalid = waitingUnit;
            ClearWaitingSubscriptions();
            invalid.Abilities.CancelAll();
            FinishCurrentUnit();
            RequestPump();
        }

        private bool IsActionable(Unit unit)
        {
            if (unit == null || !unit.IsAlive || !unit.isActiveAndEnabled ||
                unit.Faction != Faction.Enemy || !unit.IsTurnActive ||
                !grid.TryGetUnitCoordinates(unit, out _))
                return false;
            IReadOnlyList<Unit> registered = turns.Units;
            for (int i = 0; i < registered.Count; i++)
                if (registered[i] == unit) return true;
            return false;
        }

        private bool CanContinueTurn() => IsInitialized && enemyTurnActive && !endRequested &&
            turns != null && turns.State == BattleState.Running &&
            turns.CurrentFaction == Faction.Enemy;

        private void EndEnemyTurnOnce()
        {
            if (!CanContinueTurn() || waitingAbility != null) return;
            endRequested = true;
            turns.EndCurrentTurn();
        }

        private void RefreshFailuresForCurrentState()
        {
            int state = ComputeDecisionStateHash();
            if (hasFailedState && state != failedStateHash) ClearFailures();
        }

        private void MarkFailedWithoutStateChange(EnemyActionPlan plan)
        {
            failedStateHash = ComputeDecisionStateHash();
            hasFailedState = true;
            failedActions.Add(plan.GetSignature());
        }

        private void MarkFailed(EnemyActionPlan plan, int beforeState)
        {
            int afterState = ComputeDecisionStateHash();
            if (afterState != beforeState)
            {
                ClearFailures();
                return;
            }
            failedStateHash = afterState;
            hasFailedState = true;
            failedActions.Add(plan.GetSignature());
        }

        private void ClearFailures()
        {
            failedActions.Clear();
            hasFailedState = false;
            failedStateHash = 0;
        }

        private int ComputeDecisionStateHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + grid.Revision;
                hash = AddUnitState(hash, currentUnit);
                IReadOnlyList<Unit> units = turns.Units;
                for (int i = 0; i < units.Count; i++)
                    if (units[i] != null && units[i].Faction == Faction.Player)
                        hash = AddUnitState(hash, units[i]);
                return hash;
            }
        }

        private int AddUnitState(int hash, Unit unit)
        {
            unchecked
            {
                if (unit == null) return hash * 31;
                hash = hash * 31 + unit.UnitId;
                hash = hash * 31 + unit.IsAlive.GetHashCode();
                hash = hash * 31 + unit.IsTurnActive.GetHashCode();
                if (grid.TryGetUnitCoordinates(unit, out Vector2Int coordinates))
                    hash = hash * 31 + coordinates.GetHashCode();
                foreach (AttributeType attribute in DecisionAttributes)
                    if (unit.Attributes.TryGetValue(attribute, out float value))
                        hash = hash * 31 + value.GetHashCode();
                hash = hash * 31 + unit.Buffs.Buffs.Count;
                hash = hash * 31 + unit.Tags.ActiveTags.Count;
                return hash;
            }
        }

        private void ResetTurnState()
        {
            enemySnapshot.Clear();
            currentUnit = null;
            nextEnemyIndex = 0;
            ClearFailures();
        }

        private static readonly AttributeType[] DecisionAttributes =
        {
            AttributeType.ActionPoints, AttributeType.Health, AttributeType.Mobility,
            AttributeType.Aim, AttributeType.SightRange, AttributeType.Defense,
            AttributeType.Dodge, AttributeType.Armor
        };
    }
}
