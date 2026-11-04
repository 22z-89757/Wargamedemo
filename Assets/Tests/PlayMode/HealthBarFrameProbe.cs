using UnityEngine;
using Wargame.Battle;
using Wargame.Units;

namespace Wargame.Tests
{
    /// <summary>在表现层之后观测实际同帧投影，不调用表现层的更新方法。</summary>
    [DefaultExecutionOrder(200)]
    public sealed class HealthBarFrameProbe : MonoBehaviour
    {
        private Unit unit;
        private UnitHealthBarView bar;
        private Camera _camera;
        private float offset;
        public int Samples { get; private set; }
        public float MaxError { get; private set; }
        public void Configure(Unit owner, UnitHealthBarView view, Camera output, float worldOffset)
        { unit = owner; bar = view; _camera = output; offset = worldOffset; }
        private void LateUpdate()
        {
            if (unit == null || bar == null || !bar.gameObject.activeSelf) return;
            Vector2 expected = _camera.WorldToScreenPoint(unit.transform.position + Vector3.up * offset);
            Vector2 actual = RectTransformUtility.WorldToScreenPoint(null, bar.Rect.position);
            MaxError = Mathf.Max(MaxError, Vector2.Distance(expected, actual));
            Samples++;
        }
    }
}
