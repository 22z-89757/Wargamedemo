using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Wargame.Battle
{
    /// <summary>把鼠标左键和主触点转换成相同的指针手势；不处理棋盘或能力。</summary>
    public sealed class BattleInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;
        private InputActionAsset runtimeActions;
        private InputAction point, press;
        private InputDevice capturedDevice;
        private int capturedTouchId;
        private readonly Queue<PointerEvent> pending = new Queue<PointerEvent>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        public bool ValidateConfiguration(out string error)
        {
            var map = actions != null ? actions.FindActionMap("Battle") : null;
            error = map == null || map.FindAction("Point") == null || map.FindAction("Press") == null
                ? "需要包含 Battle/Point 与 Battle/Press 的 InputActionAsset。" : null;
            return error == null;
        }
        public event Action<Vector2> Pressed, Dragged, Released;
        public event Action Cancelled;
        public bool HasCapturedPointer => capturedDevice != null;
        private readonly struct PointerEvent
        {
            public readonly int Kind;
            public readonly Vector2 Position;
            public PointerEvent(int kind, Vector2 position) { Kind = kind; Position = position; }
        }

        public void Configure(InputActionAsset asset)
        {
            DisableActions();
            actions = asset;
            if (isActiveAndEnabled) EnableActions();
        }

        private void OnEnable() => EnableActions();
        private void EnableActions()
        {
            if (runtimeActions != null || !ValidateConfiguration(out _)) return;
            runtimeActions = Instantiate(actions);
            var map = runtimeActions.FindActionMap("Battle", true);
            point = map.FindAction("Point", true);
            press = map.FindAction("Press", true);
            point.performed += OnPoint;
            press.performed += OnPress;
            InputSystem.onDeviceChange += OnDeviceChange;
            InputSystem.onAfterUpdate += CheckCapturedTouch;
            map.Enable();
        }

        private static Vector2 Position(InputDevice device)
        {
            if (device is Mouse mouse) return mouse.position.ReadValue();
            if (device is Touchscreen touch) return touch.primaryTouch.position.ReadValue();
            return default;
        }

        private void OnPress(InputAction.CallbackContext context)
        {
            var device = context.control.device;
            bool down = context.ReadValueAsButton();
            if (down)
            {
                if (capturedDevice != null || (!(device is Mouse) && !(device is Touchscreen))) return;
                capturedDevice = device;
                capturedTouchId = device is Touchscreen touch ? touch.primaryTouch.touchId.ReadValue() : 0;
                pending.Enqueue(new PointerEvent(0, Position(device)));
            }
            else if (capturedDevice == device)
            {
                bool cancelled = device is Touchscreen touch &&
                    touch.primaryTouch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled;
                pending.Enqueue(new PointerEvent(cancelled ? 3 : 2, Position(device)));
                capturedDevice = null;
            }
        }

        private void OnPoint(InputAction.CallbackContext context)
        {
            if (capturedDevice == null || context.control.device != capturedDevice) return;
            if (capturedDevice is Touchscreen touch && touch.primaryTouch.touchId.ReadValue() != capturedTouchId)
            { CancelGesture(); return; }
            pending.Enqueue(new PointerEvent(1, context.ReadValue<Vector2>()));
        }

        private void CheckCapturedTouch()
        {
            // primaryTouch 的合成 press 可能等待所有手指抬起，实际手势只等待捕获的那根手指。
            if (!(capturedDevice is Touchscreen screen)) return;
            foreach (var contact in screen.touches)
            {
                if (contact.touchId.ReadValue() != capturedTouchId) continue;
                var phase = contact.phase.ReadValue();
                if (phase != UnityEngine.InputSystem.TouchPhase.Ended && phase != UnityEngine.InputSystem.TouchPhase.Canceled) return;
                pending.Enqueue(new PointerEvent(phase == UnityEngine.InputSystem.TouchPhase.Canceled ? 3 : 2, contact.position.ReadValue()));
                capturedDevice = null;
                return;
            }
        }

        private void Update()
        {
            // UI 命中以本次屏幕坐标主动 Raycast，不依赖 UI 模块更新先后。
            while (pending.Count > 0)
            {
                PointerEvent item = pending.Dequeue();
                switch (item.Kind)
                {
                    case 0: Pressed?.Invoke(item.Position); break;
                    case 1: Dragged?.Invoke(item.Position); break;
                    case 2: Released?.Invoke(item.Position); break;
                    case 3: Cancelled?.Invoke(); break;
                }
            }
        }

        public bool IsOverUI(Vector2 position)
        {
            if (EventSystem.current == null) return false;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
            foreach (RaycastResult hit in uiHits)
                if (hit.module is UnityEngine.UI.GraphicRaycaster) return true;
            return false;
        }

        public void CancelGesture()
        {
            capturedDevice = null;
            pending.Clear();
            Cancelled?.Invoke();
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device == capturedDevice && (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected))
                CancelGesture();
        }

        private void OnApplicationFocus(bool focused) { if (!focused) CancelGesture(); }
        private void OnApplicationPause(bool paused) { if (paused) CancelGesture(); }
        private void OnDisable() => DisableActions();
        private void DisableActions()
        {
            CancelGesture();
            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onAfterUpdate -= CheckCapturedTouch;
            if (runtimeActions == null) return;
            point.performed -= OnPoint;
            press.performed -= OnPress;
            runtimeActions.Disable();
            if (Application.isPlaying) Destroy(runtimeActions); else DestroyImmediate(runtimeActions);
            runtimeActions = null;
        }
    }
}
