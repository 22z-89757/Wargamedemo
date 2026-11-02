using System.Collections.Generic;
using UnityEngine;
using Wargame.Units;

namespace Wargame.Battle
{
    /// <summary>锁定按下时的手势用途；相机更新后才拾取并提交移动。</summary>
    public sealed class BattlePlayerController : MonoBehaviour
    {
        private enum Gesture { None, UnitMove, CameraPan }
        [SerializeField] private BattleInputReader input;
        [SerializeField] private Camera battleCamera;
        [SerializeField] private BattleCameraRig cameraRig;
        [SerializeField] private BattleMovementPreview preview;
        [SerializeField] private BattleHUD hud;
        [SerializeField, Min(1)] private float dragThresholdPixels = 10f;
        private BattleSceneManager scene;
        private Unit selected;
        private Unit selectedShootTarget;
        private MovePlan plan;
        private readonly List<ShootPreview> shootTargets = new List<ShootPreview>();
        private ShootPreview? selectedShootPreview;
        private bool bound, dirty = true, targetDirty = true;
        private Vector2 pointerPosition, pressPosition;
        private bool hasDragged, releasePending, panStarted;
        private Vector2Int? lastTarget;
        private Gesture gesture;
        public Unit SelectedUnit => selected;
        public bool IsDragging => gesture == Gesture.UnitMove;
        public bool HasActiveGesture => gesture != Gesture.None;
        public bool IsPanning => gesture == Gesture.CameraPan;
        public MovePlan PreviewPlan => plan;
        public Unit SelectedShootTarget => selectedShootTarget;
        public IReadOnlyList<ShootPreview> ShootTargets => shootTargets;

        public bool ValidateConfiguration(out string error)
        {
            error = input == null || battleCamera == null || cameraRig == null || preview == null || hud == null ||
                (cameraRig != null && cameraRig.OutputCamera != battleCamera)
                ? "玩家控制器需要输入、相机、匹配的 CameraRig、范围预览和 HUD。" : null;
            return error == null && input.ValidateConfiguration(out error) && hud.ValidateConfiguration(out error);
        }

        public void Initialize(BattleSceneManager manager)
        {
            scene = manager;
            Bind();
            foreach (Unit unit in scene.SpawnedUnits)
                if (unit != null && unit.Faction == Faction.Player) { Select(unit); break; }
            dirty = true;
        }

        private void OnEnable() { if (scene != null && scene.IsInitialized) Bind(); }
        private void Bind()
        {
            if (bound) return;
            bound = true;
            input.Pressed += OnPressed;
            input.Dragged += OnDragged;
            input.Released += OnReleased;
            input.Cancelled += CancelGesture;
            scene.Changed += MarkDirty;
            scene.Grid.Changed += MarkDirty;
            cameraRig.ViewChanged += OnViewChanged;
            hud.TargetSelected += OnTargetSelected;
            hud.FireRequested += OnFireRequested;
            hud.AbilityRequested += OnAbilityRequested;
            hud.EndTurnConfirmationChanged += OnEndTurnConfirmationChanged;
        }

        private void Select(Unit unit)
        {
            if (selected != null)
            {
                selected.Attributes.AttributeChanged -= OnAttributeChanged;
                selected.Tags.TagChanged -= OnTagChanged;
                selected.GetComponent<UnitVisualController>()?.SetSelected(false);
            }
            selected = unit;
            if (selected != null)
            {
                selected.Attributes.AttributeChanged += OnAttributeChanged;
                selected.Tags.TagChanged += OnTagChanged;
                selected.GetComponent<UnitVisualController>()?.SetSelected(true, scene.Grid.CellSize);
            }
            MarkDirty();
            lastTarget = null;
            selectedShootTarget = null;
            selectedShootPreview = null;
        }

        private void OnPressed(Vector2 position)
        {
            if (scene == null || !scene.IsInitialized || HasActiveGesture || cameraRig.IsRotating ||
                !cameraRig.isActiveAndEnabled || hud.IsInputBlocked || input.IsOverUI(position) || !battleCamera.pixelRect.Contains(position)) return;
            gesture = Gesture.CameraPan;
            if (scene.CanIssueOrders && Physics.Raycast(battleCamera.ScreenPointToRay(position), out RaycastHit hit, battleCamera.farClipPlane))
            {
                Unit unit = hit.collider.GetComponentInParent<Unit>();
                if (unit != null && unit.IsAlive && unit.isActiveAndEnabled && unit.Faction == Faction.Player &&
                    unit.IsTurnActive && scene.Grid.TryGetUnitCoordinates(unit, out _))
                { Select(unit); gesture = Gesture.UnitMove; }
            }
            hasDragged = releasePending = panStarted = false;
            pressPosition = pointerPosition = position;
            targetDirty = true;
        }

