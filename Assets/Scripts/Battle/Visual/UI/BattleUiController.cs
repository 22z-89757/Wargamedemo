using System;
using System.Collections.Generic;
using Wargame.Units;

namespace Wargame.Battle
{
    /// <summary>
    /// 战斗 UI 的轻量控制器：订阅战斗变化、保存当前界面输入状态并生成快照。
    /// 它不持有 Text/Button，也不直接修改单位属性。
    /// </summary>
    public sealed class BattleUiController : IDisposable
    {
        private BattleSceneManager scene;
        private BattleHUD view;
        private Unit selected;
        private MovePlan movePlan;
        private bool dragging;
        private bool overCancel;
        private string hint = string.Empty;
        private readonly List<ShootPreview> targets = new List<ShootPreview>();
        private ShootPreview? selectedTarget;
        private bool canFire;
        private bool showTargets;
        private bool canCancelShoot;
        private bool initialized;

        public BattleUiState CurrentState { get; private set; }
        public event Action<BattleUiState> StateChanged;

        public void Initialize(BattleSceneManager manager, BattleHUD battleView)
        {
            if (initialized) return;
            scene = manager ?? throw new ArgumentNullException(nameof(manager));
            view = battleView ?? throw new ArgumentNullException(nameof(battleView));
            scene.Changed += OnSceneChanged;
            if (scene.Turns != null)
            {
                scene.Turns.FactionTurnStarted += OnFactionChanged;
                scene.Turns.FactionTurnEnded += OnFactionChanged;
            }
            view.EndTurnRequested += EndTurn;
            view.EndTurnConfirmed += ConfirmEndTurn;
            view.EndTurnCancelled += CancelEndTurn;
            initialized = true;
            Refresh();
        }

        public void SetSelected(Unit unit)
        {
            if (selected == unit) return;
            UnbindSelected();
            selected = unit;
            BindSelected();
            Refresh();
        }

        public void SetMovement(MovePlan plan, bool isDragging, bool isOverCancel, string movementHint,
            bool canCancelPendingShoot = false)
        {
            movePlan = plan;
            dragging = isDragging;
            overCancel = isOverCancel;
            canCancelShoot = canCancelPendingShoot;
            hint = movementHint ?? string.Empty;
            Refresh();
        }

        public void SetAttack(IReadOnlyList<ShootPreview> shootTargets, ShootPreview? selectedShootTarget, bool fireAvailable, bool showPanel)
        {
            targets.Clear();
            if (shootTargets != null) targets.AddRange(shootTargets);
            selectedTarget = selectedShootTarget;
            canFire = fireAvailable;
            showTargets = showPanel;
            Refresh();
        }

        public void Refresh()
        {
            if (!initialized || scene == null || view == null || scene.Turns == null) return;
            SelectedUnitUiState selectedState = null;
            if (selected != null && selected.IsAlive)
            {
                float ap = selected.Attributes.TryGetValue(AttributeType.ActionPoints, out float current) ? current : 0f;
                float maxAp = selected.Attributes.TryGetValue(AttributeType.MaxActionPoints, out float maximum) ? maximum : 0f;
                selectedState = new SelectedUnitUiState(selected, ap, maxAp,
                    BattleUiAbilityAdapter.ReadAll(selected));
            }

            CurrentState = new BattleUiState(scene.Turns.RoundNumber, scene.Turns.CurrentFaction,
                scene.CanEndPlayerTurn && !dragging, scene.HasUnspentPlayerActionPoints,
                hint, selectedState, targets, selectedTarget, canFire, showTargets && scene.CanIssueOrders,
                canCancelShoot && scene.CanIssueOrders);
            view.RenderState(CurrentState, movePlan, dragging, overCancel);
            StateChanged?.Invoke(CurrentState);
        }

        private void BindSelected()
        {
            if (selected == null) return;
            selected.Attributes.AttributeChanged += OnSelectedChanged;
            selected.Tags.TagChanged += OnSelectedTagChanged;
            selected.Abilities.AbilityGranted += OnAbilityChanged;
            selected.Abilities.AbilityRemoved += OnAbilityChanged;
            selected.Abilities.AbilityActivated += OnAbilityActivated;
            selected.Abilities.AbilityEnded += OnAbilityEnded;
        }

        private void UnbindSelected()
        {
            if (selected == null) return;
            selected.Attributes.AttributeChanged -= OnSelectedChanged;
            selected.Tags.TagChanged -= OnSelectedTagChanged;
            selected.Abilities.AbilityGranted -= OnAbilityChanged;
            selected.Abilities.AbilityRemoved -= OnAbilityChanged;
            selected.Abilities.AbilityActivated -= OnAbilityActivated;
            selected.Abilities.AbilityEnded -= OnAbilityEnded;
        }

        private void OnSceneChanged() => Refresh();
        private void OnFactionChanged(Faction faction) => Refresh();
        private void OnSelectedChanged(AttributeType type, float previous, float value) => Refresh();
        private void OnSelectedTagChanged(GameplayTag tag, bool added) => Refresh();
        private void OnAbilityChanged(AbilityBase ability) => Refresh();
        private void OnAbilityActivated(AbilityBase ability, AbilityTarget target) => Refresh();
        private void OnAbilityEnded(AbilityBase ability, bool cancelled) => Refresh();

        private void EndTurn()
        {
            if (scene != null) scene.TryEndPlayerTurn();
        }

        private void ConfirmEndTurn()
        {
            if (scene == null) return;
            // 确认按钮不复用打开弹窗时的旧判定；状态失效时也要立即恢复快照。
            if (!scene.TryEndPlayerTurn()) Refresh();
        }

        private void CancelEndTurn() { Refresh(); }

        public void Dispose()
        {
            if (!initialized) return;
            UnbindSelected();
            if (scene != null)
            {
                scene.Changed -= OnSceneChanged;
                if (scene.Turns != null)
                {
                    scene.Turns.FactionTurnStarted -= OnFactionChanged;
                    scene.Turns.FactionTurnEnded -= OnFactionChanged;
                }
            }
            if (view != null)
            {
                view.EndTurnRequested -= EndTurn;
                view.EndTurnConfirmed -= ConfirmEndTurn;
                view.EndTurnCancelled -= CancelEndTurn;
            }
            scene = null;
            view = null;
            selected = null;
            initialized = false;
        }
    }
}
