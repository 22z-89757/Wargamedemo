using System;
using DG.Tweening;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Battle
{
    /// <summary>棋盘相机中心与手势运动；实际镜头由 Cinemachine 输出。</summary>
    [DefaultExecutionOrder(-100), DisallowMultipleComponent]
    public sealed class BattleCameraRig : MonoBehaviour
    {
        [SerializeField] private Camera outputCamera;
        [SerializeField] private CinemachineCamera virtualCamera;
        [SerializeField] private Transform centerAxis;
        [SerializeField] private Transform pitchTarget;
        [SerializeField, Range(15, 90)] private float fieldOfView = 45;
        [SerializeField, Range(30, 80)] private float pitch = 55;
        [SerializeField] private float initialYaw = 45;
        [SerializeField, Min(1)] private float heightInCells = 12;
        [SerializeField, Min(.01f)] private float rotationDuration = .3f;
        [SerializeField, Range(.01f, .2f)] private float edgeBandFraction = .06f;
        [SerializeField, Min(0)] private float edgeSpeedInCells = 8;
        private GridManager grid;
        private CinemachineBrain brain;
        private BattlePlayerController playerController;
        private Tween rotation;
        private bool rotationFinishing;
        private float yaw, rotationGoal;
        private bool panActive, panAnchored, endPanAfterUpdate;
        private Vector2 panPointer;
        private Vector3 panAnchor;
        private Vector2? edgePointer;
        private Vector3 lastPosition;
        private Quaternion lastRotation;
        private Vector2Int lastResolution;
        public bool IsInitialized { get; private set; }
        public bool IsRotating => rotationFinishing || (rotation != null && rotation.IsActive());
        public Vector3 ViewCenter { get; private set; }
        public Camera OutputCamera => outputCamera;
        public event Action ViewChanged;

        public bool ValidateConfiguration(out string error)
        {
            error = null;
            if (outputCamera == null || !outputCamera.TryGetComponent(out CinemachineBrain configuredBrain) ||
                !configuredBrain.isActiveAndEnabled || virtualCamera == null || !virtualCamera.isActiveAndEnabled ||
                virtualCamera.GetComponent<CinemachineFollow>() == null ||
                virtualCamera.GetComponent<CinemachineRotateWithFollowTarget>() == null ||
                centerAxis == null || pitchTarget == null || pitchTarget.parent != centerAxis ||
                centerAxis.lossyScale != Vector3.one)
                error = "相机需要启用的 CinemachineBrain、CinemachineCamera、Follow、RotateWithFollowTarget 和中心/俯角节点。";
            else if (!float.IsFinite(initialYaw) || !float.IsFinite(heightInCells) || heightInCells < 1 ||
                !float.IsFinite(fieldOfView) || fieldOfView < 15 || fieldOfView > 90 ||
                !float.IsFinite(pitch) || pitch < 30 || pitch > 80 || !float.IsFinite(rotationDuration) || rotationDuration <= 0 ||
                !float.IsFinite(edgeBandFraction) || edgeBandFraction < .01f || edgeBandFraction > .2f ||
                !float.IsFinite(edgeSpeedInCells) || edgeSpeedInCells < 0)
                error = "相机视野、俯角、高度、旋转时间或边缘滚屏参数无效。";
            return error == null;
        }

        public void Initialize(GridManager board, Vector3 focus, BattlePlayerController controller)
        {
            if (IsInitialized) return;
            if (!ValidateConfiguration(out string error)) throw new InvalidOperationException(error);
            grid = board;
            playerController = controller;
            brain = outputCamera.GetComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            brain.IgnoreTimeScale = true;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0);
            brain.LensModeOverride = new CinemachineBrain.LensModeOverrideSettings
                { Enabled = true, DefaultMode = LensSettings.OverrideModes.Perspective };
            yaw = Mathf.Repeat(initialYaw, 360);
            centerAxis.SetPositionAndRotation(ClampCenter(focus), Quaternion.Euler(0, yaw, 0));
            pitchTarget.localPosition = Vector3.zero;
            pitchTarget.localRotation = Quaternion.Euler(pitch, 0, 0);
            virtualCamera.Follow = pitchTarget;
            virtualCamera.LookAt = null;
            var follow = virtualCamera.GetComponent<CinemachineFollow>();
            // 使用四元数跟随固定俯角，避免欧拉增量在小角度处被截断。
            follow.TrackerSettings = new TrackerSettings
                { BindingMode = BindingMode.LockToTarget, AngularDampingMode = AngularDampingMode.Quaternion };
            follow.FollowOffset = Vector3.back * (heightInCells * grid.CellSize / Mathf.Sin(pitch * Mathf.Deg2Rad));
            virtualCamera.GetComponent<CinemachineRotateWithFollowTarget>().Damping = 0;
            virtualCamera.Lens.FieldOfView = fieldOfView;
            virtualCamera.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            virtualCamera.Lens.NearClipPlane = .1f;
            virtualCamera.Lens.FarClipPlane = Mathf.Max(200, (grid.Width + grid.Height) * grid.CellSize * 2);
            outputCamera.orthographic = false;
            virtualCamera.PreviousStateIsValid = false;
            IsInitialized = true;
            UpdateView(true);
        }

        private Vector3 ClampCenter(Vector3 point)
        {
            Vector3 min = grid.Origin;
            float inset = grid.CellSize * .001f;
            return new Vector3(Mathf.Clamp(point.x, min.x + inset, min.x + grid.Width * grid.CellSize - inset), min.y,
                Mathf.Clamp(point.z, min.z + inset, min.z + grid.Height * grid.CellSize - inset));
        }

        public bool TryGetPlanePoint(Vector2 screenPoint, out Vector3 point)
        {
            point = default;
            if (!IsInitialized || outputCamera == null || grid == null) return false;
            Ray ray = outputCamera.ScreenPointToRay(screenPoint);
            if (!new Plane(Vector3.up, grid.Origin).Raycast(ray, out float distance)) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        public bool BeginPan(Vector2 screenPoint)
        {
            if (!IsInitialized || !isActiveAndEnabled || IsRotating) return false;
            panActive = true;
            panPointer = screenPoint;
            panAnchored = TryGetPlanePoint(screenPoint, out panAnchor);
            return true;
        }

        public void DragPan(Vector2 screenPoint, bool blockedByUI)
        {
            if (!panActive) return;
            panPointer = screenPoint;
            if (blockedByUI || !outputCamera.pixelRect.Contains(screenPoint)) panAnchored = false;
            else if (!panAnchored) panAnchored = TryGetPlanePoint(screenPoint, out panAnchor);
        }

        public void EndPan() { if (panActive) endPanAfterUpdate = true; }
        public void SetEdgeScrollPointer(Vector2? screenPoint) => edgePointer = screenPoint;
        public void CancelInteraction()
        {
            panActive = panAnchored = endPanAfterUpdate = false;
            edgePointer = null;
        }

        public bool TryRotate(int direction)
        {
            if ((direction != -1 && direction != 1) || !IsInitialized || !isActiveAndEnabled || IsRotating ||
                panActive || (playerController != null && playerController.HasActiveGesture)) return false;
            centerAxis.position = ClampCenter(ViewCenter);
            rotationGoal = yaw + direction * 90;
            rotation = DOTween.To(() => yaw, value =>
                { yaw = value; centerAxis.rotation = Quaternion.Euler(0, yaw, 0); }, rotationGoal, rotationDuration)
                .SetEase(Ease.InOutSine).SetUpdate(true).SetLink(gameObject)
                .OnComplete(() => { yaw = Mathf.Repeat(rotationGoal, 360); centerAxis.rotation = Quaternion.Euler(0, yaw, 0); rotation = null; rotationFinishing = true; });
            ViewChanged?.Invoke();
            return true;
        }

        private void LateUpdate()
        {
            if (!IsInitialized) return;
            bool reanchor = false;
            if (!IsRotating)
            {
                Vector3 desired = centerAxis.position;
                if (panActive && panAnchored && TryGetPlanePoint(panPointer, out Vector3 current)) desired += panAnchor - current;
                else if (!panActive && edgePointer.HasValue)
                    desired += EdgeVelocity(edgePointer.Value) * Time.unscaledDeltaTime;
                Vector3 clamped = ClampCenter(desired);
                reanchor = (clamped - desired).sqrMagnitude > .000001f;
                centerAxis.position = clamped;
            }
            UpdateView(rotationFinishing);
            rotationFinishing = false;
            if (reanchor && panActive) panAnchored = TryGetPlanePoint(panPointer, out panAnchor);
            if (endPanAfterUpdate) panActive = panAnchored = endPanAfterUpdate = false;
        }

        private Vector3 EdgeVelocity(Vector2 pointer)
        {
            Rect rect = outputCamera.pixelRect;
            if (!rect.Contains(pointer)) return Vector3.zero;
            float band = Mathf.Min(rect.width, rect.height) * edgeBandFraction;
            Vector2 direction = new Vector2(
                Mathf.Clamp01((pointer.x - rect.xMax + band) / band) - Mathf.Clamp01((rect.xMin + band - pointer.x) / band),
                Mathf.Clamp01((pointer.y - rect.yMax + band) / band) - Mathf.Clamp01((rect.yMin + band - pointer.y) / band));
            direction = Vector2.ClampMagnitude(direction, 1);
            Vector3 right = Vector3.ProjectOnPlane(outputCamera.transform.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(outputCamera.transform.forward, Vector3.up).normalized;
            return (right * direction.x + forward * direction.y) * (edgeSpeedInCells * grid.CellSize);
        }

        private void UpdateView(bool force)
        {
            brain.ManualUpdate();
            // 编辑器缩放时 pixelRect 可含小数像素，ViewportPointToRay(0.5) 会有舍入偏移。
            // 无镜头偏移的透视相机，其光轴就是精确的视野中心射线。
            var centerRay = new Ray(outputCamera.transform.position, outputCamera.transform.forward);
            if (new Plane(Vector3.up, grid.Origin).Raycast(centerRay, out float distance))
            {
                Vector3 center = centerRay.GetPoint(distance);
                center.y = grid.Origin.y;
                ViewCenter = center;
            }
            Vector2Int resolution = new Vector2Int(Screen.width, Screen.height);
            Transform view = outputCamera.transform;
            if (force || (view.position - lastPosition).sqrMagnitude > .0000001f ||
                Quaternion.Angle(view.rotation, lastRotation) > .001f || resolution != lastResolution)
            {
                lastPosition = view.position; lastRotation = view.rotation; lastResolution = resolution;
                ViewChanged?.Invoke();
            }
        }

        public void Shutdown()
        {
            CancelInteraction();
            StopRotation();
            IsInitialized = false;
            playerController = null;
        }
        private void StopRotation()
        {
            if (IsRotating)
            {
                rotation?.Kill();
                yaw = Mathf.Repeat(rotationGoal, 360);
                if (centerAxis != null) centerAxis.rotation = Quaternion.Euler(0, yaw, 0);
            }
            rotation = null;
            rotationFinishing = false;
        }
        private void OnDisable() { CancelInteraction(); StopRotation(); }
        private void OnDestroy() => Shutdown();
        private void OnDrawGizmosSelected()
        {
            if (centerAxis == null) return;
            Gizmos.color = Color.cyan;
            Vector3 center = IsInitialized ? ViewCenter : centerAxis.position;
            Gizmos.DrawLine(center, center + Vector3.up * 5);
            Gizmos.DrawWireSphere(center, .15f);
        }
    }
}
