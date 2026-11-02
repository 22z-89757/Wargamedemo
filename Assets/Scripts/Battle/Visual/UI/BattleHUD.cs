using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Wargame.Units;

namespace Wargame.Battle
{
    /// <summary>战斗 UI 视图层；状态由 BattleUiController 提供，控件只负责渲染和发布用户意图。</summary>
    public sealed class BattleHUD : MonoBehaviour
    {
        public enum ConfirmationMode { None, EndTurn, Ability }
        [SerializeField] private Canvas interactionCanvas;
        [SerializeField] private Canvas infoCanvas;
        [SerializeField] private Text roundText;
        [SerializeField] private Text factionText;
        [SerializeField] private Text selectedUnitText;
        [SerializeField] private Text actionPointsText;
        [SerializeField] private Text abilityStatusText;
        [SerializeField] private Text hintText;
        [SerializeField] private Button endTurnButton;
        [SerializeField] private Button rotateLeftButton;
        [SerializeField] private Button rotateRightButton;
        [SerializeField] private RectTransform cancelArea;
        [SerializeField] private Text cancelText;
        [SerializeField] private RectTransform safeArea;
        [SerializeField] private RectTransform targetPanel;
        [SerializeField] private RectTransform targetList;
        [SerializeField] private Button targetButtonTemplate;
        [SerializeField] private Button abilityButtonTemplate;
        [SerializeField] private RectTransform abilityDic;
        [SerializeField] private Text targetEmptyText;
        [SerializeField] private Text selectedTargetText;
        [SerializeField] private Text hitChanceText;
        [SerializeField] private Text shotResultText;
        [SerializeField] private RectTransform endTurnDialog;
        [SerializeField] private Text endTurnWarningText;
        [SerializeField] private Button confirmEndTurnButton;
        [SerializeField] private Button cancelEndTurnButton;

        private BattleSceneManager scene;
        private BattleUiController uiController;
        private readonly List<Button> targetButtons = new List<Button>();
        private readonly List<Text> targetButtonLabels = new List<Text>();
        private readonly List<Unit> displayedTargets = new List<Unit>();
        private readonly List<Button> abilityButtons = new List<Button>();
        private readonly List<Text> abilityButtonLabels = new List<Text>();
        private readonly List<AbilityBase> displayedAbilities = new List<AbilityBase>();
        private Rect lastSafeArea;
        private Vector2Int lastResolution;

        public event Action<Unit> TargetSelected;
        public event Action AbilityConfirmed;
        public event Action AbilityCancelled;
        public event Action<bool> ConfirmationChanged;
        public event Action<AbilityBase> AbilityRequested;
        public event Action EndTurnRequested;
        public event Action EndTurnConfirmed;
        public event Action EndTurnCancelled;
        public event Action<bool> EndTurnConfirmationChanged;

        public Canvas InfoCanvas => infoCanvas;
        public Canvas InteractionCanvas => interactionCanvas;
        public Text RoundText => roundText;
        public Text FactionText => factionText;
        public Text SelectedUnitText => selectedUnitText;
        public Text ActionPointsText => actionPointsText;
        public Text AbilityStatusText => abilityStatusText;
        public RectTransform ConfirmDialog => endTurnDialog;
        public RectTransform CancelArea => cancelArea;
        public Button EndTurnButton => endTurnButton;
        public Button RotateLeftButton => rotateLeftButton;
        public Button RotateRightButton => rotateRightButton;
        public RectTransform AbilityDic => abilityDic;
        public RectTransform TargetPanel => targetPanel;
        public Button ConfirmButton => confirmEndTurnButton;
        public Button CancelButton => cancelEndTurnButton;
        public Text HitChanceText => hitChanceText;
        public Text ShotResultText => shotResultText;
        public IReadOnlyList<Button> TargetButtons => targetButtons;
        public IReadOnlyList<Button> AbilityButtons => abilityButtons;
        public int VisibleTargetCount { get; private set; }
        public ConfirmationMode ActiveConfirmation { get; private set; }
        public bool IsEndTurnConfirmationVisible => ActiveConfirmation == ConfirmationMode.EndTurn;
        public bool IsAbilityConfirmationVisible => ActiveConfirmation == ConfirmationMode.Ability;
        public bool IsInputBlocked => ActiveConfirmation != ConfirmationMode.None;