        private void OnDragged(Vector2 position)
        {
            if (!HasActiveGesture || releasePending) return;
            pointerPosition = position;
            UpdateDragThreshold();
            targetDirty = true;
            PrepareCameraInput();
        }

        private void OnReleased(Vector2 position)
        {
            if (!HasActiveGesture) return;
            pointerPosition = position;
            UpdateDragThreshold();
            releasePending = true;
            targetDirty = true;
            cameraRig.SetEdgeScrollPointer(null);
            PrepareCameraInput();
        }

        private void UpdateDragThreshold()
        {
            if (!hasDragged && (pointerPosition - pressPosition).sqrMagnitude >= dragThresholdPixels * dragThresholdPixels)
            { hasDragged = true; targetDirty = true; }
        }

        private void CancelGesture()
        {
            gesture = Gesture.None;
            hasDragged = releasePending = panStarted = false;
            plan = null;
            lastTarget = null;
            if (cameraRig != null) cameraRig.CancelInteraction();
            targetDirty = true;
        }

        private void Update()
        {
            if (scene == null || !scene.IsInitialized) return;
            if (hud != null && hud.IsInputBlocked)
            {
                if (HasActiveGesture) CancelGesture();
                return;
            }
            if (IsDragging && selected == null) { CancelGesture(); Select(null); }
            if (selected != null && (!selected.IsAlive || !selected.isActiveAndEnabled))
            { if (IsDragging) CancelGesture(); Select(null); }
            if (selectedShootTarget != null && (!selectedShootTarget.IsAlive || !scene.Grid.TryGetUnitCoordinates(selectedShootTarget, out _)))
            { selectedShootTarget = null; selectedShootPreview = null; MarkDirty(); }
            if (HasActiveGesture && (!cameraRig.isActiveAndEnabled || (IsDragging && !scene.CanIssueOrders))) CancelGesture();
            PrepareCameraInput();
        }

        private void PrepareCameraInput()
        {
            if (IsPanning && hasDragged)
            {
                if (!panStarted) panStarted = cameraRig.BeginPan(pressPosition);
                cameraRig.DragPan(pointerPosition, input.IsOverUI(pointerPosition));
                if (releasePending) cameraRig.EndPan();
            }
            cameraRig.SetEdgeScrollPointer(IsDragging && hasDragged && !releasePending && !input.IsOverUI(pointerPosition)
                ? pointerPosition : (Vector2?)null);
        }

        private void LateUpdate()
        {
            if (scene == null || !scene.IsInitialized) return;
            if (dirty) RefreshRange();
            if (dirty) RefreshShootTargets();
            // CameraRig 的 LateUpdate 优先执行，拾取始终使用 Cinemachine 本帧输出。
            if (IsDragging && (targetDirty || releasePending)) RefreshTarget();
            else if (!IsDragging && targetDirty) { plan = null; preview.ShowPath(null); }
            if (releasePending)
            {
                MovePlan request = IsDragging ? plan : null;
                bool blocked = input.IsOverUI(pointerPosition) || hud.IsCancelArea(pointerPosition);
                CancelGesture();
                if (!blocked && request != null && selected != null && scene.CanIssueOrders)
                    selected.Abilities.TryActivate("Move", AbilityTarget.ForCell(request.Destination), out _);
                if (dirty) RefreshRange();
                if (dirty) RefreshShootTargets();
                preview.ShowPath(null);
            }
            if (targetDirty || dirty || HasActiveGesture)
                hud.Render(selected, plan, IsDragging, IsDragging && hud.IsCancelArea(pointerPosition));
            if (targetDirty || dirty || HasActiveGesture)
                hud.RenderAttack(shootTargets, selectedShootPreview, selected != null && scene.CanIssueOrders,
                    selectedShootPreview.HasValue && selectedShootPreview.Value.CanFire && !HasActiveGesture && !cameraRig.IsRotating);
            dirty = targetDirty = false;
        }

        private void RefreshRange()
        {
            var ability = selected != null ? selected.Abilities.GetAbility<MoveAbility>() : null;
            bool available = scene.CanIssueOrders && selected != null && selected.IsTurnActive && ability != null &&
                !selected.Tags.HasAny(ability.BlockedTags);
            preview.ShowRange(available ? ability.GetReachableCells() : null, ability?.BudgetPerActionPoint ?? 0);
            targetDirty = true;
        }

        private void RefreshTarget()
        {
            Vector2Int? target = null;
            if (hasDragged && battleCamera.pixelRect.Contains(pointerPosition) && !input.IsOverUI(pointerPosition) &&
                cameraRig.TryGetPlanePoint(pointerPosition, out Vector3 point) &&
                scene.Grid.TryWorldToGrid(point, out Vector2Int cell)) target = cell;
            if (target == lastTarget && !dirty) return;
            lastTarget = target;
            plan = null;
            if (target.HasValue && selected != null && scene.CanIssueOrders)
                selected.Abilities.GetAbility<MoveAbility>()?.TryGetMovePlan(target.Value, out plan, out _);
            preview.ShowPath(plan);
        }

