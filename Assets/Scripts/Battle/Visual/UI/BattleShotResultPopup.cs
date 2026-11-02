using System;
using UnityEngine;

namespace Wargame.Battle
{
    //处理伤害跳字
    public sealed class BattleShotResultPopup : MonoBehaviour
    {
        private const float HoldDuration = 2f;
        private const int PopupFontSize = 64;
        private const float PopupCharacterSize = .04f;
        private TextMesh label;
        private Camera viewCamera;
        private Vector3 startPosition;
        private Action completed;
        private float elapsed;

        public static BattleShotResultPopup Create(string targetName, string message, Vector3 position,
            Camera camera, Action onCompleted)
        {
            var gameObject = new GameObject("ShotResultPopup");
            var popup = gameObject.AddComponent<BattleShotResultPopup>();
            popup.Initialize(targetName, message, position, camera, onCompleted);
            return popup;
        }

        public string Message => label != null ? label.text : string.Empty;

        private void Initialize(string targetName, string message, Vector3 position, Camera camera, Action onCompleted)
        {
            gameObject.name = $"ShotResult_{targetName}";
            startPosition = position;
            viewCamera = camera;
            completed = onCompleted;
            label = gameObject.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = PopupFontSize;
            label.characterSize = PopupCharacterSize;
            label.fontStyle = FontStyle.Bold;
            label.color = message.Contains("未命中") ? new Color(1f, .38f, .12f) : new Color(1f, .88f, .25f);
            label.text = message;
            label.GetComponent<MeshRenderer>().sortingOrder = 100;
            transform.position = position;
            FaceCamera();
        }

        private void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            transform.position = startPosition + Vector3.up * (Mathf.Min(elapsed, .35f) * .3f);
            FaceCamera();
            if (elapsed < HoldDuration) return;
            Action callback = completed;
            completed = null;
            callback?.Invoke();
            Destroy(gameObject);
        }

        private void FaceCamera()
        {
            if (viewCamera != null) transform.rotation = viewCamera.transform.rotation;
        }

        public void Cancel()
        {
            completed = null;
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }
    }
}