        public void Initialize(BattleSceneManager manager)
        {
            if (uiController != null) return;
            scene = manager;
            endTurnDialog.gameObject.SetActive(false);
            ActiveConfirmation = ConfirmationMode.None;
            if (endTurnButton != null)
            {
                endTurnButton.onClick.RemoveListener(OnEndTurnClicked);
                endTurnButton.onClick.AddListener(OnEndTurnClicked);
            }
            confirmEndTurnButton.onClick.RemoveListener(ConfirmAction);
            confirmEndTurnButton.onClick.AddListener(ConfirmAction);
            cancelEndTurnButton.onClick.RemoveListener(CancelAction);
            cancelEndTurnButton.onClick.AddListener(CancelAction);
            if (cancelArea != null) cancelArea.gameObject.SetActive(false);
            ApplySafeArea();
            targetButtonTemplate.gameObject.SetActive(false);
            targetPanel.gameObject.SetActive(false);
            abilityButtonTemplate.gameObject.SetActive(false);
            uiController = new BattleUiController();
            uiController.Initialize(manager, this);
        }

        private void OnEndTurnClicked()
        {
            if (endTurnButton == null || !endTurnButton.interactable || scene == null || IsInputBlocked) return;
            if (scene.HasUnspentPlayerActionPoints)
            {
                ShowConfirmation(ConfirmationMode.EndTurn, BattleUiText.EndTurnWarning);
            }
            else EndTurnRequested?.Invoke();
        }

        public void ShowAbilityConfirmation(string message)
        {
            if (IsInputBlocked) return;
            ShowConfirmation(ConfirmationMode.Ability, message);
        }

        private void ShowConfirmation(ConfirmationMode mode, string message)
        {
            ActiveConfirmation = mode;
            endTurnWarningText.text = message;
            endTurnDialog.gameObject.SetActive(true);
            endTurnDialog.SetAsLastSibling();
            if (mode == ConfirmationMode.EndTurn) EndTurnConfirmationChanged?.Invoke(true);
            ConfirmationChanged?.Invoke(true);
            uiController?.Refresh();
        }

        public void CloseAbilityConfirmation()
        {
            if (IsAbilityConfirmationVisible) CloseConfirmation();
        }

        private void CloseConfirmation()
        {
            bool wasEndTurn = IsEndTurnConfirmationVisible;
            ActiveConfirmation = ConfirmationMode.None;
            if (endTurnDialog != null) endTurnDialog.gameObject.SetActive(false);
            if (wasEndTurn) EndTurnConfirmationChanged?.Invoke(false);
            ConfirmationChanged?.Invoke(false);
            uiController?.Refresh();
        }

        private void ConfirmAction()
        {
            ConfirmationMode mode = ActiveConfirmation;
            if (mode == ConfirmationMode.None) return;
            CloseConfirmation();
            if (mode == ConfirmationMode.EndTurn) EndTurnConfirmed?.Invoke();
            else AbilityConfirmed?.Invoke();
        }

        private void CancelAction()
        {
            ConfirmationMode mode = ActiveConfirmation;
            if (mode == ConfirmationMode.None) return;
            CloseConfirmation();
            if (mode == ConfirmationMode.EndTurn) EndTurnCancelled?.Invoke();
            else AbilityCancelled?.Invoke();
        }

        public void ShowShotResult(ShootResult result)
        {
            if (shotResultText != null) shotResultText.text = BattleUiText.ShotOutcome(result);
        }

        public bool IsCancelArea(Vector2 screenPoint) => cancelArea != null && cancelArea.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(cancelArea, screenPoint);

        public void Render(Unit unit, MovePlan plan, bool dragging, bool overCancel = false)
        {
            if (scene == null || uiController == null) return;
            uiController.SetSelected(unit);
            uiController.SetMovement(plan, dragging, overCancel, BuildHint(unit, plan, dragging, overCancel));
        }

        public void RenderAttack(IReadOnlyList<ShootPreview> targets, ShootPreview? selectedTarget, bool showPanel, bool canFire)
        {
            RenderAttackView(targets, selectedTarget, showPanel, canFire);
            if (uiController != null) uiController.SetAttack(targets, selectedTarget, canFire, showPanel);
        }

