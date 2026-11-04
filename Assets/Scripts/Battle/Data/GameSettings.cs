using UnityEngine;

namespace Wargame.Battle
{
    [CreateAssetMenu(fileName = "GameSettings", menuName = "Wargame/Game Settings")]
    public sealed class GameSettings : ScriptableObject
    {
        public ShootSettings ShootSettings = new ShootSettings();
        public CoverEffectSettings CoverEffectSettings = new CoverEffectSettings();

        public bool TryValidate(out string error)
        {
            if (ShootSettings == null)
            {
                error = "GameSettings 缺少 ShootSettings。";
                return false;
            }
            if (!ShootSettings.TryValidate(out error)) return false;
            if (CoverEffectSettings == null)
            {
                error = "GameSettings 缺少 CoverEffectSettings。";
                return false;
            }
            return CoverEffectSettings.TryValidate(out error);
        }

        private void OnValidate() => ShootSettings?.NormalizeCurve();
    }
}
