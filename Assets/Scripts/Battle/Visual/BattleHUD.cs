using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Wargame.Units;

namespace Wargame.Battle
{
    /// <summary>轻量触屏 HUD；所有棋盘输入通过同一指针手势处理。</summary>
    public sealed class BattleHUD : MonoBehaviour
    {
        [SerializeField] private Text statusText;
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
        [SerializeField] private Text targetEmptyText;
        [SerializeField] private Text selectedTargetText;
        [SerializeField] private Text hitChanceText;
        [SerializeField] private Text shotResultText;
        [SerializeField] private Button fireButton;
        private BattleSceneManager scene;
        private readonly List<Button> targetButtons = new List<Button>();
        private readonly List<Text> targetButtonLabels = new List<Text>();
        private readonly List<Unit> displayedTargets = new List<Unit>();
        private Rect lastSafeArea;
        private Vector2Int lastResolution;
        public event Action<Unit> TargetSelected;
        public event Action FireRequested;
        public RectTransform CancelArea => cancelArea;
        public Button EndTurnButton => endTurnButton;
        public Button RotateLeftButton => rotateLeftButton;
        public Button RotateRightButton => rotateRightButton;
        public Button FireButton => fireButton;
        public Text HitChanceText => hitChanceText;
        public Text ShotResultText => shotResultText;
        public IReadOnlyList<Button> TargetButtons => targetButtons;
        public int VisibleTargetCount { get; private set; }

        public void Configure(Text status, Text hint, Button endTurn, RectTransform cancel, Text cancelLabel, RectTransform safe)
        {
            statusText = status; hintText = hint; endTurnButton = endTurn;
            cancelArea = cancel; cancelText = cancelLabel; safeArea = safe;
        }

        public void Initialize(BattleSceneManager manager)
        {
            if (!ValidateConfiguration(out string error)) throw new InvalidOperationException(error);
            scene = manager;
            if (endTurnButton != null)
            {
                endTurnButton.onClick.RemoveListener(EndTurn);
                endTurnButton.onClick.AddListener(EndTurn);
            }
            if (cancelArea != null) cancelArea.gameObject.SetActive(false);
            ApplySafeArea();
            targetButtonTemplate.gameObject.SetActive(false);
            targetPanel.gameObject.SetActive(false);
            fireButton.gameObject.SetActive(false);
            fireButton.onClick.RemoveListener(RequestFire);
            fireButton.onClick.AddListener(RequestFire);
        }

        public bool ValidateConfiguration(out string error)
        {
            error = targetPanel == null || targetList == null || targetButtonTemplate == null ||
                targetButtonTemplate.GetComponentInChildren<Text>(true) == null || targetEmptyText == null ||
                selectedTargetText == null || hitChanceText == null || shotResultText == null || fireButton == null
                ? "HUD 需要目标面板、列表、列表项模板、目标/命中率/结果文本和开火按钮引用。" : null;
            return error == null;
        }

        private void RequestFire() { if (fireButton.interactable) FireRequested?.Invoke(); }

        public void ShowShotResult(ShootResult result)
        {
            string outcome = !result.Hit ? "MISS" : result.Grazing ? "GRAZE" : "HIT";
            shotResultText.text = $"{result.Target.DisplayName}: {outcome}  -{result.Damage:0.#} HP";
        }

        private void EndTurn() { if (scene != null) scene.TryEndPlayerTurn(); }
        public bool IsCancelArea(Vector2 screenPoint) => cancelArea != null && cancelArea.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(cancelArea, screenPoint);