        private void RenderAttackView(IReadOnlyList<ShootPreview> targets, ShootPreview? selectedTarget, bool showPanel, bool canFire)
        {
            if (targetPanel == null) return;
            targetPanel.gameObject.SetActive(showPanel);
            VisibleTargetCount = showPanel && targets != null ? targets.Count : 0;
            EnsureTargetButtons(VisibleTargetCount);
            displayedTargets.Clear();
            if (targetEmptyText != null) { targetEmptyText.text = BattleUiText.NoTarget; targetEmptyText.gameObject.SetActive(VisibleTargetCount == 0); }
            for (int i = 0; i < targetButtons.Count; i++)
            {
                bool visible = showPanel && targets != null && i < targets.Count;
                targetButtons[i].gameObject.SetActive(visible);
                targetButtons[i].interactable = !IsInputBlocked;
                if (!visible) continue;
                ShootPreview preview = targets[i];
                displayedTargets.Add(preview.Target);
                targetButtonLabels[i].text = $"{preview.TargetName}    {BattleUiText.HitChance(preview.HitChance)}";
                Image image = targetButtons[i].GetComponent<Image>();
                if (image != null) image.color = selectedTarget.HasValue && selectedTarget.Value.Target == preview.Target
                    ? new Color(0.15f, 0.38f, 0.32f, 1f) : new Color(0.18f, 0.22f, 0.24f, 1f);
            }
            bool hasTarget = selectedTarget.HasValue && selectedTarget.Value.IsAvailable;
            if (selectedTargetText != null) selectedTargetText.text = hasTarget
                ? $"目标：{selectedTarget.Value.TargetName}  生命 {selectedTarget.Value.TargetHealth:0.#}" : BattleUiText.SelectTarget;
            if (hitChanceText != null) hitChanceText.text = hasTarget ? BattleUiText.HitChance(selectedTarget.Value.HitChance) : string.Empty;
        }

        public void RenderState(BattleUiState state, MovePlan plan, bool dragging, bool overCancel)
        {
            if (state == null) return;
            if (roundText != null) roundText.text = $"第 {state.RoundNumber} 回合";
            if (factionText != null) factionText.text = BattleUiText.Faction(state.CurrentFaction);
            if (hintText != null) hintText.text = state.Hint;
            if (cancelArea != null) cancelArea.gameObject.SetActive(dragging);
            if (cancelText != null) cancelText.text = overCancel ? "松开以取消" : "拖到这里取消";
            if (endTurnButton != null) endTurnButton.interactable = state.CanEndTurn && !IsInputBlocked;
            if (state.SelectedUnit == null)
            {
                if (selectedUnitText != null) selectedUnitText.text = BattleUiText.NoSelectedUnit;
                if (actionPointsText != null) actionPointsText.text = string.Empty;
                RenderAbilities(Array.Empty<AbilityUiState>());
            }
            else
            {
                if (selectedUnitText != null) selectedUnitText.text = $"单位：{state.SelectedUnit.DisplayName}";
                if (actionPointsText != null) actionPointsText.text = $"行动点：{state.SelectedUnit.ActionPoints:0.#}/{state.SelectedUnit.MaxActionPoints:0.#}";
                RenderAbilities(state.SelectedUnit.Abilities);
            }
            RenderAttackView(state.ShootTargets, state.SelectedTarget, state.IsTargetPanelVisible, state.CanFire);
        }

        private void RenderAbilities(IReadOnlyList<AbilityUiState> abilities)
        {
            EnsureAbilityButtons(abilities != null ? abilities.Count : 0);
            displayedAbilities.Clear();
            var lines = new List<string>();
            for (int i = 0; i < abilityButtons.Count; i++)
            {
                bool visible = abilities != null && i < abilities.Count;
                abilityButtons[i].gameObject.SetActive(visible);
                if (!visible) continue;
                AbilityUiState state = abilities[i];
                displayedAbilities.Add(state.Ability);
                abilityButtonLabels[i].text = $"{state.DisplayName}  {state.ActionPointCost} AP";
                abilityButtons[i].interactable = state.CanActivate && !IsInputBlocked && scene.CanIssueOrders;
                lines.Add(BattleUiText.AbilityLine(state));
            }
            if (abilityStatusText != null) abilityStatusText.text = string.Join("\n", lines);
        }

