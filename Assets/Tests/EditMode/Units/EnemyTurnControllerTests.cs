using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Wargame.Battle;
using Wargame.Battle.AI;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class EnemyTurnControllerTests : UnitTestBase
    {
        private GridManager grid;
        private BattleTurnManager turns;
        private EnemyTurnController controller;
        private Unit player;

        [SetUp]
        public void Setup()
        {
            grid = CreateComponent<GridManager>("Grid");
            grid.Initialize(20, 5);
            turns = CreateComponent<BattleTurnManager>("Turns");
            controller = CreateComponent<EnemyTurnController>("EnemyTurns");
            player = CreateUnit("Player", Faction.Player);
            Assert.That(grid.TryPlaceUnit(player, new Vector2Int(15, 2)), Is.True);
            Assert.That(turns.RegisterUnit(player), Is.True);
            turns.SetFactionOrder(new[] { Faction.Player, Faction.Enemy });
        }

        [Test]
        public void MultipleEnemiesRunInRegistrationOrderAndEndExactlyOnce()
        {
            Unit first = AddEnemy("First", new Vector2Int(1, 1));
            Unit second = AddEnemy("Second", new Vector2Int(1, 3));
            var policy = new LoggingNoActionPolicy();
            int enemyEnds = 0;
            turns.FactionTurnEnded += faction => { if (faction == Faction.Enemy) enemyEnds++; };
            controller.Initialize(turns, grid, policy);

            turns.StartBattle();
            Assert.That(policy.Actors, Is.Empty, "玩家回合不得驱动敌人。");
            turns.EndCurrentTurn();

            Assert.That(policy.Actors, Is.EqualTo(new[] { first, second }));
            Assert.That(enemyEnds, Is.EqualTo(1));
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Player));
            Assert.That(turns.RoundNumber, Is.EqualTo(2));
        }

        [Test]
        public void CapableEnemiesNeverOverlapExclusiveAbilities()
        {
            Unit first = AddEnemy("First", new Vector2Int(10, 1));
            Unit second = AddEnemy("Second", new Vector2Int(10, 3));
            var firstVisual = new FakeShootVisual();
            var secondVisual = new FakeShootVisual();
            GrantAbilities(first, null, firstVisual);
            GrantAbilities(second, null, secondVisual);
            controller.Initialize(turns, grid, new SingleShotEachEnemyPolicy(player));

            StartEnemyTurn();
            Assert.That(firstVisual.PlayCount, Is.EqualTo(1));
            Assert.That(secondVisual.PlayCount, Is.Zero);
            Assert.That(first.Abilities.ActiveExclusiveAbility, Is.Not.Null);
            Assert.That(second.Abilities.ActiveExclusiveAbility, Is.Null);

            firstVisual.Complete(0);
            Assert.That(first.Abilities.ActiveExclusiveAbility, Is.Null);
            Assert.That(secondVisual.PlayCount, Is.EqualTo(1));
            Assert.That(second.Abilities.ActiveExclusiveAbility, Is.Not.Null);
            secondVisual.Complete(0);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Player));
        }

        [Test]
        public void EmptyEnemyListEndsTurnAndNonEnemyTurnDoesNothing()
        {
            var policy = new LoggingNoActionPolicy();
            int enemyEnds = 0;
            turns.FactionTurnEnded += faction => { if (faction == Faction.Enemy) enemyEnds++; };
            controller.Initialize(turns, grid, policy);

            turns.StartBattle();
            Assert.That(policy.Actors, Is.Empty);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Player));
            turns.EndCurrentTurn();

            Assert.That(policy.Actors, Is.Empty);
            Assert.That(enemyEnds, Is.EqualTo(1));
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Player));
        }

        [Test]
        public void DeadAndUnregisteredEnemiesAreSkippedFromSnapshot()
        {
            Unit dead = AddEnemy("Dead", new Vector2Int(1, 0));
            Unit removed = AddEnemy("Removed", new Vector2Int(1, 1));
            Unit valid = AddEnemy("Valid", new Vector2Int(1, 2));
            dead.Kill();
            Assert.That(turns.UnregisterUnit(removed), Is.True);
            var policy = new LoggingNoActionPolicy();
            controller.Initialize(turns, grid, policy);

            StartEnemyTurn();

            Assert.That(policy.Actors, Is.EqualTo(new[] { valid }));
        }

        [Test]
        public void AsyncMoveMustFinishBeforeShootAndThenReturnsToPlayer()
        {
            Unit enemy = AddEnemy("Enemy", new Vector2Int(1, 2));
            var moveVisual = new FakeMovementVisual();
            var shootVisual = new FakeShootVisual();
            GrantAbilities(enemy, moveVisual, shootVisual);
            Vector2Int destination = new Vector2Int(3, 2);
            controller.Initialize(turns, grid,
                new MoveThenShootPolicy(grid, destination, player));

            StartEnemyTurn();

            Assert.That(moveVisual.PlayCount, Is.EqualTo(1));
            Assert.That(shootVisual.PlayCount, Is.Zero);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Enemy));
            Assert.That(enemy.Abilities.GetAbility<MoveAbility>().IsActive, Is.True);

            moveVisual.Complete();
            Assert.That(shootVisual.PlayCount, Is.EqualTo(1));
            Assert.That(enemy.Abilities.GetAbility<ShootAbility>().IsActive, Is.True);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Enemy));

            shootVisual.Complete(0);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Player));
            Assert.That(enemy.Attributes.GetValue(AttributeType.ActionPoints), Is.Zero);
        }

        [Test]
        public void MoveCompletionReevaluatesAndSelectsAnotherLivingTarget()
        {
            Unit secondPlayer = CreateUnit("SecondPlayer", Faction.Player);
            Assert.That(grid.TryPlaceUnit(secondPlayer, new Vector2Int(15, 3)), Is.True);
            turns.RegisterUnit(secondPlayer);
            Unit enemy = AddEnemy("Enemy", new Vector2Int(1, 2));
            var moveVisual = new FakeMovementVisual();
            var shootVisual = new FakeShootVisual();
            GrantAbilities(enemy, moveVisual, shootVisual);
            controller.Initialize(turns, grid, new MoveThenFirstLivingTargetPolicy(
                grid, new Vector2Int(3, 2)));

            StartEnemyTurn();
            player.Kill();
            moveVisual.Complete();

            Assert.That(shootVisual.Targets, Is.EqualTo(new[] { secondPlayer }));
            shootVisual.Complete(0);
        }

        [Test]
        public void RemainingActionPointsAllowTwoAsyncShots()
        {
            Unit enemy = AddEnemy("Enemy", new Vector2Int(10, 2));
            var shootVisual = new FakeShootVisual();
            GrantAbilities(enemy, null, shootVisual);
            controller.Initialize(turns, grid, new CoverFirstEnemyDecisionPolicy());

            StartEnemyTurn();
            Assert.That(shootVisual.PlayCount, Is.EqualTo(1));
            Assert.That(enemy.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1f));

            shootVisual.Complete(0);
            Assert.That(shootVisual.PlayCount, Is.EqualTo(2));
            Assert.That(enemy.Attributes.GetValue(AttributeType.ActionPoints), Is.Zero);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Enemy));

            shootVisual.Complete(1);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Player));
        }

        [Test]
        public void SynchronousAbilityCompletionIsObservedWithoutLosingEvents()
        {
            Unit enemy = AddEnemy("Enemy", new Vector2Int(10, 2));
            GrantAbilities(enemy, null, null);
            int activations = 0;
            enemy.Abilities.AbilityActivated += (ability, _) =>
            {
                if (ability is ShootAbility) activations++;
            };
            controller.Initialize(turns, grid, new CoverFirstEnemyDecisionPolicy());

            Assert.DoesNotThrow(StartEnemyTurn);

            Assert.That(activations, Is.EqualTo(2));
            Assert.That(enemy.Attributes.GetValue(AttributeType.ActionPoints), Is.Zero);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Player));
        }

        [Test]
        public void FailedPlanIsExcludedAndCannotRetryForever()
        {
            Unit enemy = AddEnemy("Enemy", new Vector2Int(1, 2));
            GrantAbilities(enemy, null, null);
            Vector2Int blocked = new Vector2Int(2, 2);
            grid.TrySetBlockState(blocked, BlockState.FullBlocked);
            var policy = new AlwaysMovePolicy(blocked);
            controller.Initialize(turns, grid, policy);

            Assert.DoesNotThrow(StartEnemyTurn);

            Assert.That(policy.DecideCount, Is.EqualTo(2));
            Assert.That(enemy.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2f));
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Player));
        }

        [Test]
        public void DeathCancelsWaitingAbilityAndAdvancesToNextEnemy()
        {
            Unit first = AddEnemy("First", new Vector2Int(10, 1));
            Unit second = AddEnemy("Second", new Vector2Int(10, 3));
            var visual = new FakeShootVisual();
            GrantAbilities(first, null, visual);
            var policy = new FirstShootsSecondWaitsPolicy(first, player);
            controller.Initialize(turns, grid, policy);

            StartEnemyTurn();
            Assert.That(first.Abilities.GetAbility<ShootAbility>().IsActive, Is.True);
            first.Kill();

            Assert.That(policy.SecondObserved, Is.True);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Player));
            visual.InvokeRawCompletion(0);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Player));
        }

        [Test]
        public void OldVisualCallbackCannotCompleteAbilityFromANewTurn()
        {
            Unit enemy = AddEnemy("Enemy", new Vector2Int(10, 2));
            var visual = new FakeShootVisual();
            GrantAbilities(enemy, null, visual);
            controller.Initialize(turns, grid, new OneShotPerTurnPolicy(player));
            int enemyEnds = 0;
            turns.FactionTurnEnded += faction => { if (faction == Faction.Enemy) enemyEnds++; };

            StartEnemyTurn();
            Assert.That(visual.PlayCount, Is.EqualTo(1));
            turns.EndCurrentTurn();
            Assert.That(enemyEnds, Is.EqualTo(1));
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Player));

            turns.EndCurrentTurn();
            Assert.That(visual.PlayCount, Is.EqualTo(2));
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Enemy));
            visual.InvokeRawCompletion(0);
            Assert.That(enemy.Abilities.GetAbility<ShootAbility>().IsActive, Is.True);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Enemy));
            Assert.That(enemyEnds, Is.EqualTo(1));

            visual.Complete(1);
            Assert.That(enemyEnds, Is.EqualTo(2));
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Player));
        }

        [Test]
        public void ShutdownWhileWaitingUnsubscribesBeforeCancellingAndNeverRotates()
        {
            Unit enemy = AddEnemy("Enemy", new Vector2Int(10, 2));
            var visual = new FakeShootVisual();
            GrantAbilities(enemy, null, visual);
            controller.Initialize(turns, grid, new OneShotPerTurnPolicy(player));
            int enemyEnds = 0;
            turns.FactionTurnEnded += faction => { if (faction == Faction.Enemy) enemyEnds++; };
            StartEnemyTurn();

            controller.Shutdown();

            Assert.That(controller.IsInitialized, Is.False);
            Assert.That(enemy.Abilities.GetAbility<ShootAbility>().IsActive, Is.False);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Enemy));
            Assert.That(enemyEnds, Is.Zero);
            visual.InvokeRawCompletion(0);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Enemy));
            Assert.That(enemyEnds, Is.Zero);
        }

        [Test]
        public void DisablingControllerWhileWaitingStopsFurtherActions()
        {
            Unit enemy = AddEnemy("Enemy", new Vector2Int(10, 2));
            var visual = new FakeShootVisual();
            GrantAbilities(enemy, null, visual);
            controller.Initialize(turns, grid, new OneShotPerTurnPolicy(player));
            StartEnemyTurn();

            controller.HandleDisabled();

            Assert.That(controller.IsInitialized, Is.False);
            Assert.That(enemy.Abilities.GetAbility<ShootAbility>().IsActive, Is.False);
            visual.InvokeRawCompletion(0);
            Assert.That(turns.CurrentFaction, Is.EqualTo(Faction.Enemy));
        }

        [Test]
        public void BattleEndWhileWaitingCancelsAndLateCallbackDoesNothing()
        {
            Unit enemy = AddEnemy("Enemy", new Vector2Int(10, 2));
            var visual = new FakeShootVisual();
            GrantAbilities(enemy, null, visual);
            controller.Initialize(turns, grid, new OneShotPerTurnPolicy(player));
            StartEnemyTurn();

            turns.EndBattle();

            Assert.That(turns.State, Is.EqualTo(BattleState.Ended));
            Assert.That(enemy.Abilities.GetAbility<ShootAbility>().IsActive, Is.False);
            visual.InvokeRawCompletion(0);
            Assert.That(turns.State, Is.EqualTo(BattleState.Ended));
        }

        private Unit AddEnemy(string name, Vector2Int coordinates)
        {
            Unit enemy = CreateUnit(name, Faction.Enemy);
            Assert.That(grid.TryPlaceUnit(enemy, coordinates), Is.True);
            Assert.That(turns.RegisterUnit(enemy), Is.True);
            return enemy;
        }

        private void GrantAbilities(Unit enemy, IUnitMovementVisual moveVisual,
            IUnitShootVisual shootVisual)
        {
            enemy.Abilities.Grant(new MoveAbility(grid, moveVisual));
            enemy.Abilities.Grant(new ShootAbility(grid, new ShootSettings(), shootVisual,
                new FixedRandomSource()));
        }

        private void StartEnemyTurn()
        {
            turns.StartBattle();
            turns.EndCurrentTurn();
        }

        private sealed class LoggingNoActionPolicy : IEnemyDecisionPolicy
        {
            public readonly List<Unit> Actors = new List<Unit>();
            public EnemyActionPlan Decide(EnemyDecisionContext context)
            {
                Actors.Add(context.Actor);
                return EnemyActionPlan.NoAction(context.Actor);
            }
        }

        private sealed class MoveThenShootPolicy : IEnemyDecisionPolicy
        {
            private readonly GridManager grid;
            private readonly Vector2Int destination;
            private readonly Unit target;
            public MoveThenShootPolicy(GridManager grid, Vector2Int destination, Unit target) =>
                (this.grid, this.destination, this.target) = (grid, destination, target);
            public EnemyActionPlan Decide(EnemyDecisionContext context)
            {
                grid.TryGetUnitCoordinates(context.Actor, out Vector2Int current);
                if (current != destination) return EnemyActionPlan.Move(context.Actor, destination);
                return context.Actor.Attributes.GetValue(AttributeType.ActionPoints) > 0
                    ? EnemyActionPlan.Shoot(context.Actor, target)
                    : EnemyActionPlan.NoAction(context.Actor);
            }
        }

        private sealed class MoveThenFirstLivingTargetPolicy : IEnemyDecisionPolicy
        {
            private readonly GridManager grid;
            private readonly Vector2Int destination;
            public MoveThenFirstLivingTargetPolicy(GridManager grid, Vector2Int destination) =>
                (this.grid, this.destination) = (grid, destination);
            public EnemyActionPlan Decide(EnemyDecisionContext context)
            {
                grid.TryGetUnitCoordinates(context.Actor, out Vector2Int current);
                if (current != destination) return EnemyActionPlan.Move(context.Actor, destination);
                for (int i = 0; i < context.Players.Count; i++)
                    if (context.Players[i] != null && context.Players[i].IsAlive)
                        return EnemyActionPlan.Shoot(context.Actor, context.Players[i]);
                return EnemyActionPlan.NoAction(context.Actor);
            }
        }

        private sealed class AlwaysMovePolicy : IEnemyDecisionPolicy
        {
            private readonly Vector2Int destination;
            public int DecideCount;
            public AlwaysMovePolicy(Vector2Int destination) => this.destination = destination;
            public EnemyActionPlan Decide(EnemyDecisionContext context)
            {
                DecideCount++;
                return EnemyActionPlan.Move(context.Actor, destination);
            }
        }

        private sealed class FirstShootsSecondWaitsPolicy : IEnemyDecisionPolicy
        {
            private readonly Unit first;
            private readonly Unit target;
            public bool SecondObserved;
            public FirstShootsSecondWaitsPolicy(Unit first, Unit target) =>
                (this.first, this.target) = (first, target);
            public EnemyActionPlan Decide(EnemyDecisionContext context)
            {
                if (context.Actor == first) return EnemyActionPlan.Shoot(first, target);
                SecondObserved = true;
                return EnemyActionPlan.NoAction(context.Actor);
            }
        }

        private sealed class OneShotPerTurnPolicy : IEnemyDecisionPolicy
        {
            private readonly Unit target;
            public OneShotPerTurnPolicy(Unit target) => this.target = target;
            public EnemyActionPlan Decide(EnemyDecisionContext context) =>
                context.Actor.Attributes.GetValue(AttributeType.ActionPoints) >= 2f
                    ? EnemyActionPlan.Shoot(context.Actor, target)
                    : EnemyActionPlan.NoAction(context.Actor);
        }

        private sealed class SingleShotEachEnemyPolicy : IEnemyDecisionPolicy
        {
            private readonly Unit target;
            private readonly HashSet<Unit> acted = new HashSet<Unit>();
            public SingleShotEachEnemyPolicy(Unit target) => this.target = target;
            public EnemyActionPlan Decide(EnemyDecisionContext context) => acted.Add(context.Actor)
                ? EnemyActionPlan.Shoot(context.Actor, target)
                : EnemyActionPlan.NoAction(context.Actor);
        }

        private sealed class FixedRandomSource : IShootRandomSource
        {
            public float NextValue() => 0.99f;
        }

        private sealed class FakeMovementVisual : IUnitMovementVisual
        {
            private readonly List<Action<bool>> callbacks = new List<Action<bool>>();
            public bool IsConfigured => true;
            public bool IsMoving { get; private set; }
            public int PlayCount => callbacks.Count;
            public void PlayMovement(GridManager _, Vector2Int __, Vector2Int ___,
                Action<bool> onCompleted)
            {
                callbacks.Add(onCompleted);
                IsMoving = true;
            }
            public void StopMovement(bool notify = true)
            {
                IsMoving = false;
                if (notify && callbacks.Count > 0) callbacks[^1]?.Invoke(true);
            }
            public void Complete(int index = 0)
            {
                IsMoving = false;
                callbacks[index]?.Invoke(false);
            }
        }

        private sealed class FakeShootVisual : IUnitShootVisual
        {
            private readonly List<Action> arrivals = new List<Action>();
            private readonly List<Action<bool>> completions = new List<Action<bool>>();
            public readonly List<Unit> Targets = new List<Unit>();
            public bool IsConfigured => true;
            public bool IsPlaying { get; private set; }
            public int PlayCount => completions.Count;
            public void SetStartGate(Func<bool> canStart) { }
            public void PlayShot(GridManager _, Unit target, Vector2Int __, Vector2Int ___,
                Action onProjectileArrived, Action<bool> onCompleted)
            {
                Targets.Add(target);
                arrivals.Add(onProjectileArrived);
                completions.Add(onCompleted);
                IsPlaying = true;
            }
            public void StopShot(bool notify = true)
            {
                IsPlaying = false;
                if (notify && completions.Count > 0) completions[^1]?.Invoke(true);
            }
            public void Complete(int index)
            {
                IsPlaying = false;
                completions[index]?.Invoke(false);
            }
            public void InvokeRawCompletion(int index) => completions[index]?.Invoke(false);
        }
    }
}
