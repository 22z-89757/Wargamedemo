using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Cinemachine;
using Wargame.Battle;
using Wargame.GridSystem;
using Wargame.Units;
using Object = UnityEngine.Object;

namespace Wargame.Tests
{
    public sealed class BattleVisualPlayModeTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<Material> createdMaterials = new List<Material>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in created)
                if (go != null) Object.DestroyImmediate(go);
            created.Clear();
            foreach (Material material in createdMaterials)
                if (material != null) Object.DestroyImmediate(material);
            createdMaterials.Clear();
        }

        [UnityTest]
        public IEnumerator MovementVisualCompletesOnceAndAlignsCommittedDestination()
        {
            GridManager grid = CreateGrid();
            UnitMovementVisual visual = CreateMovementVisual(out Transform model);
            visual.PathProvider = new FixedPathProvider(new[]
            {
                grid.GridToWorld(Vector2Int.zero),
                grid.GridToWorld(Vector2Int.right) + Vector3.forward * 0.25f,
                grid.GridToWorld(Vector2Int.right)
            });
            int callbackCount = 0;
            bool? cancelled = null;

            visual.PlayMovement(grid, Vector2Int.zero, Vector2Int.right, value =>
            { callbackCount++; cancelled = value; });
            yield return new WaitUntil(() => !visual.IsMoving);

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(cancelled, Is.False);
            Assert.That(Vector3.Distance(visual.transform.position, grid.GridToWorld(Vector2Int.right)), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(model.localPosition, Vector3.zero), Is.LessThan(0.001f));
        }

        [Test]
        public void MovementPreviewDrawsOneWorldSpaceSegmentAndClearRemovesIt()
        {
            GridManager grid = CreateGrid();
            var previewObject = new GameObject("MovementPreview");
            created.Add(previewObject);
            var preview = previewObject.AddComponent<BattleMovementPreview>();
            var material = new Material(Shader.Find("Sprites/Default"));
            createdMaterials.Add(material);
            preview.Configure(material);
            preview.Initialize(grid);

            Vector3 start = new Vector3(.25f, 0f, .5f);
            Vector3 destination = grid.GridToWorld(new Vector2Int(3, 2));
            preview.ShowPath(start, destination, 2);

            LineRenderer line = previewObject.transform.Find("MovePath").GetComponent<LineRenderer>();
            Assert.That(line.positionCount, Is.EqualTo(2));
            Assert.That(line.GetPosition(0), Is.EqualTo(start + Vector3.up * .085f));
            Assert.That(line.GetPosition(1), Is.EqualTo(destination + Vector3.up * .085f));
            Assert.That(Vector3.Distance(line.GetPosition(0), line.GetPosition(1)),
                Is.EqualTo(Vector3.Distance(start, destination)).Within(.0001f));

            preview.ClearPath();
            Assert.That(line.positionCount, Is.Zero);
        }

        [Test]
        public void StraightPreviewDoesNotChangeGridPathCostOrOccupancy()
        {
            GridManager grid = CreateGrid();
            var unitObject = new GameObject("Mover");
            created.Add(unitObject);
            Unit unit = unitObject.AddComponent<Unit>();
            Vector2Int start = Vector2Int.zero;
            Vector2Int destination = new Vector2Int(3, 2);
            unit.transform.position = grid.GridToWorld(start);
            Assert.That(grid.TryPlaceUnit(unit, start), Is.True);
            var move = new MoveAbility(grid);
            unit.Abilities.Grant(move);
            unit.BeginTurn();

            Assert.That(move.TryGetMovePlan(destination, out MovePlan plan, out _), Is.True);
            Assert.That(plan.Path.Count, Is.GreaterThan(2));
            Assert.That(plan.ActionPointCost, Is.EqualTo(1));
            Assert.That(grid.TryGetUnitCoordinates(unit, out Vector2Int occupied), Is.True);
            Assert.That(occupied, Is.EqualTo(start));
            Assert.That(unit.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));

            var previewObject = new GameObject("MovementPreview");
            created.Add(previewObject);
            var preview = previewObject.AddComponent<BattleMovementPreview>();
            var material = new Material(Shader.Find("Sprites/Default"));
            createdMaterials.Add(material);
            preview.Configure(material);
            preview.Initialize(grid);
            preview.ShowPath(unit.transform.position, grid.GridToWorld(plan.Destination), plan.ActionPointCost);

            LineRenderer line = previewObject.transform.Find("MovePath").GetComponent<LineRenderer>();
            Assert.That(line.positionCount, Is.EqualTo(2));
            Assert.That(Vector3.Distance(line.GetPosition(0), line.GetPosition(1)),
                Is.EqualTo(Vector3.Distance(unit.transform.position, grid.GridToWorld(destination))).Within(.0001f));
            Assert.That(move.TryGetMovePlan(destination, out MovePlan unchanged, out _), Is.True);
            Assert.That(unchanged.Path, Is.EqualTo(plan.Path));
            Assert.That(unchanged.ActionPointCost, Is.EqualTo(plan.ActionPointCost));
        }

        [Test]
        public void CameraDirectorFocusesByDirectionAndRestoresAfterShot()
        {
            var tacticalObject = new GameObject("TacticalCamera");
            created.Add(tacticalObject);
            CinemachineCamera tactical = tacticalObject.AddComponent<CinemachineCamera>();
            Unit player = new GameObject("Player").AddComponent<Unit>();
            Unit enemy = new GameObject("Enemy").AddComponent<Unit>();
            created.Add(player.gameObject);
            created.Add(enemy.gameObject);
            enemy.Faction = Faction.Enemy;
            var focusCameras = new CinemachineCamera[4];
            for (int i = 0; i < focusCameras.Length; i++)
            {
                var cameraObject = new GameObject($"EnemyCamera{i}");
                created.Add(cameraObject);
                focusCameras[i] = cameraObject.AddComponent<CinemachineCamera>();
                focusCameras[i].Follow = enemy.transform;
                focusCameras[i].LookAt = enemy.transform;
            }
            enemy.gameObject.AddComponent<UnitBattleCameraSet>().Configure(focusCameras, null);
            var shotObject = new GameObject("ShotCamera");
            created.Add(shotObject);
            CinemachineCamera shotCamera = shotObject.AddComponent<CinemachineCamera>();
            shotCamera.Follow = player.transform;
            player.gameObject.AddComponent<UnitBattleCameraSet>().Configure(null, shotCamera);

            int direction = 0;
            var director = new BattleCameraDirector(tactical, new[] { player, enemy }, () => direction);
            director.FocusTarget(enemy);
            Assert.That(focusCameras[0].Priority.Value, Is.EqualTo(20));
            Assert.That(tactical.Priority.Value, Is.EqualTo(10));

            direction = 2;
            director.RefreshDirection();
            Assert.That(focusCameras[0].Priority.Value, Is.Zero);
            Assert.That(focusCameras[2].Priority.Value, Is.EqualTo(20));

            director.BeginShot(player, enemy);
            Assert.That(shotCamera.Priority.Value, Is.EqualTo(30));
            Assert.That(shotCamera.LookAt, Is.EqualTo(enemy.transform));
            Assert.That(shotCamera.Follow, Is.EqualTo(player.transform));
            player.transform.position = new Vector3(2f, 0f, 3f);
            Assert.That(shotCamera.Follow.position, Is.EqualTo(player.transform.position));
            director.EndShot();
            Assert.That(shotCamera.Priority.Value, Is.Zero);
            Assert.That(focusCameras[2].Priority.Value, Is.EqualTo(20));
            director.FocusTarget(null);
            Assert.That(tactical.Priority.Value, Is.EqualTo(10));
            director.Dispose();
        }

        [UnityTest]
        public IEnumerator LethalShotRetainsTargetSnapshotAfterTargetDestruction()
        {
            GridManager grid = CreateGrid();
            UnitShootVisualController visual = CreateShootVisual(out Unit target, out _);
            Unit attacker = visual.GetComponent<Unit>();
            attacker.Faction = Faction.Player;
            target.Faction = Faction.Enemy;
            attacker.BeginTurn();
            Vector2Int attackerCell = Vector2Int.one;
            Vector2Int targetCell = new Vector2Int(5, 1);
            Assert.That(grid.TryPlaceUnit(attacker, attackerCell), Is.True);
            Assert.That(grid.TryPlaceUnit(target, targetCell), Is.True);
            attacker.transform.position = grid.GridToWorld(attackerCell);
            target.transform.position = grid.GridToWorld(targetCell);
            target.Attributes.SetBaseValue(AttributeType.Health, 1f);
            Vector3 targetPosition = target.transform.position;
            target.Died += dead => Object.Destroy(dead.gameObject);
            var ability = new ShootAbility(grid, new ShootSettings(), visual, new AlwaysHitRandom());
            attacker.Abilities.Grant(ability);

            Assert.That(attacker.Abilities.TryActivate(ability, AbilityTarget.ForUnit(target), out _), Is.True);
            yield return new WaitUntil(() => !ability.IsActive);
            yield return null;

            Assert.That(target == null, Is.True);
            Assert.That(ability.LastResult.TargetName, Is.EqualTo("Target"));
            Assert.That(ability.LastResult.TargetPosition, Is.EqualTo(targetPosition));
            Assert.That(ability.LastResult.Hit, Is.True);
        }

        [UnityTest]
        public IEnumerator ShotResultPopupRemainsVisibleForTwoRealtimeSeconds()
        {
            var cameraObject = new GameObject("PopupCamera");
            created.Add(cameraObject);
            Camera camera = cameraObject.AddComponent<Camera>();
            bool completed = false;
            BattleShotResultPopup popup = BattleShotResultPopup.Create("Enemy", "Enemy：命中，伤害 4",
                Vector3.zero, camera, () => completed = true);
            Assert.That(popup.Message, Does.Contain("命中").And.Contain("4"));
            TextMesh popupText = popup.GetComponent<TextMesh>();
            Assert.That(popupText.fontSize, Is.EqualTo(64));
            Assert.That(popupText.characterSize, Is.EqualTo(.04f));
            Assert.That(popupText.fontStyle, Is.EqualTo(FontStyle.Bold));
            Assert.That(popupText.GetComponent<MeshRenderer>().bounds.size.y, Is.GreaterThan(.2f),
                "跳字在每格 1 世界单位的战场上必须有可读的世界高度。");

            yield return new WaitForSecondsRealtime(1.8f);
            Assert.That(completed, Is.False);
            Assert.That(popup != null, Is.True);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(completed, Is.True);
        }

        [UnityTest]
        public IEnumerator MovementVisualCancellationSnapsAndNotifiesOnce()
        {
            GridManager grid = CreateGrid();
            UnitMovementVisual visual = CreateMovementVisual(out _);
            visual.PathProvider = new FixedPathProvider(new[]
            { grid.GridToWorld(Vector2Int.zero), grid.GridToWorld(new Vector2Int(4, 0)) });
            int callbackCount = 0;
            bool? cancelled = null;
            visual.PlayMovement(grid, Vector2Int.zero, new Vector2Int(4, 0), value =>
            { callbackCount++; cancelled = value; });
            yield return null;

            visual.StopMovement();
            visual.StopMovement();

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(cancelled, Is.True);
            Assert.That(Vector3.Distance(visual.transform.position, grid.GridToWorld(new Vector2Int(4, 0))),
                Is.LessThan(0.001f));
        }

        [UnityTest]
        public IEnumerator MovementVisualNavigationFailureSnapsAndCompletesOnce()
        {
            GridManager grid = CreateGrid();
            UnitMovementVisual visual = CreateMovementVisual(out Transform model);
            model.localPosition = new Vector3(0f, 0.4f, 0f);
            visual.PathProvider = new FailingPathProvider();
            int callbackCount = 0;
            bool? cancelled = null;

            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("移动视觉导航失败"));
            visual.PlayMovement(grid, Vector2Int.zero, Vector2Int.right, value =>
            { callbackCount++; cancelled = value; });
            yield return null;

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(cancelled, Is.False);
            Assert.That(Vector3.Distance(visual.transform.position, grid.GridToWorld(Vector2Int.right)), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(model.localPosition, new Vector3(0f, 0.4f, 0f)), Is.LessThan(0.001f));
        }

        [UnityTest]
        public IEnumerator PeekShotRunsArrivalThenReturnAndLeavesNoProjectile()
        {
            GridManager grid = CreateGrid();
            UnitShootVisualController visual = CreateShootVisual(out Unit target, out GameObject projectilePrefab);
            var order = new List<string>();
            Vector3 rest = visual.transform.position;
            visual.PlayShot(grid, target, Vector2Int.zero, Vector2Int.right,
                () => order.Add("arrived"), cancelled => order.Add(cancelled ? "cancelled" : "completed"));

            yield return new WaitUntil(() => !visual.IsPlaying);

            Assert.That(order, Is.EqualTo(new[] { "arrived", "completed" }));
            Assert.That(Vector3.Distance(visual.transform.position, rest), Is.LessThan(0.001f));
            Assert.That(CountProjectileClones(projectilePrefab), Is.Zero);
        }

        [UnityTest]
        public IEnumerator ShotVisualWaitsForCameraGateBeforePeekingOrSpawningProjectile()
        {
            GridManager grid = CreateGrid();
            UnitShootVisualController visual = CreateShootVisual(out Unit target, out GameObject projectilePrefab);
            Vector3 rest = visual.transform.position;
            bool cameraReady = false;
            visual.SetStartGate(() => cameraReady);
            visual.PlayShot(grid, target, Vector2Int.zero, Vector2Int.right,
                () => { }, _ => { });

            yield return null;
            Assert.That(visual.IsPlaying, Is.True);
            Assert.That(visual.IsWaitingToStart, Is.True);
            Assert.That(visual.transform.position, Is.EqualTo(rest));
            Assert.That(CountProjectileClones(projectilePrefab), Is.Zero);

            cameraReady = true;
            yield return new WaitUntil(() => !visual.IsPlaying);
            Assert.That(Vector3.Distance(visual.transform.position, rest), Is.LessThan(.001f));
            Assert.That(CountProjectileClones(projectilePrefab), Is.Zero);
        }

        [UnityTest]
        public IEnumerator ShotCancellationAndDestroyedTargetCleanUpAndFinishOnce()
        {
            GridManager grid = CreateGrid();
            UnitShootVisualController visual = CreateShootVisual(out Unit target, out GameObject projectilePrefab);
            int arrivalCount = 0, completionCount = 0;
            bool? cancelled = null;
            visual.PlayShot(grid, target, Vector2Int.zero, Vector2Int.right,
                () => arrivalCount++, value => { completionCount++; cancelled = value; });
            yield return null;
            visual.StopShot();
            visual.StopShot();
            Assert.That(arrivalCount, Is.Zero);
            Assert.That(completionCount, Is.EqualTo(1));
            Assert.That(cancelled, Is.True);
            yield return null;
            Assert.That(CountProjectileClones(projectilePrefab), Is.Zero);

            completionCount = 0;
            visual.PlayShot(grid, target, Vector2Int.zero, Vector2Int.zero,
                () => arrivalCount++, _ => completionCount++);
            Object.Destroy(target.gameObject);
            yield return new WaitUntil(() => !visual.IsPlaying);
            yield return null;
            Assert.That(completionCount, Is.EqualTo(1));
            Assert.That(CountProjectileClones(projectilePrefab), Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator EnemyAbilityUsesRealVisualForOrdinaryAndPeekShotsWithoutChangingOccupancy()
        {
            yield return RunEnemyShot(false);
            yield return RunEnemyShot(true);
        }

        private IEnumerator RunEnemyShot(bool peek)
        {
            GridManager grid = CreateGrid();
            UnitShootVisualController visual = CreateShootVisual(out Unit target, out _);
            Unit attacker = visual.GetComponent<Unit>();
            attacker.Faction = Faction.Enemy;
            target.Faction = Faction.Player;
            attacker.BeginTurn();
            Vector2Int attackerCell = new Vector2Int(1, 1);
            Vector2Int targetCell = new Vector2Int(5, 1);
            Assert.That(grid.TryPlaceUnit(attacker, attackerCell), Is.True);
            Assert.That(grid.TryPlaceUnit(target, targetCell), Is.True);
            attacker.transform.position = grid.GridToWorld(attackerCell);
            target.transform.position = grid.GridToWorld(targetCell);
            if (peek) grid.TrySetBlockState(new Vector2Int(2, 1), BlockState.FullBlocked);
            var ability = new ShootAbility(grid, new ShootSettings(), visual, new AlwaysHitRandom());
            attacker.Abilities.Grant(ability);

            Assert.That(attacker.Abilities.TryActivate(ability, AbilityTarget.ForUnit(target), out _), Is.True);
            Assert.That(ability.IsActive, Is.True);
            Assert.That(grid.TryGetUnitCoordinates(attacker, out Vector2Int occupied), Is.True);
            Assert.That(occupied, Is.EqualTo(attackerCell));
            yield return new WaitUntil(() => !ability.IsActive);
            Assert.That(grid.TryGetUnitCoordinates(attacker, out occupied), Is.True);
            Assert.That(occupied, Is.EqualTo(attackerCell));
            Assert.That(Vector3.Distance(attacker.transform.position, grid.GridToWorld(attackerCell)), Is.LessThan(0.001f));
        }

        private GridManager CreateGrid()
        {
            var go = new GameObject("Grid");
            created.Add(go);
            var grid = go.AddComponent<GridManager>();
            if (!grid.IsInitialized) grid.Initialize(8, 4);
            return grid;
        }

        private UnitMovementVisual CreateMovementVisual(out Transform model)
        {
            var root = new GameObject("Mover");
            created.Add(root);
            root.AddComponent<Unit>();
            var modelObject = new GameObject("Model");
            modelObject.transform.SetParent(root.transform, false);
            model = modelObject.transform;
            var visual = root.AddComponent<UnitMovementVisual>();
            visual.Configure(model, null);
            return visual;
        }

        private UnitShootVisualController CreateShootVisual(out Unit target, out GameObject projectilePrefab)
        {
            var attackerObject = new GameObject("Attacker");
            created.Add(attackerObject);
            attackerObject.AddComponent<Unit>();
            var modelObject = new GameObject("Model");
            modelObject.transform.SetParent(attackerObject.transform, false);
            var muzzleObject = new GameObject("Muzzle");
            muzzleObject.transform.SetParent(modelObject.transform, false);
            muzzleObject.transform.localPosition = new Vector3(0f, 0.5f, 0.25f);
            projectilePrefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            created.Add(projectilePrefab);
            projectilePrefab.name = "TestBullet";
            Object.DestroyImmediate(projectilePrefab.GetComponent<Collider>());
            target = new GameObject("Target").AddComponent<Unit>();
            created.Add(target.gameObject);
            target.transform.position = new Vector3(3f, 0f, 0f);
            var visual = attackerObject.AddComponent<UnitShootVisualController>();
            visual.Configure(modelObject.transform, muzzleObject.transform, projectilePrefab);
            return visual;
        }

        private static int CountProjectileClones(GameObject prefab)
        {
            int count = 0;
            foreach (Transform item in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (item.gameObject != prefab && item.name == prefab.name) count++;
            return count;
        }

        private sealed class FixedPathProvider : INavigationPathProvider
        {
            private readonly IReadOnlyList<Vector3> corners;
            public FixedPathProvider(IReadOnlyList<Vector3> corners) => this.corners = corners;
            public bool TryCalculate(Vector3 start, Vector3 destination, float sampleRadius,
                out IReadOnlyList<Vector3> result, out string error)
            { result = corners; error = null; return true; }
        }

        private sealed class FailingPathProvider : INavigationPathProvider
        {
            public bool TryCalculate(Vector3 start, Vector3 destination, float sampleRadius,
                out IReadOnlyList<Vector3> corners, out string error)
            { corners = Array.Empty<Vector3>(); error = "测试失败"; return false; }
        }

        private sealed class AlwaysHitRandom : IShootRandomSource
        {
            private int index;
            public float NextValue() => index++ == 0 ? 0f : 0.99f;
        }
    }
}
