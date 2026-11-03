using System;
using TMPro;
using UnityEngine;
using Wargame.Units;

namespace Wargame.Battle
{
    /// <summary>世界空间 TMP 射击结果视图；实例生命周期由 BattleDamagePopupManager 管理。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMeshPro))]
    public sealed class BattleShotResultPopup : MonoBehaviour
    {
        public const float MinimumHoldDuration = 2f;

        [SerializeField] private TextMeshPro label;
        [SerializeField] private Color hitColor = new Color(1f, .82f, .08f, 1f);
        [SerializeField] private Color grazingColor = new Color(1f, .45f, .05f, 1f);
        [SerializeField] private Color missColor = new Color(1f, .12f, .04f, 1f);
        [SerializeField, Min(0f)] private float worldOffset = 1.5f;
        [SerializeField, Min(0f)] private float riseDistance = .3f;
        [SerializeField, Min(0f)] private float riseDuration = .35f;
        [SerializeField, Min(MinimumHoldDuration)] private float holdDuration = MinimumHoldDuration;

        private Camera viewCamera;
        private Vector3 startPosition;
        private Action<BattleShotResultPopup> completed;
        private float elapsed;
        private bool playing;

        public string Message => label != null ? label.text : string.Empty;
        public TextMeshPro Label => label;
        public Color HitColor => hitColor;
        public Color GrazingColor => grazingColor;
        public Color MissColor => missColor;
        public float WorldOffset => worldOffset;
        public float RiseDistance => riseDistance;
        public float RiseDuration => riseDuration;
        public float HoldDuration => holdDuration;
        public bool IsPlaying => playing;

        /// <summary>供 Prefab 生成与测试显式配置文字引用；视觉参数仍保存在 Prefab。</summary>
        public void Configure(TextMeshPro text) => label = text;

        public bool ValidateConfiguration(out string error)
        {
            error = null;
            if (label == null || label.GetComponent<TextMeshPro>() == null)
            { error = "伤害跳字 Prefab 需要显式引用 3D TextMeshPro。"; return false; }
            if (label.font == null)
            { error = "伤害跳字 TextMeshPro 需要显式字体资产。"; return false; }
            if (!IsFiniteNonNegative(worldOffset) || !IsFiniteNonNegative(riseDistance) ||
                !IsFiniteNonNegative(riseDuration) || float.IsNaN(holdDuration) ||
                float.IsInfinity(holdDuration) || holdDuration < MinimumHoldDuration)
            { error = $"伤害跳字偏移和动画参数必须有效，保持时长不得少于 {MinimumHoldDuration:0.#} 秒。"; return false; }
            return true;
        }

        public void Play(ShootResult result, string message, Camera camera,
            Action<BattleShotResultPopup> onCompleted)
        {
            if (!ValidateConfiguration(out string error)) throw new InvalidOperationException(error);
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (playing) throw new InvalidOperationException("伤害跳字实例正在播放。每次展示必须实例化新 Prefab。");

            gameObject.name = $"ShotResult_{result.TargetName}";
            startPosition = result.TargetPosition + Vector3.up * worldOffset;
            viewCamera = camera;
            completed = onCompleted;
            elapsed = 0f;
            playing = true;
            label.text = message ?? string.Empty;
            label.color = !result.Hit ? missColor : result.Grazing ? grazingColor : hitColor;
            transform.position = startPosition;
            FaceCamera();
        }

        private void Update()
        {
            if (!playing) return;
            elapsed += Time.unscaledDeltaTime;
            float progress = riseDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / riseDuration);
            transform.position = startPosition + Vector3.up * (riseDistance * progress);
            FaceCamera();
            if (elapsed < holdDuration) return;

            playing = false;
            Action<BattleShotResultPopup> callback = completed;
            completed = null;
            callback?.Invoke(this);
        }

        private void FaceCamera()
        {
            if (viewCamera != null) transform.rotation = viewCamera.transform.rotation;
        }

        public void Cancel()
        {
            if (!playing && completed == null) return;
            playing = false;
            completed = null;
            viewCamera = null;
        }

        private void OnDisable() => Cancel();

        private static bool IsFiniteNonNegative(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    }
}
