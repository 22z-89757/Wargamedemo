using UnityEngine;
using UnityEngine.UI;

namespace Wargame.Battle
{
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class UnitHealthBarView : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private Image fill;
        [SerializeField] private UnitHealthBarDividers dividers;
        public RectTransform Rect => (RectTransform)transform;
        public double TotalSegments { get; private set; }
        public double FilledSegments { get; private set; }
        public bool HasValidHealth { get; private set; }

        public bool ValidateConfiguration(out string error)
        {
            error = null;
            if (!gameObject.activeSelf || background == null || fill == null || dividers == null ||
                background == fill || !background.transform.IsChildOf(transform) ||
                !fill.transform.IsChildOf(transform) || !dividers.transform.IsChildOf(transform) ||
                fill.sprite == null || fill.type != Image.Type.Filled || fill.fillMethod != Image.FillMethod.Horizontal || fill.fillOrigin != 0 ||
                GetComponentsInChildren<Canvas>(true).Length != 0)
                error = "血条预制体需要背景、左起水平填充和刻度，且不能包含 Canvas。";
            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.canvasRenderer == null) error = "血条 Graphic 需要 CanvasRenderer 才能绘制。";
                if (graphic.raycastTarget) error = "血条 Graphic 必须关闭 raycastTarget。";
            }
            return error == null;
        }

        public void SetSize(Vector2 size)
        {
            if (Rect.sizeDelta != size) Rect.sizeDelta = size;
        }

        public void SetHealth(float health, float maxHealth)
        {
            HasValidHealth = UnitHealthBarRules.TryGetSegments(health, maxHealth, out double total, out double filled);
            TotalSegments = total;
            FilledSegments = filled;
            if (!HasValidHealth) { SetVisible(false); return; }
            dividers.SetSegments(total);
            fill.fillAmount = (float)(filled / total);
        }

        public void SetVisible(bool visible)
        {
            bool active = visible && HasValidHealth;
            if (gameObject.activeSelf != active) gameObject.SetActive(active);
        }
    }
}
