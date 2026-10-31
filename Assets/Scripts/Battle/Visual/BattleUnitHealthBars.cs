using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Wargame.Units;

namespace Wargame.Battle
{
    /// <summary>独立血条层：场景拥有单位，本组件只拥有视图及事件绑定。</summary>
    [DefaultExecutionOrder(100), DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
    public sealed class BattleUnitHealthBars : MonoBehaviour
    {
        [SerializeField] private Canvas healthCanvas;
        [SerializeField] private UnitHealthBarView viewPrefab;
        [SerializeField] private UnitHealthBarSettings settings = new UnitHealthBarSettings();
        private BattleSceneManager scene;
        private Camera outputCamera;
        private bool subscribed;
        private readonly Dictionary<Unit, Binding> bindings = new Dictionary<Unit, Binding>();
        private readonly HashSet<Unit> desired = new HashSet<Unit>();
        private readonly List<Unit> removed = new List<Unit>();
        public int ViewCount => bindings.Count;
        public UnitHealthBarSettings Settings => settings;
        public Canvas HealthCanvas => healthCanvas;

        private sealed class Binding
        {
            public Unit Unit;
            public CharacterAttribute Attributes;
            public UnitHealthBarView View;
            public bool Dirty = true;
            public Action<Unit> Gone;
            public void AttributeChanged(AttributeType type, float previous, float value)
            {
                if (type == AttributeType.Health || type == AttributeType.MaxHealth) Dirty = true;
            }
        }

        public bool ValidateConfiguration(Canvas hudCanvas, out string error)
        {
            error = null;
            if (!isActiveAndEnabled || healthCanvas == null || !healthCanvas.isActiveAndEnabled ||
                healthCanvas.transform != transform || transform.parent != null ||
                healthCanvas.renderMode != RenderMode.ScreenSpaceOverlay || hudCanvas == null ||
                healthCanvas == hudCanvas || hudCanvas.transform.IsChildOf(transform) ||
                healthCanvas.sortingLayerID != hudCanvas.sortingLayerID || healthCanvas.sortingOrder >= hudCanvas.sortingOrder ||
                GetComponentsInChildren<Canvas>(true).Length != 1)
                error = "需要独立根级 UnitHealthBarCanvas（Overlay），排序低于 BattleUICanvas。";
            else if (GetComponentsInChildren<GraphicRaycaster>(true).Length != 0)
                error = "血条 Canvas 不能包含 GraphicRaycaster。";
            else if (viewPrefab == null)
                error = "需要 UnitHealthBarView 血条预制体。";
            else if (!viewPrefab.ValidateConfiguration(out error)) return false;
            else if (settings == null) error = "需要血条显示参数。";
            else if (!settings.TryValidate(out error)) return false;
            if (error != null) return false;
            var scaler = healthCanvas.GetComponent<CanvasScaler>();
            var hudScaler = hudCanvas.GetComponent<CanvasScaler>();
            if (scaler == null || hudScaler == null || !scaler.enabled ||
                scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize ||
                scaler.uiScaleMode != hudScaler.uiScaleMode || scaler.referenceResolution != hudScaler.referenceResolution ||
                scaler.screenMatchMode != hudScaler.screenMatchMode || scaler.matchWidthOrHeight != hudScaler.matchWidthOrHeight)
                error = "血条 CanvasScaler 需与 BattleUICanvas 使用相同的参考分辨率和缩放方式。";
            return error == null;
        }

        public void Initialize(BattleSceneManager owner, Camera camera)
        {
            if (owner == null || camera == null) throw new ArgumentNullException("血条需要战斗场景和输出摄像机。");
            if (scene != owner || outputCamera != camera)
            {
                Shutdown();
                scene = owner;
                outputCamera = camera;
            }
            if (isActiveAndEnabled) Subscribe();
        }

        private void Subscribe()
        {
            if (!subscribed) { scene.Changed += Refresh; subscribed = true; }
            Refresh();
        }

        public bool TryGetView(Unit unit, out UnitHealthBarView view)
        {
            view = null;
            if (ReferenceEquals(unit, null) || !bindings.TryGetValue(unit, out Binding binding)) return false;
            view = binding.View;
            return view != null;
        }

        public void Refresh()
        {
            if (!isActiveAndEnabled || scene == null) return;
            desired.Clear();
            foreach (Unit unit in scene.SpawnedUnits)
            {
                if (unit == null || !unit.IsAlive) continue;
                desired.Add(unit);
                if (bindings.ContainsKey(unit)) continue;
                var binding = new Binding { Unit = unit, Attributes = unit.Attributes,
                    View = Instantiate(viewPrefab, healthCanvas.transform, false), Gone = Remove };
                binding.View.name = "HealthBar_" + unit.UnitId;
                binding.View.SetSize(settings.BaseSize);
                // 尚未投影的视图不能先闪现在屏幕中心。
                binding.View.gameObject.SetActive(false);
                bindings.Add(unit, binding);
                binding.Attributes.AttributeChanged += binding.AttributeChanged;
                unit.Died += binding.Gone;
                unit.Destroyed += binding.Gone;
            }
            removed.Clear();
            foreach (Unit unit in bindings.Keys)
                if (!desired.Contains(unit)) removed.Add(unit);
            foreach (Unit unit in removed) Remove(unit);
        }

        private void LateUpdate()
        {
            foreach (Binding binding in bindings.Values)
            {
                if (binding.Unit == null || !binding.Unit.IsAlive) { binding.View.SetVisible(false); continue; }
                if (binding.Dirty)
                {
                    binding.Dirty = false;
                    bool hasHealth = binding.Attributes.TryGetValue(AttributeType.Health, out float health);
                    bool hasMax = binding.Attributes.TryGetValue(AttributeType.MaxHealth, out float max);
                    binding.View.SetHealth(hasHealth ? health : float.NaN, hasMax ? max : float.NaN);
                }
                binding.View.SetSize(settings.BaseSize);
                Vector3 anchor = binding.Unit.transform.position + Vector3.up * settings.WorldOffset;
                bool visible = outputCamera != null && outputCamera.isActiveAndEnabled;
                Vector3 viewport = visible ? outputCamera.WorldToViewportPoint(anchor) : default;
                visible = visible && viewport.z >= outputCamera.nearClipPlane && viewport.z <= outputCamera.farClipPlane &&
                    viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1;
                if (visible)
                {
                    Vector2 screen = outputCamera.WorldToScreenPoint(anchor);
                    visible = RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)healthCanvas.transform,
                        screen, null, out Vector2 local);
                    binding.View.Rect.anchoredPosition = local;
                    binding.View.Rect.localRotation = Quaternion.identity;
                    float distance = Vector3.Distance(outputCamera.transform.position, anchor);
                    binding.View.Rect.localScale = Vector3.one * settings.GetScale(distance);
                }
                binding.View.SetVisible(visible);
            }
        }

        private void Remove(Unit unit)
        {
            if (ReferenceEquals(unit, null) || !bindings.TryGetValue(unit, out Binding binding)) return;
            bindings.Remove(unit);
            binding.Attributes.AttributeChanged -= binding.AttributeChanged;
            binding.Unit.Died -= binding.Gone;
            binding.Unit.Destroyed -= binding.Gone;
            if (binding.View == null) return;
            binding.View.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(binding.View.gameObject); else DestroyImmediate(binding.View.gameObject);
        }

        private void ReleaseBindings()
        {
            if (subscribed && !ReferenceEquals(scene, null)) scene.Changed -= Refresh;
            subscribed = false;
            removed.Clear();
            removed.AddRange(bindings.Keys);
            foreach (Unit unit in removed) Remove(unit);
            removed.Clear();
            desired.Clear();
        }

        public void Shutdown()
        {
            ReleaseBindings();
            scene = null;
            outputCamera = null;
        }

        private void OnEnable() { if (scene != null && scene.IsInitialized) Subscribe(); }
        private void OnDisable() => ReleaseBindings();
        private void OnDestroy() => Shutdown();
    }
}
