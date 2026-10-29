using UnityEngine;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Battle
{
    /// <summary>从单位开始的拖动只产生预览，释放时才请求 MoveAbility。</summary>
    public sealed class BattlePlayerController : MonoBehaviour
    {
        [SerializeField] private BattleInputReader input;
        [SerializeField] private Camera battleCamera;
        [SerializeField] private BattleMovementPreview preview;
        [SerializeField] private BattleHUD hud;
        [SerializeField, Min(1)] private float dragThresholdPixels = 10f;
        private BattleSceneManager scene;
        private Unit selected;
        private MovePlan plan;
        private bool bound, dirty = true;
        private Vector2 pointerPosition;
        private Vector2 pressPosition;
        private bool hasDragged;
        private Vector2Int? lastTarget;
        public Unit SelectedUnit => selected;
        public bool IsDragging { get; private set; }
        public MovePlan PreviewPlan => plan;

        public bool ValidateConfiguration(out string error)
        {
            error = input == null || battleCamera == null || preview == null || hud == null
                ? "玩家控制器需要输入读取器、相机、范围预览和 HUD 引用。" : null;
            return error == null && input.ValidateConfiguration(out error);
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
            input.Cancelled += CancelDrag;
            scene.Changed += MarkDirty;
            scene.Grid.Changed += MarkDirty;
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
            dirty = true;
            lastTarget = null;
        }

        private void OnPressed(Vector2 position)
        {
            if (scene == null || !scene.CanIssueOrders || input.IsOverUI(position)) return;
            Ray ray = battleCamera.ScreenPointToRay(position);
            if (!Physics.Raycast(ray, out RaycastHit hit, battleCamera.farClipPlane)) return;
            Unit unit = hit.collider.GetComponentInParent<Unit>();
            if (unit == null || !unit.IsAlive || !unit.isActiveAndEnabled || unit.Faction != Faction.Player ||
                !unit.IsTurnActive || !scene.Grid.TryGetUnitCoordinates(unit, out _)) return;
            Select(unit);
            IsDragging = true;
            hasDragged = false;
            pressPosition = position;
            pointerPosition = position;
            Refresh();
        }

        private void OnDragged(Vector2 position)
        {
            if (!IsDragging) return;
            pointerPosition = position;
            UpdateDragThreshold(position);
            RefreshTarget();
        }

        private void OnReleased(Vector2 position)
        {
            if (!IsDragging) return;
            pointerPosition = position;
            UpdateDragThreshold(position);
            dirty = true;
            RefreshTarget();
            MovePlan request = plan;
            bool blockedByUI = input.IsOverUI(position) || hud.IsCancelArea(position);
            CancelDrag();
            if (!blockedByUI && request != null && selected != null && scene.CanIssueOrders)
            {
                var ability = selected.Abilities.GetAbility<MoveAbility>();
                if (ability != null) selected.Abilities.TryActivate(ability, AbilityTarget.ForCell(request.Destination), out _);
            }
            dirty = true;
            Refresh();
        }

        private void UpdateDragThreshold(Vector2 position)
        {
            if (!hasDragged && (position - pressPosition).sqrMagnitude >= dragThresholdPixels * dragThresholdPixels)
            {
                hasDragged = true;
                dirty = true;
            }
        }

        private void CancelDrag()
        {
            IsDragging = false;
            hasDragged = false;
            plan = null;
            lastTarget = null;
            dirty = true;
            if (scene != null && scene.IsInitialized) Refresh();
        }

        private void Update()
        {
            if (scene == null || !scene.IsInitialized) return;
            if (selected != null && (!selected.IsAlive || !selected.isActiveAndEnabled)) Select(null);
            if (IsDragging && !scene.CanIssueOrders) CancelDrag();
            if (dirty) Refresh();
        }

        private void Refresh()
        {
            if (scene == null || !scene.IsInitialized) return;
            if (selected == null) selected = null;
            var ability = selected != null ? selected.Abilities.GetAbility<MoveAbility>() : null;
            bool available = scene.CanIssueOrders && selected != null && selected.IsTurnActive && ability != null &&
                !selected.Tags.HasAny(ability.BlockedTags);
            preview.ShowRange(available ? ability.GetReachableCells() : null, ability?.BudgetPerActionPoint ?? 0);
            if (IsDragging) RefreshTarget();
            else { plan = null; preview.ShowPath(null); hud.Render(selected, null, false); }
            dirty = false;
        }

        private void RefreshTarget()
        {
            Vector2Int? target = null;
            bool overCancel = hud.IsCancelArea(pointerPosition);
            if (hasDragged && !input.IsOverUI(pointerPosition))
            {
                Ray ray = battleCamera.ScreenPointToRay(pointerPosition);
                if (new Plane(Vector3.up, scene.Grid.Origin).Raycast(ray, out float distance) &&
                    scene.Grid.TryWorldToGrid(ray.GetPoint(distance), out Vector2Int cell)) target = cell;
            }
            if (target == lastTarget && !dirty)
            { hud.Render(selected, plan, true, overCancel); return; }
            lastTarget = target;
            plan = null;
            if (target.HasValue && selected != null && scene.CanIssueOrders)
                selected.Abilities.GetAbility<MoveAbility>()?.TryGetMovePlan(target.Value, out plan, out _);
            preview.ShowPath(plan);
            hud.Render(selected, plan, true, overCancel);
        }

        private void MarkDirty() => dirty = true;
        private void OnAttributeChanged(AttributeType type, float previous, float value) => MarkDirty();
        private void OnTagChanged(GameplayTag tag, bool added) => MarkDirty();

        public void Shutdown()
        {
            if (bound)
            {
                input.Pressed -= OnPressed;
                input.Dragged -= OnDragged;
                input.Released -= OnReleased;
                input.Cancelled -= CancelDrag;
                if (scene != null)
                {
                    scene.Changed -= MarkDirty;
                    if (scene.Grid != null) scene.Grid.Changed -= MarkDirty;
                }
            }
            bound = false;
            IsDragging = false;
            plan = null;
            Select(null);
            if (preview != null) preview.Clear();
            if (hud != null && scene != null) hud.Render(null, null, false);
        }
        private void OnDisable() => Shutdown();
    }
}
