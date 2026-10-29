using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Wargame.Battle;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class BattleFlowPlayModeTests
    {
        private Mouse mouse;
        private Touchscreen touch;
        private BattleSceneManager battle;
        private BattlePlayerController controller;
        private Unit player;
        private Camera view;

        private Vector2 mousePosition;
        private bool mouseDown;

        [SetUp]
        public void Setup()
        {
            mouse = InputSystem.AddDevice<Mouse>();
            touch = InputSystem.AddDevice<Touchscreen>();
            mousePosition = Vector2.zero;
            mouseDown = false;
        }

        [TearDown]
        public void RemoveDevices()
        {
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(touch);
        }

        private void QueueMouse()
        {
            var state = new MouseState { position = mousePosition };
            if (mouseDown) state = state.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(mouse, state);
            InputSystem.Update();
        }
        private void Set(Vector2Control control, Vector2 value) { mousePosition = value; QueueMouse(); }
        private void Press(ButtonControl control) { mouseDown = true; QueueMouse(); }
        private void Release(ButtonControl control) { mouseDown = false; QueueMouse(); }
        private void QueueTouch(int id, Vector2 position, UnityEngine.InputSystem.TouchPhase phase, Touchscreen screen)
        {
            InputSystem.QueueStateEvent(screen, new TouchState { touchId = id, position = position, phase = phase });
            InputSystem.Update();
        }
        private void BeginTouch(int id, Vector2 position, Touchscreen screen) => QueueTouch(id, position, UnityEngine.InputSystem.TouchPhase.Began, screen);
        private void MoveTouch(int id, Vector2 position, Touchscreen screen) => QueueTouch(id, position, UnityEngine.InputSystem.TouchPhase.Moved, screen);
        private void EndTouch(int id, Vector2 position, Touchscreen screen) => QueueTouch(id, position, UnityEngine.InputSystem.TouchPhase.Ended, screen);
        private void CancelTouch(int id, Vector2 position, Touchscreen screen) => QueueTouch(id, position, UnityEngine.InputSystem.TouchPhase.Canceled, screen);

        [UnitySetUp]
        public IEnumerator LoadBattle()
        {
            yield return SceneManager.LoadSceneAsync("Map1", LoadSceneMode.Single);
            yield return null;
            battle = Object.FindFirstObjectByType<BattleSceneManager>();
            controller = Object.FindFirstObjectByType<BattlePlayerController>();
            Assert.That(battle.IsInitialized, Is.True);
            player = battle.SpawnedUnits.First(u => u.Faction == Faction.Player);
            view = Camera.main;
            Physics.SyncTransforms();
            Canvas.ForceUpdateCanvases();
        }

        [UnityTearDown]
        public IEnumerator CleanupBattle()
        {
            var empty = SceneManager.CreateScene("BattleTestCleanup");
            Scene previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(previous);
            yield return null;
        }

        private Vector2 UnitScreen => view.WorldToScreenPoint(player.transform.position + Vector3.up * .75f);
        private Vector2 CellScreen(Vector2Int cell) => view.WorldToScreenPoint(battle.Grid.GridToWorld(cell));
        private Vector2Int Target => player.Abilities.GetAbility<MoveAbility>().GetReachableCells().ReachableCosts
            .First(item => item.Value == 2).Key;

        private IEnumerator WaitForMovement()
        {
            float deadline = Time.realtimeSinceStartup + 6;
            while (battle.HasActiveAction && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(battle.HasActiveAction, Is.False, "移动表现必须结束并释放能力锁。");
        }

        [UnityTest]
        public IEnumerator SpawnCoordinatesAndManualTurnLoop()
        {
            Assert.That(battle.SpawnedUnits.Count, Is.EqualTo(2));
            Assert.That(battle.TryInitialize(out _), Is.True);
            Assert.That(battle.SpawnedUnits.Count, Is.EqualTo(2));
            Assert.That(battle.Grid.TryGetUnitCoordinates(player, out var coordinate), Is.True);
            Assert.That(coordinate, Is.EqualTo(Vector2Int.one));
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            Assert.That(enemy.Abilities.GetAbility<MoveAbility>(), Is.Null);
            Assert.That(battle.Grid.TryGetUnitCoordinates(enemy, out coordinate), Is.True);
            Assert.That(coordinate, Is.EqualTo(new Vector2Int(18,18)));
            player.Attributes.SetBaseValue(AttributeType.ActionPoints, 0);
            Assert.That(battle.Turns.RoundNumber, Is.EqualTo(1));
            Object.FindFirstObjectByType<BattleHUD>().EndTurnButton.onClick.Invoke();
            Assert.That(battle.Turns.CurrentFaction, Is.EqualTo(Faction.Enemy));
            yield return null;
            yield return null;
            Assert.That(battle.Turns.CurrentFaction, Is.EqualTo(Faction.Player));
            Assert.That(battle.Turns.RoundNumber, Is.EqualTo(2));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator MouseDragPreviewsThenCommitsOnlyOnRelease()
        {
            Vector2Int target = Target;
            Set(mouse.position, UnitScreen); Press(mouse.leftButton);
            yield return null;
            Assert.That(controller.IsDragging, Is.True);
            Set(mouse.position, CellScreen(target));
            yield return null;
            Assert.That(controller.PreviewPlan, Is.Not.Null);
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            battle.Grid.TryGetUnitCoordinates(player, out var before);
            Assert.That(before, Is.EqualTo(Vector2Int.one));
            Release(mouse.leftButton);
            yield return null;
            battle.Grid.TryGetUnitCoordinates(player, out var after);
            Assert.That(after, Is.EqualTo(target));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1));
            Assert.That(battle.TryEndPlayerTurn(), Is.False);
            Assert.That(player.Abilities.TryActivate("Move", AbilityTarget.ForCell(Vector2Int.one), out var reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.AlreadyActive));
            yield return WaitForMovement();
            Assert.That(Vector3.Distance(player.transform.position, battle.Grid.GridToWorld(target)), Is.LessThan(.001f));
            Assert.That(player.Tags.HasTag(GameplayTag.Moving), Is.False);
        }

        [UnityTest]
        public IEnumerator ClickAndUiOrOutsideReleaseDoNotSpendAp()
        {
            // 模型顶部投影到棋盘的邻格，静止单击仍然只能选择。
            Vector2 highPoint = view.WorldToScreenPoint(player.transform.position + Vector3.up * 1.4f);
            Set(mouse.position, highPoint); Press(mouse.leftButton);
            yield return null;
            Assert.That(controller.IsDragging, Is.True);
            Assert.That(controller.PreviewPlan, Is.Null);
            Set(mouse.position, highPoint + Vector2.right * 2);
            Release(mouse.leftButton);
            yield return null;
            Assert.That(controller.SelectedUnit, Is.SameAs(player));
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            var endButton = Object.FindFirstObjectByType<BattleHUD>().EndTurnButton.GetComponent<RectTransform>();
            Vector2 endScreen = RectTransformUtility.WorldToScreenPoint(null, endButton.TransformPoint(endButton.rect.center));
            Set(mouse.position, endScreen); Press(mouse.leftButton);
            yield return null;
            Assert.That(controller.IsDragging, Is.False, "从 UI 按下不能开始棋盘手势。");
            Set(mouse.position, CellScreen(Target)); Release(mouse.leftButton);
            yield return null;
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Set(mouse.position, UnitScreen); Press(mouse.leftButton);
            yield return null;
            Set(mouse.position, endScreen); Release(mouse.leftButton);
            yield return null;
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(battle.Turns.RoundNumber, Is.EqualTo(1), "移动手势松开到结束回合按钮不能触发该按钮。");
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Set(mouse.position, UnitScreen); Press(mouse.leftButton);
            yield return null;
            var cancel = Object.FindFirstObjectByType<BattleHUD>().CancelArea;
            Vector2 cancelScreen = RectTransformUtility.WorldToScreenPoint(null, cancel.TransformPoint(cancel.rect.center));
            Set(mouse.position, cancelScreen);
            yield return null;
            Release(mouse.leftButton);
            yield return null;
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Set(mouse.position, UnitScreen); Press(mouse.leftButton);
            yield return null;
            Set(mouse.position, new Vector2(-100,-100)); Release(mouse.leftButton);
            yield return null;
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            battle.Grid.TryGetUnitCoordinates(player, out var cell);
            Assert.That(cell, Is.EqualTo(Vector2Int.one));
        }

        [UnityTest]
        public IEnumerator TouchDragIgnoresSecondaryFingerAndMoves()
        {
            Vector2Int target = Target;
            BeginTouch(1, UnitScreen, screen: touch);
            yield return null;
            Assert.That(controller.IsDragging, Is.True);
            BeginTouch(2, new Vector2(5,5), screen: touch);
            MoveTouch(1, CellScreen(target), screen: touch);
            yield return null;
            Assert.That(controller.PreviewPlan, Is.Not.Null);
            EndTouch(1, CellScreen(target), screen: touch);
            yield return null;
            Assert.That(controller.IsDragging, Is.False, "捕获手指松开时应立即执行，无需等待另一根手指。");
            battle.Grid.TryGetUnitCoordinates(player, out var after);
            Assert.That(after, Is.EqualTo(target));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1));
            EndTouch(2, new Vector2(5,5), screen: touch);
            yield return WaitForMovement();
        }

        [UnityTest]
        public IEnumerator TouchCancellationAndFocusLossDoNotMove()
        {
            BeginTouch(1, UnitScreen, screen: touch);
            yield return null;
            MoveTouch(1, CellScreen(Target), screen: touch);
            CancelTouch(1, CellScreen(Target), screen: touch);
            yield return null;
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Set(mouse.position, UnitScreen); Press(mouse.leftButton);
            yield return null;
            Object.FindFirstObjectByType<BattleInputReader>().SendMessage("OnApplicationFocus", false);
            Release(mouse.leftButton);
            yield return null;
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator InterruptedMovementSnapsAndDeathClearsOccupancy()
        {
            Vector2Int target = Target;
            var move = player.Abilities.GetAbility<MoveAbility>();
            Assert.That(player.Abilities.TryActivate(move, AbilityTarget.ForCell(target), out _), Is.True);
            yield return null;
            player.GetComponent<UnitVisualController>().enabled = false;
            Assert.That(move.IsActive, Is.False);
            Assert.That(player.Tags.HasTag(GameplayTag.Moving), Is.False);
            Assert.That(player.transform.position, Is.EqualTo(battle.Grid.GridToWorld(target)));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1));
            player.Kill();
            Assert.That(battle.Grid.TryGetUnitCoordinates(player, out _), Is.False);
            Assert.That(battle.Turns.Units.Contains(player), Is.False);
            yield return null;
        }
    }
}
