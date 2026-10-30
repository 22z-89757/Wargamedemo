using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
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
            yield return SettleFrame();
            yield return SettleFrame();
            Assert.That(battle.Turns.CurrentFaction, Is.EqualTo(Faction.Player));
            Assert.That(battle.Turns.RoundNumber, Is.EqualTo(2));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
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
            player.GetComponent<UnitVisualController>().enabled = false;
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
            var virtualCamera = Object.FindFirstObjectByType<CinemachineCamera>();
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
            var virtualCamera = Object.FindFirstObjectByType<CinemachineCamera>();
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
        public IEnumerator MousePanGrabsGroundAndPausesAcrossUi()
        {
            yield return PlaceCameraAndPlayerInInterior();
            var rig = battle.CameraRig;
            Vector2 start = new Vector2(Screen.width * .65f, Screen.height * .55f);
            Assert.That(rig.TryGetPlanePoint(start, out Vector3 anchor), Is.True);
            Vector3 before = rig.ViewCenter;
            Set(mouse.position, start); Press(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.IsPanning, Is.True);
            Assert.That(rig.TryRotate(1), Is.False);
            Assert.That(battle.TryEndPlayerTurn(), Is.False);
            Vector2 end = start + new Vector2(35,20);
            Set(mouse.position, end);
            yield return SettleFrame();
            Assert.That(Vector3.Distance(before, rig.ViewCenter), Is.GreaterThan(.1f));
            Assert.That(Vector2.Distance(view.WorldToScreenPoint(anchor), end), Is.LessThan(1));
            Vector3 paused = rig.ViewCenter;
            Set(mouse.position, new Vector2(Screen.width * .5f, Screen.height - 30));
            yield return SettleFrame();
            Assert.That(Vector3.Distance(paused, rig.ViewCenter), Is.LessThan(.001f));
            Set(mouse.position, end);
            yield return SettleFrame();
            Assert.That(Vector3.Distance(paused, rig.ViewCenter), Is.LessThan(.001f), "离开 UI 后需重新锚定。");
            Set(mouse.position, end + Vector2.right * 20); Release(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.HasActiveGesture, Is.False);
            Assert.That(Vector3.Distance(paused, rig.ViewCenter), Is.GreaterThan(.05f));
            Assert.That(controller.SelectedUnit, Is.SameAs(player));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
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
            Assert.That(battle.CameraRig.TryGetPlanePoint(edge,out var finalPoint), Is.True);
            Assert.That(battle.Grid.TryWorldToGrid(finalPoint,out var expected), Is.True);
            int cost = controller.PreviewPlan.ActionPointCost;
            Release(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(battle.Grid.TryGetUnitCoordinates(player,out var actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2-cost));
            before = battle.CameraRig.ViewCenter;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(Vector3.Distance(before,battle.CameraRig.ViewCenter), Is.LessThan(.001f));
            yield return WaitForMovement();
        }

        [UnityTest]
        public IEnumerator EdgeDirectionsUiBlockingAndIdleHover()
        {
            yield return PlaceCameraAndPlayerInInterior();
            var rig = battle.CameraRig;
            Vector3 before = rig.ViewCenter;
            Set(mouse.position,new Vector2(Screen.width - 2,Screen.height * .5f));
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(Vector3.Distance(before,rig.ViewCenter), Is.LessThan(.001f));
            Set(mouse.position,UnitScreen); Press(mouse.leftButton);
            yield return SettleFrame();
            foreach (Vector2 edge in new[] { new Vector2(Screen.width-2,Screen.height*.5f),
                new Vector2(2,Screen.height*.5f),new Vector2(2,Screen.height-2),new Vector2(Screen.width-2,2) })
            {
                before = rig.ViewCenter;
                Set(mouse.position,edge);
                yield return new WaitForSecondsRealtime(.15f);
                yield return SettleFrame();
                Assert.That(Vector3.Distance(before,rig.ViewCenter), Is.GreaterThan(.05f));
            }
            before = rig.ViewCenter;
            Set(mouse.position,new Vector2(Screen.width*.5f,Screen.height-30));
            yield return SettleFrame();
            Vector3 stopped = rig.ViewCenter;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(Vector3.Distance(stopped,rig.ViewCenter), Is.LessThan(.001f));
            Release(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(player.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            Assert.That(controller.HasActiveGesture, Is.False);
            Assert.That(rig.TryRotate(1), Is.True);
            yield return new WaitForSecondsRealtime(.4f);
            yield return SettleFrame();
            Set(mouse.position,UnitScreen); Press(mouse.leftButton);
            yield return SettleFrame();
            Assert.That(controller.IsDragging, Is.True);
            before = rig.ViewCenter;
            Set(mouse.position,new Vector2(Screen.width-2,Screen.height*.5f));
            yield return new WaitForSecondsRealtime(.2f);
            yield return SettleFrame();
            Vector3 delta = rig.ViewCenter-before;
            Vector3 right = Vector3.ProjectOnPlane(view.transform.right,Vector3.up).normalized;
            Assert.That(Vector3.Dot(delta,right), Is.GreaterThan(.2f));
            Object.FindFirstObjectByType<BattleInputReader>().CancelGesture();
            Release(mouse.leftButton);
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
