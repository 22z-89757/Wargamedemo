using System;
using System.Collections.Generic;
using UnityEngine;
using Wargame.Units;

namespace Wargame.Battle
{
    /// <summary>战斗级伤害跳字入口；直接实例化并销毁 Prefab，当前不使用对象池。</summary>
    [DisallowMultipleComponent]
    public sealed class BattleDamagePopupManager : MonoBehaviour
    {
        [SerializeField] private BattleShotResultPopup viewPrefab;

        private readonly HashSet<BattleShotResultPopup> activeViews = new HashSet<BattleShotResultPopup>();
        private Camera outputCamera;
        private bool initialized;

        public BattleShotResultPopup ViewPrefab => viewPrefab;
        public int ActiveCount => activeViews.Count;
        public bool IsInitialized => initialized;

        /// <summary>供场景生成与测试配置 Prefab；正式场景通过 Inspector 保持该引用。</summary>
        public void Configure(BattleShotResultPopup prefab) => viewPrefab = prefab;

        public bool ValidateConfiguration(out string error)
        {
            error = null;
            if (!isActiveAndEnabled)
            { error = "伤害跳字管理器必须处于启用状态。"; return false; }
            if (viewPrefab == null || !viewPrefab.enabled || !viewPrefab.gameObject.activeSelf)
            { error = "伤害跳字管理器需要启用的 BattleDamagePopup Prefab。"; return false; }
            if (viewPrefab.GetComponentInChildren<Canvas>(true) != null)
            { error = "BattleDamagePopup 必须使用 3D TextMeshPro，不能包含 Canvas。"; return false; }
            if (viewPrefab.GetComponentInChildren<TextMesh>(true) != null)
            { error = "BattleDamagePopup 不能继续使用旧 TextMesh。"; return false; }
            return viewPrefab.ValidateConfiguration(out error);
        }

        public void Initialize(Camera camera)
        {
            if (!ValidateConfiguration(out string error)) throw new InvalidOperationException(error);
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            Shutdown();
            outputCamera = camera;
            initialized = true;
        }

        public bool ShowShotResult(ShootResult result, Action onCompleted = null)
        {
            if (!initialized || outputCamera == null || viewPrefab == null ||
                string.IsNullOrEmpty(result.TargetName)) return false;

            BattleShotResultPopup view = Instantiate(viewPrefab, transform);
            activeViews.Add(view);
            try
            {
                view.Play(result, BattleUiText.ShotOutcome(result), outputCamera,
                    completedView => Complete(completedView, onCompleted));
                return true;
            }
            catch (Exception exception)
            {
                activeViews.Remove(view);
                DestroyView(view);
                Debug.LogException(exception, this);
                return false;
            }
        }

        private void Complete(BattleShotResultPopup view, Action onCompleted)
        {
            if (view == null || !activeViews.Remove(view)) return;
            DestroyView(view);
            onCompleted?.Invoke();
        }

        public void Shutdown()
        {
            initialized = false;
            outputCamera = null;
            Clear();
        }

        public void Clear()
        {
            if (activeViews.Count == 0) return;

            var snapshot = new List<BattleShotResultPopup>(activeViews);
            activeViews.Clear();
            foreach (BattleShotResultPopup view in snapshot)
            {
                if (view == null) continue;
                view.Cancel();
                DestroyView(view);
            }
        }

        private static void DestroyView(BattleShotResultPopup view)
        {
            if (view == null) return;
            if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject);
        }

        private void OnDisable() => Shutdown();
        private void OnDestroy() => Shutdown();
    }
}
