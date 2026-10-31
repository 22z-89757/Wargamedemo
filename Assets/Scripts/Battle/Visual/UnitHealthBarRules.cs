using System;
using UnityEngine;

namespace Wargame.Battle
{
    /// <summary>只用于显示的整格换算，不修改生命属性。</summary>
    public static class UnitHealthBarRules
    {
        public static bool TryGetSegments(float health, float maxHealth, out double total, out double filled)
        {
            total = filled = 0;
            if (!float.IsFinite(maxHealth) || maxHealth <= 0 || !float.IsFinite(health)) return false;
            // double 避免整数容量溢出，并避免 float 比例误差把整格多填一格。
            total = Math.Ceiling((double)maxHealth);
            double clamped = Math.Max(0, Math.Min((double)health, maxHealth));
            filled = Math.Min(total, Math.Ceiling(clamped * total / maxHealth));
            return true;
        }
    }

    [Serializable]
    public sealed class UnitHealthBarSettings
    {
        public Vector2 BaseSize = new Vector2(96, 10);
        [Min(0)] public float WorldOffset = 2f;
        [Min(0)] public float NearDistance = 10;
        [Min(0)] public float FarDistance = 30;
        [Min(.01f)] public float NearScale = 1.1f;
        [Min(.01f)] public float FarScale = .65f;

        public bool TryValidate(out string error)
        {
            error = null;
            if (!float.IsFinite(BaseSize.x) || !float.IsFinite(BaseSize.y) || BaseSize.x <= 0 || BaseSize.y <= 0 ||
                !float.IsFinite(WorldOffset) || WorldOffset < 0 ||
                !float.IsFinite(NearDistance) || !float.IsFinite(FarDistance) || NearDistance < 0 || FarDistance <= NearDistance ||
                !float.IsFinite(NearScale) || !float.IsFinite(FarScale) || FarScale <= 0 || NearScale <= FarScale)
                error = "血条尺寸、头顶偏移或距离缩放配置无效。";
            return error == null;
        }

        public float GetScale(float distance) => Mathf.Lerp(NearScale, FarScale,
            Mathf.Clamp01((distance - NearDistance) / (FarDistance - NearDistance)));
    }
}
