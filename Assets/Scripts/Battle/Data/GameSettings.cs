using UnityEngine;

namespace Wargame.Battle
{
    [CreateAssetMenu(fileName = "GameSettings", menuName = "Wargame/Game Settings")]
    public sealed class GameSettings : ScriptableObject
    {
        public ShootSettings ShootSettings = new ShootSettings();

        public bool TryValidate(out string error)
        {
            if (ShootSettings == null)
            {
                error = "GameSettings 缺少 ShootSettings。";
                return false;
            }
            return ShootSettings.TryValidate(out error);
        }

        private void OnValidate() => ShootSettings?.NormalizeCurve();
    }
}