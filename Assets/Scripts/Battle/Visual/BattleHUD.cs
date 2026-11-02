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
        [SerializeField] private Text targetEmptyText;
        [SerializeField] private Text selectedTargetText;
        [SerializeField] private Text hitChanceText;
        [SerializeField] private Text shotResultText;
        [SerializeField] private Button fireButton;
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
        public event Action FireRequested;
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
        public Button FireButton => fireButton;
        public Text HitChanceText => hitChanceText;
        public Text ShotResultText => shotResultText;
        public IReadOnlyList<Button> TargetButtons => targetButtons;
        public IReadOnlyList<Button> AbilityButtons => abilityButtons;
        public int VisibleTargetCount { get; private set; }
        public bool IsEndTurnConfirmationVisible => endTurnDialog != null && endTurnDialog.gameObject.activeSelf;
        public bool IsInputBlocked => IsEndTurnConfirmationVisible;

        public void Initialize(BattleSceneManager manager)
        {
            if (uiController != null) return;
            if (!ValidateConfiguration(out string error)) throw new InvalidOperationException(error);
            scene = manager;
            endTurnDialog.gameObject.SetActive(false);
            if (endTurnButton != null)
            {
                endTurnButton.onClick.RemoveListener(OnEndTurnClicked);
                endTurnButton.onClick.AddListener(OnEndTurnClicked);
            }
            confirmEndTurnButton.onClick.RemoveListener(ConfirmEndTurn);
            confirmEndTurnButton.onClick.AddListener(ConfirmEndTurn);
            cancelEndTurnButton.onClick.RemoveListener(CancelEndTurn);
            cancelEndTurnButton.onClick.AddListener(CancelEndTurn);
            if (cancelArea != null) cancelArea.gameObject.SetActive(false);
            ApplySafeArea();
            targetButtonTemplate.gameObject.SetActive(false);
            targetPanel.gameObject.SetActive(false);
            fireButton.gameObject.SetActive(false);
            fireButton.onClick.RemoveListener(RequestFire);
            fireButton.onClick.AddListener(RequestFire);
            uiController = new BattleUiController();
            uiController.Initialize(manager, this);
        }

        public bool ValidateConfiguration(out string error)
        {
            Canvas canvas = interactionCanvas;
            GraphicRaycaster[] raycasters = canvas == null ? Array.Empty<GraphicRaycaster>() :
                canvas.GetComponents<GraphicRaycaster>();
            error = canvas == null || canvas.gameObject != gameObject || raycasters.Length != 1 || !raycasters[0].enabled ||
                endTurnButton == null || targetPanel == null || targetList == null ||
                targetButtonTemplate == null || targetButtonTemplate.GetComponentInChildren<Text>(true) == null ||
                abilityButtonTemplate == null || abilityButtonTemplate.GetComponentInChildren<Text>(true) == null ||
                targetEmptyText == null || selectedTargetText == null || hitChanceText == null ||
                shotResultText == null || fireButton == null || infoCanvas == null ||
                roundText == null || factionText == null || selectedUnitText == null ||
                actionPointsText == null || abilityStatusText == null || endTurnDialog == null ||
                endTurnWarningText == null || confirmEndTurnButton == null || cancelEndTurnButton == null
                ? "战斗 UI 必须在 Inspector 中配置交互/信息 Canvas、文本、按钮、模板和结束回合弹窗引用。" : null;
            if (error == null)
            {
                if (infoCanvas == canvas || infoCanvas.transform.parent != canvas.transform.parent ||
                    infoCanvas.GetComponent<GraphicRaycaster>() != null ||
                    infoCanvas.GetComponent<CanvasScaler>() == null ||
                    !IsUnderCanvas(roundText, infoCanvas) || !IsUnderCanvas(factionText, infoCanvas) ||
                    !IsUnderCanvas(hintText, infoCanvas) || !IsUnderCanvas(selectedUnitText, infoCanvas) ||
                    !IsUnderCanvas(actionPointsText, infoCanvas) || !IsUnderCanvas(abilityStatusText, infoCanvas) ||
                    !IsUnderCanvas(selectedTargetText, infoCanvas) || !IsUnderCanvas(hitChanceText, infoCanvas) ||
                    !IsUnderCanvas(shotResultText, infoCanvas))
                    error = "战斗信息 Canvas 必须与交互 Canvas 同属 Canvas 根物体、带 CanvasScaler 且不能挂 GraphicRaycaster。";
                else
                {
                    CanvasScaler infoScaler = infoCanvas.GetComponent<CanvasScaler>();
                    CanvasScaler interactionScaler = canvas.GetComponent<CanvasScaler>();
                    if (interactionScaler == null || infoScaler.uiScaleMode != interactionScaler.uiScaleMode ||
                        infoScaler.referenceResolution != interactionScaler.referenceResolution ||
                        !Mathf.Approximately(infoScaler.matchWidthOrHeight, interactionScaler.matchWidthOrHeight))
                        error = "战斗只读信息 Canvas 必须与交互 Canvas 使用相同的缩放和参考分辨率。";
                    else
                    {
                        foreach (Graphic graphic in infoCanvas.GetComponentsInChildren<Graphic>(true))
                            if (graphic.raycastTarget)
                            {
                                error = "战斗只读信息 Canvas 的 Graphic 必须关闭 raycastTarget。";
                                break;
                            }
                        if (error == null && (endTurnDialog.GetComponent<Image>() == null ||
                            !endTurnDialog.transform.IsChildOf(canvas.transform) ||
                            !endTurnWarningText.transform.IsChildOf(endTurnDialog) ||
                            !confirmEndTurnButton.transform.IsChildOf(endTurnDialog) ||
                            !cancelEndTurnButton.transform.IsChildOf(endTurnDialog)))
                            error = "结束回合弹窗、提示文本和确认/取消按钮必须在交互 Canvas 中预先配置。";
                    }
                }
            }
            return error == null;
        }

        private static bool IsUnderCanvas(Graphic graphic, Canvas canvas) =>
            graphic != null && canvas != null && graphic.transform.IsChildOf(canvas.transform);

        private void OnEndTurnClicked()
        {
            if (endTurnButton == null || !endTurnButton.interactable || scene == null) return;
            if (scene.HasUnspentPlayerActionPoints)
            {
                if (endTurnDialog != null && !endTurnDialog.gameObject.activeSelf)
                {
                    endTurnDialog.gameObject.SetActive(true);
                    EndTurnConfirmationChanged?.Invoke(true);
                }
            }
            else EndTurnRequested?.Invoke();
        }

        private void ConfirmEndTurn()
        {
            if (!IsEndTurnConfirmationVisible) return;
            if (endTurnDialog != null) endTurnDialog.gameObject.SetActive(false);
            EndTurnConfirmationChanged?.Invoke(false);
            EndTurnConfirmed?.Invoke();
        }

        private void CancelEndTurn()
        {
            if (!IsEndTurnConfirmationVisible) return;
            if (endTurnDialog != null) endTurnDialog.gameObject.SetActive(false);
            EndTurnConfirmationChanged?.Invoke(false);
            EndTurnCancelled?.Invoke();
        }

        private void RequestFire() { if (fireButton != null && fireButton.interactable) FireRequested?.Invoke(); }

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
            if (uiController != null) uiController.SetAttack(targets, selectedTarget, canFire);
        }

        private void RenderAttackView(IReadOnlyList<ShootPreview> targets, ShootPreview? selectedTarget, bool showPanel, bool canFire)
        {
            if (targetPanel == null) return;
            targetPanel.gameObject.SetActive(showPanel);
            VisibleTargetCount = targets != null ? targets.Count : 0;
            EnsureTargetButtons(VisibleTargetCount);
            displayedTargets.Clear();
            if (targetEmptyText != null) { targetEmptyText.text = BattleUiText.NoTarget; targetEmptyText.gameObject.SetActive(VisibleTargetCount == 0); }
            for (int i = 0; i < targetButtons.Count; i++)
            {
                bool visible = targets != null && i < targets.Count;
                targetButtons[i].gameObject.SetActive(visible);
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
            if (fireButton != null)
            {
                fireButton.gameObject.SetActive(hasTarget);
                fireButton.interactable = hasTarget && canFire;
            }
        }

        public void RenderState(BattleUiState state, MovePlan plan, bool dragging, bool overCancel)
        {
            if (state == null) return;
            if (roundText != null) roundText.text = $"第 {state.RoundNumber} 回合";
            if (factionText != null) factionText.text = BattleUiText.Faction(state.CurrentFaction);
            if (hintText != null) hintText.text = state.Hint;
            if (cancelArea != null) cancelArea.gameObject.SetActive(dragging);
            if (cancelText != null) cancelText.text = overCancel ? "松开以取消" : "拖到这里取消";
            if (endTurnButton != null) endTurnButton.interactable = state.CanEndTurn;
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
            RenderAttackView(state.ShootTargets, state.SelectedTarget, state.SelectedUnit != null && state.CurrentFaction == Faction.Player, state.CanFire);
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
                abilityButtons[i].interactable = state.CanActivate || state.Ability is ShootAbility || state.Ability is MoveAbility;
                lines.Add(BattleUiText.AbilityLine(state));
            }
            if (abilityStatusText != null) abilityStatusText.text = string.Join("\n", lines);
        }

        private void EnsureAbilityButtons(int count)
        {
            while (abilityButtons.Count < count)
            {
                int index = abilityButtons.Count;
                Button button = Instantiate(abilityButtonTemplate, interactionCanvas.transform);
                button.name = "AbilityButton";
                button.gameObject.SetActive(false);
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
                rect.pivot = new Vector2(1, .5f);
                rect.sizeDelta = new Vector2(220, 34);
                rect.anchoredPosition = new Vector2(-20, 38 - index * 38);
                Text label = button.GetComponentInChildren<Text>(true);
                if (label != null) label.raycastTarget = false;
                button.onClick.AddListener(() => { if (index < displayedAbilities.Count && button.interactable) AbilityRequested?.Invoke(displayedAbilities[index]); });
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
                button.onClick.AddListener(() => { if (index < displayedTargets.Count) TargetSelected?.Invoke(displayedTargets[index]); });
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
                bool canRotate = scene.IsInitialized && !scene.HasActiveGesture && !scene.CameraRig.IsRotating;
                if (rotateLeftButton != null) rotateLeftButton.interactable = canRotate;
                if (rotateRightButton != null) rotateRightButton.interactable = canRotate;
                if (endTurnButton != null && !IsEndTurnConfirmationVisible) endTurnButton.interactable = scene.CanEndPlayerTurn;
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
            if (fireButton != null) fireButton.onClick.RemoveListener(RequestFire);
            if (confirmEndTurnButton != null) confirmEndTurnButton.onClick.RemoveListener(ConfirmEndTurn);
            if (cancelEndTurnButton != null) cancelEndTurnButton.onClick.RemoveListener(CancelEndTurn);
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