        private void EnsureAbilityButtons(int count)
        {
            while (abilityButtons.Count < count)
            {
                int index = abilityButtons.Count;
                Button button = Instantiate(abilityButtonTemplate, abilityDic);
                button.name = "AbilityButton";
                button.gameObject.SetActive(false);
                Text label = button.GetComponentInChildren<Text>(true);
                if (label != null) label.raycastTarget = false;
                button.onClick.AddListener(() => { if (!IsInputBlocked && index < displayedAbilities.Count && button.interactable) AbilityRequested?.Invoke(displayedAbilities[index]); });
                abilityButtons.Add(button);
                abilityButtonLabels.Add(label);
            }
        }

        private void EnsureTargetButtons(int count)
        {
            while (targetButtons.Count < count)
            {
                int index = targetButtons.Count;
                Button button = Instantiate(targetButtonTemplate, targetList);
                button.name = "TargetButton";
                button.onClick.AddListener(() => { if (!IsInputBlocked && index < displayedTargets.Count && button.interactable) TargetSelected?.Invoke(displayedTargets[index]); });
                targetButtons.Add(button);
                targetButtonLabels.Add(button.GetComponentInChildren<Text>(true));
            }
        }

        private string BuildHint(Unit unit, MovePlan plan, bool dragging, bool overCancel)
        {
            if (scene.CameraRig != null && scene.CameraRig.IsRotating) return "正在旋转视角……";
            if (scene.HasActiveAction) return "正在执行操作……";
            if (overCancel) return "松开以取消";
            if (plan != null && unit != null)
                return $"移动消耗 {plan.ActionPointCost} AP，路径消耗 {plan.TotalCost}\n移动后行动点 {Mathf.Max(0, unit.Attributes.GetValue(AttributeType.ActionPoints) - plan.ActionPointCost):0.#}，松开移动";
            if (dragging) return "拖动到蓝色或黄色格子\n在棋盘外松开以取消";
            return "拖动地面平移视角 / 按住单位移动\n蓝色：1 AP，黄色：2 AP / 靠近边缘滚动";
        }

        private void Update()
        {
            if (scene != null)
            {
                bool canRotate = scene.IsInitialized && !scene.HasActiveGesture && !scene.CameraRig.IsRotating && !IsInputBlocked;
                if (rotateLeftButton != null) rotateLeftButton.interactable = canRotate;
                if (rotateRightButton != null) rotateRightButton.interactable = canRotate;
                if (endTurnButton != null) endTurnButton.interactable = scene.CanEndPlayerTurn && !IsInputBlocked;
            }
            if (lastSafeArea != Screen.safeArea || lastResolution != new Vector2Int(Screen.width, Screen.height)) ApplySafeArea();
        }

        private void ApplySafeArea()
        {
            lastSafeArea = Screen.safeArea;
            lastResolution = new Vector2Int(Screen.width, Screen.height);
            if (safeArea == null || Screen.width <= 0 || Screen.height <= 0) return;
            safeArea.anchorMin = new Vector2(lastSafeArea.xMin / Screen.width, lastSafeArea.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(lastSafeArea.xMax / Screen.width, lastSafeArea.yMax / Screen.height);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        public void Shutdown()
        {
            if (uiController != null)
            {
                uiController.Dispose();
                uiController = null;
            }
            if (endTurnButton != null) endTurnButton.onClick.RemoveListener(OnEndTurnClicked);
            if (confirmEndTurnButton != null) confirmEndTurnButton.onClick.RemoveListener(ConfirmAction);
            if (cancelEndTurnButton != null) cancelEndTurnButton.onClick.RemoveListener(CancelAction);
            CloseConfirmation();
            for (int i = 0; i < abilityButtons.Count; i++)
                if (abilityButtons[i] != null)
                    if (Application.isPlaying) Destroy(abilityButtons[i].gameObject); else DestroyImmediate(abilityButtons[i].gameObject);
            abilityButtons.Clear();
            abilityButtonLabels.Clear();
            displayedAbilities.Clear();
            for (int i = 0; i < targetButtons.Count; i++)
                if (targetButtons[i] != null)
                    if (Application.isPlaying) Destroy(targetButtons[i].gameObject); else DestroyImmediate(targetButtons[i].gameObject);
            targetButtons.Clear();
            targetButtonLabels.Clear();
            displayedTargets.Clear();
            VisibleTargetCount = 0;
            scene = null;
        }
    }
}