        private void MarkDirty() { dirty = targetDirty = true; }
        private void OnViewChanged() => targetDirty = true;
        private void OnAttributeChanged(AttributeType type, float previous, float value) => MarkDirty();
        private void OnTagChanged(GameplayTag tag, bool added) => MarkDirty();

        private void RefreshShootTargets()
        {
            shootTargets.Clear();
            selectedShootPreview = null;
            if (selected == null || !scene.CanIssueOrders || !selected.IsTurnActive)
            {
                selectedShootTarget = null;
                return;
            }

            ShootAbility ability = selected.Abilities.GetAbility<ShootAbility>();
            if (ability == null) return;
            foreach (Unit candidate in scene.SpawnedUnits)
            {
                if (candidate == null || candidate == selected || candidate.Faction == selected.Faction) continue;
                ShootPreview preview = ability.GetPreview(candidate);
                // 可瞄准集合由能力的视野规则决定；AP 和 0% 命中率不应隐藏可见目标。
                if (preview.IsAvailable) shootTargets.Add(preview);
            }
            shootTargets.Sort((a, b) => a.Target.UnitId.CompareTo(b.Target.UnitId));

            if (selectedShootTarget != null)
            {
                foreach (ShootPreview preview in shootTargets)
                    if (preview.Target == selectedShootTarget) { selectedShootPreview = preview; break; }
                if (!selectedShootPreview.HasValue) selectedShootTarget = null;
            }
        }

        private void OnTargetSelected(Unit target)
        {
            if (selected == null || target == null || !scene.CanIssueOrders || HasActiveGesture || cameraRig.IsRotating) return;
            RefreshShootTargets();
            foreach (ShootPreview preview in shootTargets)
            {
                if (preview.Target != target) continue;
                selectedShootTarget = target;
                selectedShootPreview = preview;
                targetDirty = true;
                return;
            }
        }

        private void OnFireRequested()
        {
            if (selected == null || selectedShootTarget == null || !scene.CanIssueOrders || HasActiveGesture || cameraRig.IsRotating) return;
            ShootAbility ability = selected.Abilities.GetAbility<ShootAbility>();
            if (ability == null) return;
            bool fired = selected.Abilities.TryActivate(ability, AbilityTarget.ForUnit(selectedShootTarget), out _);
            if (fired) hud.ShowShotResult(ability.LastResult);
            if (!fired) selectedShootTarget = null;
            MarkDirty();
        }

        private void OnAbilityRequested(AbilityBase ability)
        {
            if (selected == null || ability == null || !scene.CanIssueOrders || HasActiveGesture || cameraRig.IsRotating)
                return;

            // 移动和射击仍由各自的目标交互完成；按钮只选择操作，不提前扣 AP。
            if (ability is MoveAbility || ability is ShootAbility)
            {
                MarkDirty();
                return;
            }

            if (ability.TargetType == AbilityTargetType.Self)
            {
                selected.Abilities.TryActivate(ability, AbilityTarget.Self(selected), out _);
                MarkDirty();
                return;
            }

            if (ability.TargetType != AbilityTargetType.None)
            {
                // Unit/Cell 能力必须经过各自的目标入口；没有目标时不提交伪目标。
                MarkDirty();
                return;
            }

            selected.Abilities.TryActivate(ability, AbilityTarget.None, out _);
            MarkDirty();
        }

        private void OnEndTurnConfirmationChanged(bool visible)
        {
            if (visible && HasActiveGesture) CancelGesture();
            if (cameraRig != null) cameraRig.SetEdgeScrollPointer(null);
        }

        public void Shutdown()
        {
            if (bound)
            {
                input.Pressed -= OnPressed;
                input.Dragged -= OnDragged;
                input.Released -= OnReleased;
                input.Cancelled -= CancelGesture;
                if (hud != null)
                {
                    hud.TargetSelected -= OnTargetSelected;
                    hud.FireRequested -= OnFireRequested;
                    hud.AbilityRequested -= OnAbilityRequested;
                    hud.EndTurnConfirmationChanged -= OnEndTurnConfirmationChanged;
                }
                if (cameraRig != null) cameraRig.ViewChanged -= OnViewChanged;
                if (scene != null)
                {
                    scene.Changed -= MarkDirty;
                    if (scene.Grid != null) scene.Grid.Changed -= MarkDirty;
                }
            }
            bound = false;
            CancelGesture();
            Select(null);
            shootTargets.Clear();
            selectedShootTarget = null;
            selectedShootPreview = null;
            if (preview != null) preview.Clear();
            if (hud != null && scene != null)
            {
                hud.Render(null, null, false);
                hud.RenderAttack(shootTargets, null, false, false);
            }
        }
        private void OnDisable() => Shutdown();
    }
}