        public void Render(Unit unit, MovePlan plan, bool dragging, bool overCancel = false)
        {
            if (scene == null) return;
            string phase = scene.Turns.CurrentFaction == Faction.Player ? "PLAYER TURN" : "ENEMY TURN";
            string selected = unit == null ? "Select a unit" : unit.DisplayName + "    AP " +
                unit.Attributes.GetValue(AttributeType.ActionPoints).ToString("0.#") + "/" +
                unit.Attributes.GetValue(AttributeType.MaxActionPoints).ToString("0.#");
            if (statusText != null) statusText.text = $"ROUND {scene.Turns.RoundNumber:00}   /   {phase}\n{selected}";
            if (hintText != null) hintText.text = scene.CameraRig.IsRotating ? "Rotating view..." : scene.HasActiveAction ? "Moving..." : overCancel ? "Release to cancel" :
                plan != null ? $"MOVE  {plan.ActionPointCost} AP   /   PATH COST {plan.TotalCost}\n" +
                    $"AP AFTER MOVE  {Mathf.Max(0, unit.Attributes.GetValue(AttributeType.ActionPoints) - plan.ActionPointCost):0.#}   /   Release to move" :
                dragging ? "Drag to a blue or yellow cell\nRelease outside the board to cancel" :
                "Drag ground to pan / Hold a unit to move\nBLUE  1 AP   YELLOW  2 AP / Edge to scroll";
            if (cancelArea != null) cancelArea.gameObject.SetActive(dragging);
            if (cancelText != null) cancelText.text = overCancel ? "RELEASE TO CANCEL" : "DRAG HERE TO CANCEL";
            if (endTurnButton != null) endTurnButton.interactable = scene.CanEndPlayerTurn && !dragging;
        }

        public void RenderAttack(IReadOnlyList<ShootPreview> targets, ShootPreview? selectedTarget, bool showPanel, bool canFire)
        {
            if (targetPanel == null) return;
            targetPanel.gameObject.SetActive(showPanel);
            VisibleTargetCount = targets != null ? targets.Count : 0;
            EnsureTargetButtons(VisibleTargetCount);
            displayedTargets.Clear();
            if (targetEmptyText != null) targetEmptyText.gameObject.SetActive(VisibleTargetCount == 0);

            for (int i = 0; i < targetButtons.Count; i++)
            {
                bool visible = targets != null && i < targets.Count;
                targetButtons[i].gameObject.SetActive(visible);
                if (!visible) continue;
                ShootPreview preview = targets[i];
                displayedTargets.Add(preview.Target);
                targetButtonLabels[i].text = $"{preview.TargetName}    {preview.HitChance:0}%";
                targetButtons[i].GetComponent<Image>().color = selectedTarget.HasValue && selectedTarget.Value.Target == preview.Target
                    ? new Color(0.15f, 0.38f, 0.32f, 1f) : new Color(0.18f, 0.22f, 0.24f, 1f);
            }

            bool hasTarget = selectedTarget.HasValue && selectedTarget.Value.IsAvailable;
            if (selectedTargetText != null)
                selectedTargetText.text = hasTarget ? $"{selectedTarget.Value.TargetName}  HP {selectedTarget.Value.TargetHealth:0.#}" : "Select a target";
            if (hitChanceText != null)
                hitChanceText.text = hasTarget ? $"HIT CHANCE  {selectedTarget.Value.HitChance:0}%" : string.Empty;
            if (fireButton != null)
            {
                fireButton.gameObject.SetActive(hasTarget);
                fireButton.interactable = hasTarget && canFire;
            }
        }

        private void Update()
        {
            if (scene != null)
            {
                bool canRotate = scene.IsInitialized && !scene.HasActiveGesture && !scene.CameraRig.IsRotating;
                if (rotateLeftButton != null) rotateLeftButton.interactable = canRotate;
                if (rotateRightButton != null) rotateRightButton.interactable = canRotate;
                if (endTurnButton != null) endTurnButton.interactable = scene.CanEndPlayerTurn;
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

        private void EnsureTargetButtons(int count)
        {
            while (targetButtons.Count < count)
            {
                int index = targetButtons.Count;
                Button button = Instantiate(targetButtonTemplate, targetList);
                button.name = "TargetButton";
                button.onClick.AddListener(() =>
                {
                    if (index < displayedTargets.Count) TargetSelected?.Invoke(displayedTargets[index]);
                });
                targetButtons.Add(button);
                targetButtonLabels.Add(button.GetComponentInChildren<Text>(true));
            }
        }

        private void OnDestroy()
        {
            if (endTurnButton != null) endTurnButton.onClick.RemoveListener(EndTurn);
            if (fireButton != null) fireButton.onClick.RemoveListener(RequestFire);
        }
    }
}
