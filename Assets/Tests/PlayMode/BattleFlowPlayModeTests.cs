using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Unity.Cinemachine;
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
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
#endif

        [SetUp]
        public void Setup()
        {
            // 合成输入不依赖编辑器是否在前台；失焦取消仍由专门的测试显式触发。
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
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
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
#endif
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
            yield return SettleFrame();
            battle = Object.FindFirstObjectByType<BattleSceneManager>();
            controller = Object.FindFirstObjectByType<BattlePlayerController>();
            Assert.That(battle.IsInitialized, Is.True);
            player = battle.SpawnedUnits.First(u => u.Faction == Faction.Player);
            view = Camera.main;
            Physics.SyncTransforms();
            Canvas.ForceUpdateCanvases();
        }

        [UnityTest]
        public IEnumerator LivingPlayersAndEnemiesKeepOneHealthBarThroughTurnsAndAp()
        {
            var bars = battle.HealthBars;
            Assert.That(bars, Is.Not.Null);
            Assert.That(bars.ViewCount, Is.EqualTo(battle.SpawnedUnits.Count));
            Assert.That(bars.TryGetView(player, out var playerBar), Is.True);
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            Assert.That(bars.TryGetView(enemy, out var enemyBar), Is.True);
            Assert.That(playerBar.TotalSegments, Is.EqualTo(10));
            Assert.That(playerBar.FilledSegments, Is.EqualTo(10));
            Assert.That(enemyBar.FilledSegments, Is.EqualTo(10));
            player.Attributes.SetBaseValue(AttributeType.Health, 7.5f);
            yield return SettleFrame();
            Assert.That(player.Attributes.GetValue(AttributeType.Health), Is.EqualTo(7.5f));
            Assert.That(playerBar.FilledSegments, Is.EqualTo(8));
            player.Attributes.SetBaseValue(AttributeType.Health, 7.1f);
            yield return SettleFrame();
            Assert.That(playerBar.FilledSegments, Is.EqualTo(8));
            player.Attributes.SetBaseValue(AttributeType.Health, 7f);
            yield return SettleFrame();
            Assert.That(playerBar.FilledSegments, Is.EqualTo(7));
            player.Attributes.SetBaseValue(AttributeType.ActionPoints, 0);
            Assert.That(battle.TryEndPlayerTurn(), Is.True);
            yield return SettleFrame();
            Assert.That(bars.ViewCount, Is.EqualTo(battle.SpawnedUnits.Count(u => u != null && u.IsAlive)));
            Assert.That(bars.TryGetView(player, out _), Is.True);
            Assert.That(bars.TryGetView(enemy, out _), Is.True);
        }

        [UnityTest]
        public IEnumerator HealthBarFollowsActualTransformAndRemovesOnDeath()
        {
            Assert.That(battle.HealthBars.TryGetView(player, out var bar), Is.True);
            Vector2 before = view.WorldToScreenPoint(player.transform.position + Vector3.up * battle.HealthBars.Settings.WorldOffset);
            player.Attributes.SetBaseValue(AttributeType.Health, .1f);
            yield return SettleFrame();
            Assert.That(bar.FilledSegments, Is.EqualTo(1));
            Vector3 start = player.transform.position;
            player.transform.position += Vector3.right * 2;
            yield return SettleFrame();
            Vector2 after = view.WorldToScreenPoint(player.transform.position + Vector3.up * battle.HealthBars.Settings.WorldOffset);
            Assert.That(Vector2.Distance(before, after), Is.GreaterThan(1));
            player.Attributes.SetBaseValue(AttributeType.Health, 0);
            yield return SettleFrame();
            Assert.That(battle.HealthBars.TryGetView(player, out _), Is.False);
            player.transform.position = start;
        }

        [UnityTest]
        public IEnumerator HealthBarDoesNotBlockPointerInput()
        {
            yield return PlaceCameraAndPlayerInInterior();
            battle.HealthBars.Settings.WorldOffset = .75f;
            yield return SettleFrame();
            Assert.That(battle.HealthBars.HealthCanvas.GetComponent<GraphicRaycaster>(), Is.Null);
            Assert.That(battle.HealthBars.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), Is.True);
            var input = Object.FindFirstObjectByType<BattleInputReader>();
            Vector2 point = view.WorldToScreenPoint(player.transform.position + Vector3.up * battle.HealthBars.Settings.WorldOffset);
            Assert.That(input.IsOverUI(point), Is.False);
            Set(mouse.position, point); Press(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.SelectedUnit, Is.SameAs(player));
            Release(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.HasActiveGesture, Is.False);
            BeginTouch(1, point, touch);
            yield return SettleFrame();
            Assert.That(controller.IsDragging, Is.True);
            EndTouch(1, point, touch);
            yield return SettleFrame();
            Assert.That(controller.HasActiveGesture, Is.False);
            var hud = Object.FindFirstObjectByType<BattleHUD>();
            var button = (RectTransform)hud.EndTurnButton.transform;
            Vector2 buttonPoint = RectTransformUtility.WorldToScreenPoint(null, button.position);
            Assert.That(input.IsOverUI(buttonPoint), Is.True);
        }

        [UnityTest]
        public IEnumerator HealthBarsRebuildOnlyWhenNeededAndUseFinalMaxHealth()
        {
            var bars = battle.HealthBars;
            bars.TryGetView(player, out var original);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(battle.TryInitialize(out _), Is.True);
                bars.Initialize(battle, view);
                bars.Refresh();
            }
            Assert.That(bars.ViewCount, Is.EqualTo(battle.SpawnedUnits.Count(u => u != null && u.IsAlive)));
            bars.TryGetView(player, out var same);
            Assert.That(same, Is.SameAs(original));
            player.Attributes.SetBaseValue(AttributeType.MaxHealth, 7.5f);
            player.Attributes.SetBaseValue(AttributeType.Health, 3.75f);
            yield return SettleFrame();
            Assert.That(original.TotalSegments, Is.EqualTo(8));
            Assert.That(original.FilledSegments, Is.EqualTo(4));
            Assert.That(player.Attributes.GetValue(AttributeType.Health), Is.EqualTo(3.75f));
            bars.enabled = false;
            Assert.That(bars.ViewCount, Is.Zero);
            player.Attributes.SetBaseValue(AttributeType.MaxHealth, 2);
            yield return SettleFrame();
            Assert.That(original == null, Is.True);
            bars.enabled = true;
            yield return SettleFrame();
            Assert.That(bars.TryGetView(player, out var rebuilt), Is.True);
            Assert.That(rebuilt.TotalSegments, Is.EqualTo(2));
            Assert.That(rebuilt.FilledSegments, Is.EqualTo(2));
            Assert.That(bars.ViewCount, Is.EqualTo(battle.SpawnedUnits.Count(u => u != null && u.IsAlive)));
            player.Attributes.SetBaseValue(AttributeType.Health, 1.1f);
            yield return SettleFrame();
            Assert.That(rebuilt.FilledSegments, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator HealthBarsReleaseDestroyedUnitsAndSceneBindings()
        {
            var oldBars = battle.HealthBars;
            oldBars.TryGetView(player, out var oldView);
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            oldBars.TryGetView(enemy, out var enemyView);
            Object.Destroy(enemy.gameObject);
            yield return SettleFrame();
            Assert.That(oldBars.ViewCount, Is.EqualTo(battle.SpawnedUnits.Count(u => u != null && u.IsAlive)));
            Assert.That(enemyView == null, Is.True);
            oldBars.Refresh();
            Assert.That(oldBars.ViewCount, Is.EqualTo(battle.SpawnedUnits.Count(u => u != null && u.IsAlive)));
            yield return SceneManager.LoadSceneAsync("Map1", LoadSceneMode.Single);
            yield return SettleFrame();
            Assert.That(oldBars == null, Is.True);
            Assert.That(oldView == null, Is.True);
            battle = Object.FindFirstObjectByType<BattleSceneManager>();
            controller = Object.FindFirstObjectByType<BattlePlayerController>();
            player = battle.SpawnedUnits.First(u => u.Faction == Faction.Player);
            view = battle.CameraRig.OutputCamera;
            Assert.That(battle.HealthBars.ViewCount, Is.EqualTo(battle.SpawnedUnits.Count(u => u != null && u.IsAlive)));
            player.Attributes.SetBaseValue(AttributeType.Health, 2.5f);
            yield return SettleFrame();
            battle.HealthBars.TryGetView(player, out var bar);
            Assert.That(bar.FilledSegments, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator HealthBarProjectionScaleAndViewportFollowCurrentCamera()
        {
            var bars = battle.HealthBars;
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            bars.TryGetView(enemy, out var bar);
            foreach (float distance in new[] { 5f, 10f, 20f, 30f, 40f })
            {
                Vector3 anchor = view.transform.position + view.transform.forward * distance;
                enemy.transform.position = anchor - Vector3.up * bars.Settings.WorldOffset;
                yield return SettleFrame();
                Assert.That(bar.gameObject.activeSelf, Is.True);
                Assert.That(bar.Rect.localScale.x, Is.EqualTo(bars.Settings.GetScale(distance)).Within(.0001));
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, bar.Rect.position);
                Assert.That(Vector2.Distance(screen, view.WorldToScreenPoint(anchor)), Is.LessThan(.75f));
                Assert.That(bar.Rect.localRotation, Is.EqualTo(Quaternion.identity));
            }
            foreach (Vector3 viewport in new[] { new Vector3(.5f,.5f,-10), new Vector3(1.2f,.5f,10),
                new Vector3(.5f,-.2f,10), new Vector3(.5f,.5f,view.nearClipPlane*.5f),
                new Vector3(.5f,.5f,view.farClipPlane+10) })
            {
                enemy.transform.position = view.ViewportToWorldPoint(viewport) - Vector3.up * bars.Settings.WorldOffset;
                yield return SettleFrame();
                Assert.That(bar.gameObject.activeSelf, Is.False);
            }
            enemy.Attributes.SetBaseValue(AttributeType.Health, 4.2f);
            enemy.transform.position = view.ViewportToWorldPoint(new Vector3(.5f,.5f,20)) - Vector3.up * bars.Settings.WorldOffset;
            yield return SettleFrame();
            Assert.That(bar.gameObject.activeSelf, Is.True);
            Assert.That(bar.FilledSegments, Is.EqualTo(5));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = view.transform.position + view.transform.forward * 10;
            wall.transform.localScale = Vector3.one * 2;
            Physics.SyncTransforms();
            Ray ray = new Ray(view.transform.position, view.transform.forward);
            Assert.That(wall.GetComponent<Collider>().Raycast(ray, out _, 20), Is.True);
            yield return SettleFrame();
            Assert.That(bar.gameObject.activeSelf, Is.True, "墙体不额外筛选血条可见性");
            Object.Destroy(wall);
        }

        [UnityTest]
        public IEnumerator HealthBarTracksMovementAndCameraInTheSameFrame()
        {
            yield return PlaceCameraAndPlayerInInterior();
            battle.HealthBars.TryGetView(player, out var bar);
            var probe = player.gameObject.AddComponent<HealthBarFrameProbe>();
            probe.Configure(player, bar, view, battle.HealthBars.Settings.WorldOffset);
            var target = new Vector2Int(33,20);
            for (int x = 31; x <= 33; x++) battle.Grid.TrySetBlockState(new Vector2Int(x,20), Wargame.GridSystem.BlockState.Unblocked);
            Assert.That(player.Abilities.TryActivate("Move", AbilityTarget.ForCell(target), out var reason), Is.True, reason.ToString());
            Assert.That(battle.Grid.TryGetUnitCoordinates(player, out var logical), Is.True);
            Assert.That(logical, Is.EqualTo(target));
            Assert.That(Vector3.Distance(player.transform.position, battle.Grid.GridToWorld(target)), Is.GreaterThan(1));
            yield return WaitForMovement();
            Assert.That(probe.Samples, Is.GreaterThan(3));
            Assert.That(probe.MaxError, Is.LessThan(.75f));
            Assert.That(battle.CameraRig.TryRotate(1), Is.True);
            yield return new WaitForSecondsRealtime(.4f);
            yield return SettleFrame();
            var rig = battle.CameraRig;
            Vector2 point = new Vector2(Screen.width*.5f,Screen.height*.5f);
            Assert.That(rig.BeginPan(point), Is.True);
            rig.DragPan(point + Vector2.right * 40, false);
            rig.EndPan();
            yield return SettleFrame();
            Assert.That(probe.MaxError, Is.LessThan(.75f));
        }

        [UnityTearDown]
        public IEnumerator CleanupBattle()
        {
            var empty = SceneManager.CreateScene("BattleTestCleanup");
            Scene previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(previous);
            yield return SettleFrame();
        }

        private static IEnumerator SettleFrame()
        {
            yield return null;
            yield return null;
        }

        private IEnumerator ClickUi(UnityEngine.UI.Button button)
        {
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            Vector2 position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            Set(mouse.position, position);
            yield return SettleFrame();
            Press(mouse.leftButton);
            yield return SettleFrame();
            Release(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.HasActiveGesture, Is.False, "UI 点击不得触发棋盘移动或相机手势。");
        }

        private Vector2 UnitScreen => view.WorldToScreenPoint(player.transform.position + Vector3.up * .75f);
        private string PointerDiagnostics()
        {
            var reader = Object.FindFirstObjectByType<BattleInputReader>();
            var captured = typeof(BattleInputReader).GetField("capturedDevice",System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(reader);
            return $"device={captured}, touchEnabled={touch.enabled}, phase={touch.primaryTouch.phase.ReadValue()}, press={touch.primaryTouch.press.ReadValue()}, UI={reader.IsOverUI(UnitScreen)}, panning={controller.IsPanning}, focused={Application.isFocused}";
        }
        private Vector2 CellScreen(Vector2Int cell) => view.WorldToScreenPoint(battle.Grid.GridToWorld(cell));
        private Vector2Int Target => player.Abilities.GetAbility<MoveAbility>().GetReachableCells().ReachableCosts
            .First(item => item.Value == 2).Key;

        private IEnumerator WaitForMovement()
        {
            float deadline = Time.realtimeSinceStartup + 6;
            while (battle.HasActiveAction && Time.realtimeSinceStartup < deadline) yield return SettleFrame();
            Assert.That(battle.HasActiveAction, Is.False, "移动表现必须结束并释放能力锁。");
        }

        [UnityTest]
        public IEnumerator SpawnCoordinatesAndManualTurnLoop()
        {
            Assert.That(battle.SpawnedUnits.Count, Is.EqualTo(battle.SpawnedUnits.Count(u => u != null)));
            Assert.That(battle.TryInitialize(out _), Is.True);
            Assert.That(battle.SpawnedUnits.Count, Is.EqualTo(battle.SpawnedUnits.Count(u => u != null)));
            Assert.That(battle.Grid.TryGetUnitCoordinates(player, out var coordinate), Is.True);
            Assert.That(coordinate, Is.EqualTo(Vector2Int.one));
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            Assert.That(enemy.Abilities.GetAbility<MoveAbility>(), Is.Null);
            Assert.That(player.Abilities.GetAbility<ShootAbility>(), Is.Not.Null);
            Assert.That(enemy.Abilities.GetAbility<ShootAbility>(), Is.Null);
            Assert.That(battle.Grid.TryGetUnitCoordinates(enemy, out coordinate), Is.True);
            Assert.That(coordinate, Is.EqualTo(new Vector2Int(18,18)));
            foreach (Unit unit in battle.SpawnedUnits.Where(u => u != null && u.Faction == Faction.Player))
                unit.Attributes.SetBaseValue(AttributeType.ActionPoints, 0);
            Assert.That(battle.Turns.RoundNumber, Is.EqualTo(1));
            Object.FindFirstObjectByType<BattleHUD>().EndTurnButton.onClick.Invoke();
            Assert.That(battle.Turns.CurrentFaction, Is.EqualTo(Faction.Enemy));
            yield return SettleFrame();
            yield return SettleFrame();
            Assert.That(battle.Turns.CurrentFaction, Is.EqualTo(Faction.Player));
            Assert.That(battle.Turns.RoundNumber, Is.EqualTo(2));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator BattleUiUsesSeparateChineseInfoCanvasAndEndTurnConfirmation()
        {
            var hud = Object.FindFirstObjectByType<BattleHUD>();
            Assert.That(hud.InfoCanvas, Is.Not.Null);
            Assert.That(hud.InfoCanvas, Is.Not.SameAs(hud.InteractionCanvas));
            Assert.That(hud.InfoCanvas.GetComponent<GraphicRaycaster>(), Is.Null);
            Assert.That(hud.InfoCanvas.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget), Is.True);
            Assert.That(hud.RoundText.text, Does.Contain("回合"));
            Assert.That(hud.FactionText.text, Does.Contain("玩家"));
            Assert.That(hud.AbilityButtons.Count, Is.EqualTo(1));
            Assert.That(hud.AbilityStatusText.text, Does.Contain("射击").And.Contain("AP"));
            Assert.That(hud.AbilityStatusText.text, Does.Not.Contain("移动"));
            Assert.That(hud.AbilityButtons.All(button => button.transform.parent == hud.AbilityDic), Is.True);
            Assert.That(hud.AbilityButtons.Select(button => button.GetComponentInChildren<Text>(true).text)
                .All(text => text.Contains("射击")), Is.True);
            float apBeforeAbilityClick = player.Attributes.GetValue(AttributeType.ActionPoints);
            Button shootAbilityButton = hud.AbilityButtons.First(button =>
                button.GetComponentInChildren<Text>(true).text.Contains("射击"));
            shootAbilityButton.onClick.Invoke();
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(apBeforeAbilityClick));

            Faction before = battle.Turns.CurrentFaction;
            hud.EndTurnButton.onClick.Invoke();
            Assert.That(hud.IsEndTurnConfirmationVisible, Is.True);
            Assert.That(battle.Turns.CurrentFaction, Is.EqualTo(before));
            Button cancel = hud.CancelButton;
            cancel.onClick.Invoke();
            Assert.That(hud.IsEndTurnConfirmationVisible, Is.False);
            Assert.That(battle.Turns.CurrentFaction, Is.EqualTo(before));

            hud.EndTurnButton.onClick.Invoke();
            Button confirm = hud.ConfirmButton;
            confirm.onClick.Invoke();
            Assert.That(hud.IsEndTurnConfirmationVisible, Is.False);
            Assert.That(battle.Turns.CurrentFaction, Is.EqualTo(Faction.Enemy));
            yield return SettleFrame();
        }

        [UnityTest]
        public IEnumerator TargetListPreviewsAndFiresOnlyAfterConfirmation()
        {
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            var targetCell = new Vector2Int(4, 1);
            for (int x = 2; x <= targetCell.x; x++)
                battle.Grid.TrySetBlockState(new Vector2Int(x, 1), Wargame.GridSystem.BlockState.Unblocked);
            Assert.That(battle.Grid.TryMoveUnit(enemy, targetCell), Is.True);
            enemy.transform.position = battle.Grid.GridToWorld(targetCell);
            player.Attributes.SetBaseValue(AttributeType.Aim, 100f);
            Physics.SyncTransforms();
            yield return SettleFrame();

            var hud = Object.FindFirstObjectByType<BattleHUD>();
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            yield return RequestShoot(hud);
            Assert.That(hud.VisibleTargetCount, Is.EqualTo(1));
            Assert.That(hud.IsAbilityConfirmationVisible, Is.False);
            yield return ClickUi(hud.TargetButtons[0]);
            Assert.That(controller.SelectedShootTarget, Is.SameAs(enemy));
            Assert.That(hud.HitChanceText.text, Does.Contain("100%"));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2f));
            Assert.That(enemy.Attributes.GetValue(AttributeType.Health), Is.EqualTo(10f));
            Assert.That(hud.IsAbilityConfirmationVisible, Is.True);

            player.Attributes.SetBaseValue(AttributeType.ActionPoints, 0f);
            yield return SettleFrame();
            Assert.That(hud.IsAbilityConfirmationVisible, Is.False);
            player.Attributes.SetBaseValue(AttributeType.ActionPoints, 2f);
            yield return SettleFrame();

            yield return ClickUi(hud.TargetButtons[0]);
            CinemachineBrain brain = battle.CameraRig.Brain;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 2f);
            yield return ClickUi(hud.ConfirmButton);
            Assert.That(hud.CancelShootButton.interactable, Is.False);
            Assert.That(hud.CancelArea.gameObject.activeSelf, Is.False);
            Assert.That(battle.CameraDirector.IsShotCameraReady, Is.False);
            Assert.That(player.GetComponent<UnitShootVisualController>().IsWaitingToStart, Is.True);
            yield return WaitForActiveAction();
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1f));
            Assert.That(enemy.Attributes.GetValue(AttributeType.Health), Is.EqualTo(6f));
            Assert.That(hud.ShotResultText.text, Does.Contain("命中"));
            float popupDeadline = Time.realtimeSinceStartup + 4f;
            while (Object.FindFirstObjectByType<BattleShotResultPopup>() != null &&
                   Time.realtimeSinceStartup < popupDeadline)
                yield return SettleFrame();
            Assert.That(Object.FindFirstObjectByType<BattleShotResultPopup>(), Is.Null);
            float cameraDeadline = Time.realtimeSinceStartup + 5f;
            while (!battle.CameraRig.IsCameraSettled(battle.CameraRig.TacticalCamera) &&
                   Time.realtimeSinceStartup < cameraDeadline)
                yield return SettleFrame();
            Assert.That(battle.CameraRig.IsCameraSettled(battle.CameraRig.TacticalCamera), Is.True);
            Assert.That(battle.CameraDirector.FocusedTarget, Is.Null);
            Vector2 tacticalCenter = new Vector2(battle.CameraRig.ViewCenter.x, battle.CameraRig.ViewCenter.z);
            Vector2 shooterCenter = new Vector2(player.transform.position.x, player.transform.position.z);
            Assert.That(Vector2.Distance(tacticalCenter, shooterCenter), Is.LessThan(.02f));
        }

        [UnityTest]
        public IEnumerator CancellingPendingShotRestoresTacticalViewWithoutSpendingAp()
        {
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            var targetCell = new Vector2Int(4, 1);
            for (int x = 2; x <= targetCell.x; x++)
                battle.Grid.TrySetBlockState(new Vector2Int(x, 1), Wargame.GridSystem.BlockState.Unblocked);
            Assert.That(battle.Grid.TryMoveUnit(enemy, targetCell), Is.True);
            enemy.transform.position = battle.Grid.GridToWorld(targetCell);
            player.Attributes.SetBaseValue(AttributeType.Aim, 100f);
            Physics.SyncTransforms();
            yield return SettleFrame();

            var hud = Object.FindFirstObjectByType<BattleHUD>();
            yield return RequestShoot(hud);
            Assert.That(hud.CancelShootButton, Is.Not.Null);
            Assert.That(hud.CancelShootButton.transform, Is.SameAs(hud.CancelArea));
            Assert.That(hud.CancelArea.gameObject.activeSelf, Is.True);
            Assert.That(hud.CancelArea.GetComponentInChildren<Text>(true).text, Is.EqualTo("取消射击"));
            float apBefore = player.Attributes.GetValue(AttributeType.ActionPoints);

            yield return ClickUi(hud.TargetButtons[0]);
            Assert.That(controller.SelectedShootTarget, Is.SameAs(enemy));
            Assert.That(hud.IsAbilityConfirmationVisible, Is.True);
            Assert.That(hud.CancelShootButton.interactable, Is.True);
            CinemachineCamera focusCamera = enemy.GetComponent<UnitBattleCameraSet>().GetFocusCamera(0);
            Assert.That(focusCamera.Priority.Value, Is.EqualTo(20));

            yield return ClickUi(hud.CancelShootButton);

            Assert.That(controller.SelectedShootTarget, Is.Null);
            Assert.That(hud.IsAbilityConfirmationVisible, Is.False);
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            Assert.That(hud.CancelArea.gameObject.activeSelf, Is.False);
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(apBefore));
            Assert.That(battle.CameraDirector.FocusedTarget, Is.Null);
            Assert.That(focusCamera.Priority.Value, Is.Zero);
            Assert.That(battle.CameraRig.TacticalCamera.Priority.Value, Is.EqualTo(10));

            yield return RequestShoot(hud);
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.True);
            Assert.That(hud.VisibleTargetCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ShooterCanPeekFromAdjacentCellWithoutMoving()
        {
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            Vector2Int shooterCell = new Vector2Int(1, 1);
            Vector2Int targetCell = new Vector2Int(2, 0);
            battle.Grid.TrySetBlockState(new Vector2Int(2, 0), Wargame.GridSystem.BlockState.Unblocked);
            battle.Grid.TrySetBlockState(new Vector2Int(2, 1), Wargame.GridSystem.BlockState.Unblocked);
            battle.Grid.TrySetBlockState(new Vector2Int(1, 0), Wargame.GridSystem.BlockState.FullBlocked);
            Assert.That(battle.Grid.TryMoveUnit(enemy, targetCell), Is.True);
            enemy.transform.position = battle.Grid.GridToWorld(targetCell);
            player.Attributes.SetBaseValue(AttributeType.Aim, 100f);
            Physics.SyncTransforms();
            yield return SettleFrame();

            Assert.That(Wargame.GridSystem.GridLineOfSight.HasLineOfSight(
                new Wargame.GridSystem.GridLineOfSightMapModifier(battle.Grid), shooterCell, targetCell), Is.False);
            battle.Grid.TryGetUnitCoordinates(player, out Vector2Int shooterBefore);
            battle.Grid.TryGetUnitCoordinates(enemy, out Vector2Int targetBefore);
            var hud = Object.FindFirstObjectByType<BattleHUD>();
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            yield return RequestShoot(hud);
            Assert.That(hud.VisibleTargetCount, Is.EqualTo(1));
            yield return ClickUi(hud.TargetButtons[0]);
            Assert.That(controller.SelectedShootTarget, Is.SameAs(enemy));
            Assert.That(hud.IsAbilityConfirmationVisible, Is.True);
            yield return ClickUi(hud.ConfirmButton);
            yield return WaitForActiveAction();

            battle.Grid.TryGetUnitCoordinates(player, out Vector2Int shooterAfter);
            battle.Grid.TryGetUnitCoordinates(enemy, out Vector2Int targetAfter);
            Assert.That(shooterAfter, Is.EqualTo(shooterBefore));
            Assert.That(targetAfter, Is.EqualTo(targetBefore));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1f));
            Assert.That(enemy.Attributes.GetValue(AttributeType.Health), Is.EqualTo(6f));
        }

        [UnityTest]
        public IEnumerator SwitchingSelectedPlayerRefreshesItsTargetList()
        {
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            var targetCell = new Vector2Int(4, 1);
            for (int x = 2; x <= targetCell.x; x++)
                battle.Grid.TrySetBlockState(new Vector2Int(x, 1), Wargame.GridSystem.BlockState.Unblocked);
            Assert.That(battle.Grid.TryMoveUnit(enemy, targetCell), Is.True);
            enemy.transform.position = battle.Grid.GridToWorld(targetCell);

            var secondCell = new Vector2Int(1, 2);
            battle.Grid.TrySetBlockState(secondCell, Wargame.GridSystem.BlockState.Unblocked);
            Unit secondPlayer = Object.Instantiate(player, battle.Grid.GridToWorld(secondCell), Quaternion.identity);
            secondPlayer.name = "SecondPlayer";
            secondPlayer.Faction = Faction.Player;
            Assert.That(battle.Grid.TryPlaceUnit(secondPlayer, secondCell), Is.True);
            battle.Turns.RegisterUnit(secondPlayer);
            secondPlayer.BeginTurn();
            secondPlayer.Attributes.SetBaseValue(AttributeType.SightRange, 2);
            secondPlayer.Abilities.Grant(new ShootAbility(battle.Grid, new ShootSettings()));
            var spawnedField = typeof(BattleSceneManager).GetField("spawned", BindingFlags.Instance | BindingFlags.NonPublic);
            ((System.Collections.Generic.List<Unit>)spawnedField.GetValue(battle)).Add(secondPlayer);
            var secondTargetCell = new Vector2Int(5, 1);
            battle.Grid.TrySetBlockState(secondTargetCell, Wargame.GridSystem.BlockState.Unblocked);
            Unit secondEnemy = Object.Instantiate(enemy, battle.Grid.GridToWorld(secondTargetCell), Quaternion.identity);
            Assert.That(battle.Grid.TryPlaceUnit(secondEnemy, secondTargetCell), Is.True);
            battle.Turns.RegisterUnit(secondEnemy);
            ((System.Collections.Generic.List<Unit>)spawnedField.GetValue(battle)).Add(secondEnemy);
            Physics.SyncTransforms();
            yield return SettleFrame();

            var hud = Object.FindFirstObjectByType<BattleHUD>();
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            yield return RequestShoot(hud);
            Assert.That(controller.ShootTargets.Count, Is.EqualTo(2));
            hud.TargetButtons[0].onClick.Invoke();
            Assert.That(controller.SelectedShootTarget, Is.SameAs(enemy));
            hud.CancelButton.onClick.Invoke();
            hud.TargetButtons[1].onClick.Invoke();
            Assert.That(controller.SelectedShootTarget, Is.SameAs(secondEnemy));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Assert.That(secondEnemy.Attributes.GetValue(AttributeType.Health), Is.EqualTo(10));
            typeof(BattlePlayerController).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { secondPlayer });
            yield return SettleFrame();
            Assert.That(controller.SelectedUnit, Is.SameAs(secondPlayer));
            Assert.That(controller.SelectedShootTarget, Is.Null);
            Assert.That(controller.ShootTargets.Count, Is.Zero);
            Assert.That(Object.FindFirstObjectByType<BattleHUD>().VisibleTargetCount, Is.Zero);

            typeof(BattlePlayerController).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { player });
            yield return SettleFrame();
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            yield return RequestShoot(hud);
            Assert.That(controller.ShootTargets.Count, Is.EqualTo(2));
            Assert.That(Object.FindFirstObjectByType<BattleHUD>().VisibleTargetCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator LosChangeInvalidatesSelectedTargetAndClosesConfirmation()
        {
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            var targetCell = new Vector2Int(4, 1);
            for (int x = 2; x <= targetCell.x; x++)
                battle.Grid.TrySetBlockState(new Vector2Int(x, 1), Wargame.GridSystem.BlockState.Unblocked);
            Assert.That(battle.Grid.TryMoveUnit(enemy, targetCell), Is.True);
            enemy.transform.position = battle.Grid.GridToWorld(targetCell);
            Physics.SyncTransforms();
            yield return SettleFrame();

            var hud = Object.FindFirstObjectByType<BattleHUD>();
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            yield return RequestShoot(hud);
            Assert.That(hud.VisibleTargetCount, Is.EqualTo(1));
            hud.TargetButtons[0].onClick.Invoke();
            yield return SettleFrame();
            Assert.That(controller.SelectedShootTarget, Is.SameAs(enemy));
            Assert.That(hud.IsAbilityConfirmationVisible, Is.True);

            enemy.Attributes.SetBaseValue(AttributeType.Defense, 15f);
            yield return SettleFrame();
            Assert.That(hud.HitChanceText.text, Does.Contain("70%"));

            for (int y = 0; y < battle.Grid.Height; y++)
            {
                Vector2Int wallCell = new Vector2Int(2, y);
                if (battle.Grid.TryGetCell(wallCell, out var cell) && cell.Occupant == null &&
                    cell.BlockState != Wargame.GridSystem.BlockState.FullBlocked)
                    Assert.That(battle.Grid.TrySetBlockState(wallCell, Wargame.GridSystem.BlockState.FullBlocked), Is.True);
            }
            yield return SettleFrame();

            Assert.That(hud.VisibleTargetCount, Is.Zero);
            Assert.That(controller.SelectedShootTarget, Is.Null);
            Assert.That(hud.IsAbilityConfirmationVisible, Is.False);
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2f));
            Assert.That(enemy.Attributes.GetValue(AttributeType.Health), Is.EqualTo(10f));
        }

        private Unit PlaceVisibleEnemyAtDistance(int distance)
        {
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            for (int x = 2; x <= distance + 1; x++)
                battle.Grid.TrySetBlockState(new Vector2Int(x, 1), Wargame.GridSystem.BlockState.Unblocked);
            Assert.That(battle.Grid.TryMoveUnit(enemy, new Vector2Int(distance + 1, 1)), Is.True);
            enemy.transform.position = battle.Grid.GridToWorld(new Vector2Int(distance + 1, 1));
            Physics.SyncTransforms();
            return enemy;
        }

        [UnityTest]
        public IEnumerator SightRangeChangesRefreshVisibleTargetsAndClearInvalidSelection()
        {
            PlaceVisibleEnemyAtDistance(10);
            player.Attributes.SetBaseValue(AttributeType.SightRange, 9);
            yield return SettleFrame();
            var hud = Object.FindFirstObjectByType<BattleHUD>();
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            yield return RequestShoot(hud);
            Assert.That(controller.ShootTargets, Is.Empty);

            player.Attributes.SetBaseValue(AttributeType.SightRange, 10);
            yield return SettleFrame();
            Assert.That(hud.VisibleTargetCount, Is.EqualTo(1), "超过旧 6 格射程的可见敌人也必须加入列表。");
            hud.TargetButtons[0].onClick.Invoke();
            yield return SettleFrame();
            Assert.That(hud.IsAbilityConfirmationVisible, Is.True);

            player.Attributes.SetBaseValue(AttributeType.SightRange, 9);
            yield return SettleFrame();
            Assert.That(controller.SelectedShootTarget, Is.Null);
            Assert.That(hud.VisibleTargetCount, Is.Zero);
            Assert.That(hud.IsAbilityConfirmationVisible, Is.False);
            player.Attributes.SetBaseValue(AttributeType.SightRange, 12);
            yield return SettleFrame();
            Assert.That(hud.VisibleTargetCount, Is.EqualTo(1));
            Assert.That(controller.SelectedShootTarget, Is.Null);
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator MovingTargetAcrossCurveStepRefreshesDisplayedAndCommittedChance()
        {
            Unit enemy = PlaceVisibleEnemyAtDistance(9);
            player.Attributes.SetBaseValue(AttributeType.Aim, 70);
            enemy.Attributes.SetBaseValue(AttributeType.Defense, 20);
            player.Abilities.Remove(player.Abilities.GetAbility<ShootAbility>());
            var ability = new ShootAbility(battle.Grid, new ShootSettings(), new MissAtFiftyPercent());
            player.Abilities.Grant(ability);
            yield return SettleFrame();
            var hud = Object.FindFirstObjectByType<BattleHUD>();
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            yield return RequestShoot(hud);
            hud.TargetButtons[0].onClick.Invoke();
            yield return SettleFrame();
            Assert.That(hud.HitChanceText.text, Does.Contain("60%"));
            PlaceVisibleEnemyAtDistance(10);
            yield return SettleFrame();
            Assert.That(controller.SelectedShootTarget, Is.SameAs(enemy));
            Assert.That(hud.HitChanceText.text, Does.Contain("50%"));
            hud.ConfirmButton.onClick.Invoke();
            yield return SettleFrame();
            Assert.That(ability.LastResult.Distance, Is.EqualTo(10));
            Assert.That(ability.LastResult.HitChance, Is.EqualTo(50));
            Assert.That(ability.LastResult.Hit, Is.False);
            Assert.That(enemy.Attributes.GetValue(AttributeType.Health), Is.EqualTo(10));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ZeroChanceVisibleTargetStaysSelectableAndCanConfirm()
        {
            Unit enemy = PlaceVisibleEnemyAtDistance(3);
            player.Attributes.SetBaseValue(AttributeType.Aim, 0);
            enemy.Attributes.SetBaseValue(AttributeType.Defense, 100);
            yield return SettleFrame();
            var hud = Object.FindFirstObjectByType<BattleHUD>();
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            yield return RequestShoot(hud);
            Assert.That(hud.VisibleTargetCount, Is.EqualTo(1));
            yield return ClickUi(hud.TargetButtons[0]);
            Assert.That(hud.HitChanceText.text, Does.Contain("0%"));
            Assert.That(hud.IsAbilityConfirmationVisible, Is.True);
            yield return ClickUi(hud.ConfirmButton);
            yield return WaitForActiveAction();
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1));
            Assert.That(enemy.Attributes.GetValue(AttributeType.Health), Is.EqualTo(10));
            Assert.That(hud.ShotResultText.text, Does.Contain("未命中"));
            Assert.That(controller.SelectedShootTarget, Is.Null);
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
        }

        private IEnumerator RequestShoot(BattleHUD hud)
        {
            Button button = hud.AbilityButtons.First(b => b.gameObject.activeSelf && b.GetComponentInChildren<Text>(true).text.Contains("射击"));
            Assert.That(button.interactable, Is.True);
            button.onClick.Invoke();
            yield return SettleFrame();
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator CancellingAbilityKeepsTargetAndRepeatedConfirmationCommitsOnce()
        {
            Unit enemy = PlaceVisibleEnemyAtDistance(3);
            player.Attributes.SetBaseValue(AttributeType.Aim, 100);
            yield return SettleFrame();
            var hud = Object.FindFirstObjectByType<BattleHUD>();
            yield return RequestShoot(hud);
            yield return ClickUi(hud.TargetButtons[0]);
            yield return ClickUi(hud.CancelButton);
            Assert.That(hud.IsAbilityConfirmationVisible, Is.False);
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.True);
            Assert.That(controller.SelectedShootTarget, Is.SameAs(enemy));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Assert.That(enemy.Attributes.GetValue(AttributeType.Health), Is.EqualTo(10));
            hud.ConfirmButton.onClick.Invoke(); // 已取消的旧确认不得提交。
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            yield return ClickUi(hud.TargetButtons[0]);
            hud.ConfirmButton.onClick.Invoke();
            hud.ConfirmButton.onClick.Invoke();
            yield return WaitForActiveAction();
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1));
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            Assert.That(controller.SelectedShootTarget, Is.Null);
        }

        [UnityTest]
        public IEnumerator ReselectingSameUnitDuringConfirmationClosesSessionWithoutMoving()
        {
            yield return PlaceCameraAndPlayerInInterior();
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            battle.Grid.TryGetUnitCoordinates(player, out Vector2Int playerCell);
            Vector2Int enemyCell = playerCell + Vector2Int.right * 3;
            for (int x = playerCell.x + 1; x <= enemyCell.x; x++)
                battle.Grid.TrySetBlockState(new Vector2Int(x, playerCell.y), Wargame.GridSystem.BlockState.Unblocked);
            Assert.That(battle.Grid.TryMoveUnit(enemy, enemyCell), Is.True);
            enemy.transform.position = battle.Grid.GridToWorld(enemyCell);
            // 使用屏幕坐标定位，适应编辑器中不同尺寸的 Game 视图。
            Vector2 selectionPoint = new Vector2(view.pixelWidth * .6f, view.pixelHeight * .8f);
            Assert.That(battle.CameraRig.TryGetPlanePoint(selectionPoint, out Vector3 planePoint), Is.True);
            GameObject.Find("BattleViewCenter").transform.position += player.transform.position - planePoint;
            Physics.SyncTransforms();
            yield return SettleFrame();
            var hud = Object.FindFirstObjectByType<BattleHUD>();
            yield return RequestShoot(hud);
            yield return ClickUi(hud.TargetButtons[0]);
            battle.Grid.TryGetUnitCoordinates(player, out Vector2Int before);
            Assert.That(Object.FindFirstObjectByType<BattleInputReader>().IsOverUI(UnitScreen), Is.False);
            Set(mouse.position, UnitScreen); Press(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.SelectedUnit, Is.SameAs(player));
            Assert.That(hud.IsInputBlocked, Is.False);
            Assert.That(controller.HasActiveGesture, Is.False);
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            Set(mouse.position, view.WorldToScreenPoint(battle.Grid.GridToWorld(Target)));
            Release(mouse.leftButton);
            yield return SettleFrame();
            battle.Grid.TryGetUnitCoordinates(player, out Vector2Int after);
            Assert.That(after, Is.EqualTo(before));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            hud.ConfirmButton.onClick.Invoke();
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator RevokedAbilityAndDeadTargetCannotUseStaleConfirmation()
        {
            Unit enemy = PlaceVisibleEnemyAtDistance(3);
            yield return SettleFrame();
            var hud = Object.FindFirstObjectByType<BattleHUD>();
            yield return RequestShoot(hud);
            yield return ClickUi(hud.TargetButtons[0]);
            player.Abilities.Remove(player.Abilities.GetAbility<ShootAbility>());
            hud.ConfirmButton.onClick.Invoke();
            yield return SettleFrame();
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Assert.That(enemy.Attributes.GetValue(AttributeType.Health), Is.EqualTo(10));
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            player.Abilities.Grant(new ShootAbility(battle.Grid, new ShootSettings()));
            yield return SettleFrame();
            yield return RequestShoot(hud);
            yield return ClickUi(hud.TargetButtons[0]);
            enemy.Kill();
            hud.ConfirmButton.onClick.Invoke();
            yield return SettleFrame();
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Assert.That(hud.IsAbilityConfirmationVisible, Is.False);
            Assert.That(controller.SelectedShootTarget, Is.Null);
        }

        [UnityTest]
        public IEnumerator NoneAndSelfAbilityButtonsActivateImmediately()
        {
            var noTarget = new ImmediateUiAbility("扫描", AbilityTargetType.None);
            var self = new ImmediateUiAbility("警戒", AbilityTargetType.Self);
            player.Abilities.Grant(noTarget);
            player.Abilities.Grant(self);
            yield return SettleFrame();
            var hud = Object.FindFirstObjectByType<BattleHUD>();
            hud.AbilityButtons.First(b => b.GetComponentInChildren<Text>(true).text.Contains("扫描")).onClick.Invoke();
            Assert.That(noTarget.ActivationCount, Is.EqualTo(1));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1));
            hud.AbilityButtons.First(b => b.GetComponentInChildren<Text>(true).text.Contains("警戒")).onClick.Invoke();
            Assert.That(self.ActivationCount, Is.EqualTo(1));
            Assert.That(self.LastTarget.Unit, Is.SameAs(player));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.Zero);
            Assert.That(hud.IsInputBlocked, Is.False);
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
        }

        private sealed class ImmediateUiAbility : AbilityBase
        {
            public int ActivationCount;
            public AbilityTarget LastTarget;
            public ImmediateUiAbility(string name, AbilityTargetType type) : base(name, type) { DisplayName = name; }
            protected override void OnActivate(AbilityTarget target)
            { ActivationCount++; LastTarget = target; EndAbility(); }
        }

        private sealed class MissAtFiftyPercent : IShootRandomSource
        {
            public float NextValue() => 0.55f;
        }

        [UnityTest]
        public IEnumerator LethalShotRemovesTargetFromGridTurnAndTargetList()
        {
            Unit enemy = battle.SpawnedUnits.First(u => u.Faction == Faction.Enemy);
            var targetCell = new Vector2Int(4, 1);
            for (int x = 2; x <= targetCell.x; x++)
                battle.Grid.TrySetBlockState(new Vector2Int(x, 1), Wargame.GridSystem.BlockState.Unblocked);
            Assert.That(battle.Grid.TryMoveUnit(enemy, targetCell), Is.True);
            enemy.transform.position = battle.Grid.GridToWorld(targetCell);
            player.Attributes.SetBaseValue(AttributeType.Aim, 100f);
            enemy.Attributes.SetBaseValue(AttributeType.Health, 4f);
            enemy.Attributes.SetBaseValue(AttributeType.Dodge, 0f);
            enemy.Attributes.SetBaseValue(AttributeType.Armor, 0f);
            Physics.SyncTransforms();
            yield return SettleFrame();

            var hud = Object.FindFirstObjectByType<BattleHUD>();
            Assert.That(hud.TargetPanel.gameObject.activeSelf, Is.False);
            yield return RequestShoot(hud);
            Assert.That(hud.VisibleTargetCount, Is.EqualTo(1));
            hud.TargetButtons[0].onClick.Invoke();
            yield return SettleFrame();
            hud.ConfirmButton.onClick.Invoke();
            yield return WaitForActiveAction();

            Assert.That(enemy.IsAlive, Is.False);
            Assert.That(battle.Grid.TryGetUnitCoordinates(enemy, out _), Is.False);
            Assert.That(battle.Turns.Units.Contains(enemy), Is.False);
            Assert.That(controller.SelectedShootTarget, Is.Null);
            Assert.That(hud.VisibleTargetCount, Is.Zero);
            Assert.That(hud.IsAbilityConfirmationVisible, Is.False);
        }

        private IEnumerator WaitForActiveAction()
        {
            float deadline = Time.realtimeSinceStartup + 6;
            while (battle.HasActiveAction && Time.realtimeSinceStartup < deadline) yield return SettleFrame();
            Assert.That(battle.HasActiveAction, Is.False, "视觉动作必须完成并释放能力锁。");
        }

        [UnityTest]
        public IEnumerator MouseDragPreviewsThenCommitsOnlyOnRelease()
        {
            Vector2Int target = Target;
            Set(mouse.position, UnitScreen); Press(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.IsDragging, Is.True);
            Set(mouse.position, CellScreen(target));
            yield return SettleFrame();
            Assert.That(controller.PreviewPlan, Is.Not.Null);
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            battle.Grid.TryGetUnitCoordinates(player, out var before);
            Assert.That(before, Is.EqualTo(Vector2Int.one));
            Release(mouse.leftButton);
            yield return SettleFrame();
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
            yield return SettleFrame();
            Assert.That(controller.IsDragging, Is.True);
            Assert.That(controller.PreviewPlan, Is.Null);
            Set(mouse.position, highPoint + Vector2.right * 2);
            Release(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.SelectedUnit, Is.SameAs(player));
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            var endButton = Object.FindFirstObjectByType<BattleHUD>().EndTurnButton.GetComponent<RectTransform>();
            Vector2 endScreen = RectTransformUtility.WorldToScreenPoint(null, endButton.TransformPoint(endButton.rect.center));
            Set(mouse.position, endScreen); Press(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.IsDragging, Is.False, "从 UI 按下不能开始棋盘手势。");
            Set(mouse.position, CellScreen(Target)); Release(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Set(mouse.position, UnitScreen); Press(mouse.leftButton);
            yield return SettleFrame();
            Set(mouse.position, endScreen); Release(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(battle.Turns.RoundNumber, Is.EqualTo(1), "移动手势松开到结束回合按钮不能触发该按钮。");
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Set(mouse.position, UnitScreen); Press(mouse.leftButton);
            yield return SettleFrame();
            var cancel = Object.FindFirstObjectByType<BattleHUD>().CancelArea;
            Vector2 cancelScreen = RectTransformUtility.WorldToScreenPoint(null, cancel.TransformPoint(cancel.rect.center));
            Set(mouse.position, cancelScreen);
            yield return SettleFrame();
            Release(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Set(mouse.position, UnitScreen); Press(mouse.leftButton);
            yield return SettleFrame();
            Set(mouse.position, new Vector2(-100,-100)); Release(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            battle.Grid.TryGetUnitCoordinates(player, out var cell);
            Assert.That(cell, Is.EqualTo(Vector2Int.one));
        }

        [UnityTest]
        public IEnumerator TouchDragIgnoresSecondaryFingerAndMoves()
        {
            Vector2Int target = Target;
            BeginTouch(1, UnitScreen, screen: touch);
            yield return SettleFrame();
            Assert.That(controller.IsDragging, Is.True, PointerDiagnostics());
            BeginTouch(2, new Vector2(5,5), screen: touch);
            MoveTouch(1, CellScreen(target), screen: touch);
            yield return SettleFrame();
            Assert.That(controller.PreviewPlan, Is.Not.Null);
            EndTouch(1, CellScreen(target), screen: touch);
            yield return SettleFrame();
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
            yield return SettleFrame();
            MoveTouch(1, CellScreen(Target), screen: touch);
            CancelTouch(1, CellScreen(Target), screen: touch);
            yield return SettleFrame();
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Set(mouse.position, UnitScreen); Press(mouse.leftButton);
            yield return SettleFrame();
            Object.FindFirstObjectByType<BattleInputReader>().SendMessage("OnApplicationFocus", false);
            Release(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator InterruptedMovementSnapsAndDeathClearsOccupancy()
        {
            Vector2Int target = Target;
            var move = player.Abilities.GetAbility<MoveAbility>();
            Assert.That(player.Abilities.TryActivate(move, AbilityTarget.ForCell(target), out _), Is.True);
            yield return SettleFrame();
            player.GetComponent<UnitMovementVisual>().enabled = false;
            Assert.That(move.IsActive, Is.False);
            Assert.That(player.Tags.HasTag(GameplayTag.Moving), Is.False);
            Assert.That(player.transform.position, Is.EqualTo(battle.Grid.GridToWorld(target)));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(1));
            player.Kill();
            Assert.That(battle.Grid.TryGetUnitCoordinates(player, out _), Is.False);
            Assert.That(battle.Turns.Units.Contains(player), Is.False);
            yield return SettleFrame();
        }

        private IEnumerator PlaceCameraAndPlayerInInterior()
        {
            var cell = new Vector2Int(30,20);
            Assert.That(battle.Grid.TryGetCell(cell, out var data) && data.Occupant == null, Is.True);
            battle.Grid.TrySetBlockState(cell, Wargame.GridSystem.BlockState.Unblocked);
            Assert.That(battle.Grid.TryMoveUnit(player, cell), Is.True);
            player.transform.position = battle.Grid.GridToWorld(cell);
            GameObject.Find("BattleViewCenter").transform.position = player.transform.position;
            Physics.SyncTransforms();
            yield return SettleFrame();
        }

        [UnityTest]
        public IEnumerator InspectorCameraSettingsSurviveInitializationAndButtonRotation()
        {
            var rig = battle.CameraRig;
            var brain = view.GetComponent<CinemachineBrain>();
            var virtualCamera = rig.TacticalCamera;
            var follow = virtualCamera.GetComponent<CinemachineFollow>();
            Transform pitch = virtualCamera.Follow;
            Transform center = pitch.parent;
            rig.Shutdown();
            center.rotation = Quaternion.Euler(0, 123, 0);
            pitch.localRotation = Quaternion.Euler(62, 0, 0);
            follow.FollowOffset = new Vector3(0, 0, -18.5f);
            virtualCamera.Lens.FieldOfView = 51;
            virtualCamera.Lens.NearClipPlane = .25f;
            virtualCamera.Lens.FarClipPlane = 350;
            brain.IgnoreTimeScale = false;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, .15f);
            Vector3 focus = battle.Grid.GridToWorld(new Vector2Int(30, 20));
            rig.Initialize(battle.Grid, focus, controller);
            yield return SettleFrame();
            Quaternion configuredRotation = Quaternion.Euler(0, 123, 0) * Quaternion.Euler(62, 0, 0);
            Assert.That(Quaternion.Angle(view.transform.rotation, configuredRotation), Is.LessThan(.01f));
            Assert.That(view.transform.position.y - focus.y, Is.EqualTo(18.5f * Mathf.Sin(62 * Mathf.Deg2Rad)).Within(.003f));
            Assert.That(view.fieldOfView, Is.EqualTo(51).Within(.001f));
            Assert.That(view.nearClipPlane, Is.EqualTo(.25f).Within(.001f));
            Assert.That(view.farClipPlane, Is.EqualTo(350).Within(.001f));
            Assert.That(virtualCamera.Follow, Is.SameAs(pitch));
            Assert.That(follow.FollowOffset.z, Is.EqualTo(-18.5f));
            Assert.That(brain.IgnoreTimeScale, Is.False);
            Assert.That(brain.DefaultBlend.Style, Is.EqualTo(CinemachineBlendDefinition.Styles.EaseInOut));
            Assert.That(brain.DefaultBlend.Time, Is.EqualTo(.15f));

            var hud = Object.FindFirstObjectByType<BattleHUD>();
            Assert.That(hud.RotateLeftButton.onClick.GetPersistentTarget(0), Is.SameAs(rig));
            Assert.That(hud.RotateRightButton.onClick.GetPersistentTarget(0), Is.SameAs(rig));
            hud.RotateLeftButton.onClick.Invoke();
            Assert.That(rig.IsRotating, Is.True);
            hud.RotateRightButton.onClick.Invoke(); // 同一帧重复点击也必须拦截。
            yield return new WaitForSecondsRealtime(.4f);
            yield return SettleFrame();
            Assert.That(Quaternion.Angle(view.transform.rotation, Quaternion.Euler(0, -90, 0) * configuredRotation), Is.LessThan(.01f));
            Assert.That(Vector3.Distance(rig.ViewCenter, focus), Is.LessThan(.003f));
            hud.RotateRightButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(.4f);
            yield return SettleFrame();
            Assert.That(Quaternion.Angle(view.transform.rotation, configuredRotation), Is.LessThan(.01f));
            Assert.That(view.fieldOfView, Is.EqualTo(51).Within(.001f));
        }

        [UnityTest]
        public IEnumerator InvalidInspectorCameraSettingsAreRejectedWithoutBeingRewritten()
        {
            var rig = battle.CameraRig;
            var brain = view.GetComponent<CinemachineBrain>();
            var virtualCamera = rig.TacticalCamera;
            rig.Shutdown();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.SmartUpdate;
            Assert.That(rig.ValidateConfiguration(out string error), Is.False);
            StringAssert.Contains("ManualUpdate", error);
            Assert.Throws<System.InvalidOperationException>(() => rig.Initialize(battle.Grid, player.transform.position, controller));
            Assert.That(rig.IsInitialized, Is.False);
            Assert.That(brain.UpdateMethod, Is.EqualTo(CinemachineBrain.UpdateMethods.SmartUpdate));
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            Transform target = virtualCamera.Follow;
            virtualCamera.Follow = null;
            Assert.That(rig.ValidateConfiguration(out _), Is.False);
            Assert.That(virtualCamera.Follow, Is.Null);
            virtualCamera.Follow = target;
            rig.Initialize(battle.Grid, player.transform.position, controller);
            yield return SettleFrame();
            Assert.That(rig.IsInitialized, Is.True);
        }

        [UnityTest]
        public IEnumerator PerspectiveOrbitKeepsCenterHeightAndLens()
        {
            yield return PlaceCameraAndPlayerInInterior();
            var rig = battle.CameraRig;
            Vector3 center = rig.ViewCenter;
            Quaternion original = view.transform.rotation;
            float height = view.transform.position.y;
            Assert.That(view.orthographic, Is.False);
            Assert.That(view.fieldOfView, Is.EqualTo(45).Within(.001f));
            Assert.That(height - battle.Grid.Origin.y, Is.EqualTo(12 * battle.Grid.CellSize).Within(.001f));
            Assert.That(view.GetComponent<CinemachineBrain>().UpdateMethod, Is.EqualTo(CinemachineBrain.UpdateMethods.ManualUpdate));
            Assert.That(rig.TryRotate(0), Is.False);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(rig.TryRotate(1), Is.True);
                Assert.That(rig.TryRotate(-1), Is.False);
                Assert.That(battle.TryEndPlayerTurn(), Is.False);
                Set(mouse.position, UnitScreen); Press(mouse.leftButton);
                yield return SettleFrame();
                Assert.That(controller.HasActiveGesture, Is.False);
                Release(mouse.leftButton);
                float deadline = Time.realtimeSinceStartup + 2;
                while (rig.IsRotating && Time.realtimeSinceStartup < deadline)
                {
                    yield return SettleFrame();
                    Assert.That(Vector3.Distance(center, rig.ViewCenter), Is.LessThan(.003f));
                    Assert.That(view.transform.position.y, Is.EqualTo(height).Within(.003f));
                    Assert.That(view.fieldOfView, Is.EqualTo(45).Within(.001f));
                }
                Assert.That(rig.IsRotating, Is.False);
                Assert.That(Quaternion.Angle(original, view.transform.rotation), Is.EqualTo((i + 1) % 4 == 3 ? 90 : (i + 1) % 4 * 90).Within(.01f));
            }
            Assert.That(Quaternion.Angle(original, view.transform.rotation), Is.LessThan(.01f));
            Assert.That(rig.TryRotate(-1), Is.True);
            rig.enabled = false;
            Assert.That(rig.IsRotating, Is.False);
            rig.enabled = true;
            yield return SettleFrame();
            Assert.That(Quaternion.Angle(original, view.transform.rotation), Is.EqualTo(90).Within(.01f));
        }

        [UnityTest]
        public IEnumerator TouchPanLocksFingerAndFocusLossStopsIt()
        {
            yield return PlaceCameraAndPlayerInInterior();
            Vector2 start = new Vector2(Screen.width * .65f, Screen.height * .55f);
            Vector3 before = battle.CameraRig.ViewCenter;
            BeginTouch(1,start,touch);
            yield return SettleFrame();
            Assert.That(controller.IsPanning, Is.True, PointerDiagnostics());
            BeginTouch(2,new Vector2(3,3),touch);
            MoveTouch(1,start + Vector2.left * 35,touch);
            yield return SettleFrame();
            Assert.That(Vector3.Distance(before, battle.CameraRig.ViewCenter), Is.GreaterThan(.1f));
            EndTouch(1,start + Vector2.left * 35,touch);
            yield return SettleFrame();
            Assert.That(controller.HasActiveGesture, Is.False);
            before = battle.CameraRig.ViewCenter;
            MoveTouch(2,new Vector2(80,80),touch);
            EndTouch(2,new Vector2(80,80),touch);
            yield return SettleFrame();
            Assert.That(Vector3.Distance(before, battle.CameraRig.ViewCenter), Is.LessThan(.001f));
            Set(mouse.position,start); Press(mouse.leftButton);
            yield return SettleFrame();
            Object.FindFirstObjectByType<BattleInputReader>().SendMessage("OnApplicationFocus",false);
            Set(mouse.position,start + Vector2.right * 80); Release(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.HasActiveGesture, Is.False);
            Assert.That(Vector3.Distance(before, battle.CameraRig.ViewCenter), Is.LessThan(.001f));
        }

        [UnityTest]
        public IEnumerator StationaryUnitDragScrollsAndReleaseUsesFinalCamera()
        {
            yield return PlaceCameraAndPlayerInInterior();
            player.Attributes.SetBaseValue(AttributeType.Mobility, 16);
            Set(mouse.position,UnitScreen); Press(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.IsDragging, Is.True);
            Vector2 edge = new Vector2(Screen.width - 2, Screen.height * .5f);
            Set(mouse.position,edge);
            yield return SettleFrame();
            Assert.That(controller.PreviewPlan, Is.Not.Null);
            var first = controller.PreviewPlan.Destination;
            Vector3 before = battle.CameraRig.ViewCenter;
            yield return new WaitForSecondsRealtime(.3f);
            yield return SettleFrame();
            Assert.That(Vector3.Distance(before,battle.CameraRig.ViewCenter), Is.GreaterThan(.5f));
            Assert.That(controller.PreviewPlan, Is.Not.Null);
            Assert.That(controller.PreviewPlan.Destination, Is.Not.EqualTo(first));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            MoveAbility move = player.Abilities.GetAbility<MoveAbility>();
            var reachableBeforeRelease = move.GetReachableCells();
            Release(mouse.leftButton);
            yield return SettleFrame();
            // 合成松手先入输入队列；使用处理完松手后的镜头，避免多滚一帧使旧期望跨格。
            Assert.That(battle.CameraRig.TryGetPlanePoint(edge,out var finalPoint), Is.True);
            Assert.That(battle.Grid.TryWorldToGrid(finalPoint,out var expected), Is.True);
            Assert.That(battle.Grid.TryGetUnitCoordinates(player,out var actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            int pathCost = reachableBeforeRelease.ReachableCosts[expected];
            int cost = Mathf.CeilToInt((float)pathCost / move.BudgetPerActionPoint);
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2-cost));
            before = battle.CameraRig.ViewCenter;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(Vector3.Distance(before,battle.CameraRig.ViewCenter), Is.LessThan(.001f));
            yield return WaitForMovement();
        }

        [UnityTest]
        public IEnumerator PanCenterRemainsInsideBoardAtEveryHeading()
        {
            var rig = battle.CameraRig;
            for (int heading = 0; heading < 4; heading++)
            {
                for (int step = 0; step < 12; step++)
                {
                    Vector2 anchor = new Vector2(Screen.width*.2f,Screen.height*.35f);
                    Assert.That(rig.BeginPan(anchor), Is.True);
                    rig.DragPan(new Vector2(Screen.width*.8f,Screen.height*.7f),false);
                    rig.EndPan();
                    yield return SettleFrame();
                    Vector3 center = rig.ViewCenter - battle.Grid.Origin;
                    Assert.That(center.x, Is.InRange(-.002f,battle.Grid.Width*battle.Grid.CellSize+.002f));
                    Assert.That(center.z, Is.InRange(-.002f,battle.Grid.Height*battle.Grid.CellSize+.002f));
                    Assert.That(center.y, Is.EqualTo(0).Within(.002f));
                }
                Assert.That(rig.TryRotate(1), Is.True);
                yield return new WaitForSecondsRealtime(.4f);
                yield return SettleFrame();
            }
        }
    }
}
