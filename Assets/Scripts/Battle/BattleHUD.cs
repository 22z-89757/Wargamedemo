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
        [SerializeField] private RectTransform cancelArea;
        [SerializeField] private Text cancelText;
        [SerializeField] private RectTransform safeArea;
        private BattleSceneManager scene;
        private Rect lastSafeArea;
        private Vector2Int lastResolution;
        private float lastLayoutWidth;
        public RectTransform CancelArea => cancelArea;
        public Button EndTurnButton => endTurnButton;

        public void Configure(Text status, Text hint, Button endTurn, RectTransform cancel, Text cancelLabel, RectTransform safe)
        {
            statusText = status; hintText = hint; endTurnButton = endTurn;
            cancelArea = cancel; cancelText = cancelLabel; safeArea = safe;
        }

        public void Initialize(BattleSceneManager manager)
        {
            scene = manager;
            if (endTurnButton != null)
            {
                endTurnButton.onClick.RemoveListener(EndTurn);
                endTurnButton.onClick.AddListener(EndTurn);
            }
            if (cancelArea != null) cancelArea.gameObject.SetActive(false);
            ApplySafeArea();
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
            if (hintText != null) hintText.text = scene.HasActiveAction ? "Moving..." : overCancel ? "Release to cancel" :
                plan != null ? $"MOVE  {plan.ActionPointCost} AP   /   PATH COST {plan.TotalCost}\n" +
                    $"AP AFTER MOVE  {Mathf.Max(0, unit.Attributes.GetValue(AttributeType.ActionPoints) - plan.ActionPointCost):0.#}   /   Release to move" :
                dragging ? "Drag to a blue or yellow cell\nRelease outside the board to cancel" :
                "Hold a unit, drag to a cell, release to move\nBLUE  1 AP     YELLOW  2 AP";
            if (cancelArea != null) cancelArea.gameObject.SetActive(dragging);
            if (cancelText != null) cancelText.text = overCancel ? "RELEASE TO CANCEL" : "DRAG HERE TO CANCEL";
            if (endTurnButton != null) endTurnButton.interactable = scene.CanEndPlayerTurn && !dragging;
        }

        private void Update()
        {
            if (lastSafeArea != Screen.safeArea || lastResolution != new Vector2Int(Screen.width, Screen.height)) ApplySafeArea();
            if (safeArea != null && !Mathf.Approximately(lastLayoutWidth, safeArea.rect.width)) ApplyResponsiveLayout();
        }

        private void ApplySafeArea()
        {
            lastSafeArea = Screen.safeArea;
            lastResolution = new Vector2Int(Screen.width, Screen.height);
            if (safeArea == null || Screen.width <= 0 || Screen.height <= 0) return;
            safeArea.anchorMin = new Vector2(lastSafeArea.xMin / Screen.width, lastSafeArea.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(lastSafeArea.xMax / Screen.width, lastSafeArea.yMax / Screen.height);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            ApplyResponsiveLayout();
        }

        private void ApplyResponsiveLayout()
        {
            lastLayoutWidth = safeArea.rect.width;
            bool narrow = lastLayoutWidth < 1000;
            if (hintText != null)
            {
                var panel = (RectTransform)hintText.transform.parent;
                panel.sizeDelta = new Vector2(Mathf.Max(200, Mathf.Min(650, lastLayoutWidth - 40)), narrow ? 100 : 82);
                panel.anchoredPosition = new Vector2(20, narrow ? 108 : 22);
            }
            if (cancelArea != null) cancelArea.anchoredPosition = new Vector2(0, narrow ? 226 : 120);
        }

        private void OnDestroy() { if (endTurnButton != null) endTurnButton.onClick.RemoveListener(EndTurn); }
    }
}
