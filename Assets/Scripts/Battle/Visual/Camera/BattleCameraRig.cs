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
        [SerializeField] private CinemachineBrain brain;
        [SerializeField] private CinemachineCamera virtualCamera;
        [SerializeField] private Transform centerAxis;
        [SerializeField, Min(.01f)] private float rotationDuration = .3f;
        [SerializeField, Range(.01f, .2f)] private float edgeBandFraction = .06f;
        [SerializeField, Min(0)] private float edgeSpeedInCells = 8;
        private GridManager grid;
        private BattlePlayerController playerController;
        private Tween rotation;
        private bool rotationFinishing;
        private float yaw, rotationGoal;
        private int viewDirectionIndex;
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
        public Camera OutputCamera => brain != null ? brain.OutputCamera : null;
        public CinemachineBrain Brain => brain;
        public CinemachineCamera TacticalCamera => virtualCamera;
        public int ViewDirectionIndex => viewDirectionIndex;
        public event Action ViewChanged;
        public event Action RotationCompleted;

        public bool IsCameraSettled(CinemachineCamera camera) => brain != null && camera != null &&
            !brain.IsBlending && brain.ActiveVirtualCamera == camera;

        public void FocusTacticalAt(Vector3 worldPosition)
        {
            if (!IsInitialized || centerAxis == null) return;
            CancelInteraction();
            centerAxis.position = ClampCenter(worldPosition);
        }

        public bool ValidateConfiguration(out string error)
        {
            error = null;
            if (brain == null || !brain.isActiveAndEnabled || OutputCamera == null ||
                virtualCamera == null || !virtualCamera.isActiveAndEnabled || centerAxis == null)
                error = "请在 Inspector 引用启用的 CinemachineBrain、CinemachineCamera 和中心节点。";
            else if (brain.UpdateMethod != CinemachineBrain.UpdateMethods.ManualUpdate || !brain.LensModeOverride.Enabled ||
                virtualCamera.Lens.ModeOverride != LensSettings.OverrideModes.Perspective)
                error = "请在 Inspector 将 Brain 设为 ManualUpdate、启用 Lens Mode Override，并将虚拟相机设为 Perspective。";
            else if (!ValidateFollow(out error)) return false;
            else if (!float.IsFinite(rotationDuration) || rotationDuration <= 0 ||
                !float.IsFinite(edgeBandFraction) || edgeBandFraction < .01f || edgeBandFraction > .2f ||
                !float.IsFinite(edgeSpeedInCells) || edgeSpeedInCells < 0)
                error = "旋转时间或边缘滚屏参数无效。";
            return error == null;
        }

        private bool ValidateFollow(out string error)
        {
            error = null;
            Transform target = virtualCamera.Follow;
            var follow = virtualCamera.GetComponent<CinemachineFollow>();
            var aim = virtualCamera.GetComponent<CinemachineRotateWithFollowTarget>();
            if (target == null || target.parent != centerAxis || target.localPosition.sqrMagnitude > .000001f ||
                centerAxis.lossyScale != Vector3.one || Vector3.Dot(centerAxis.up, Vector3.up) < .99999f ||
                Vector3.Dot(target.right, centerAxis.right) < .99999f || target.forward.y >= 0)
                error = "跟随目标应为中心节点下位置归零、仅设俯角的子节点；中心节点需保持世界竖直且缩放为 1。";
            else if (follow == null || !follow.enabled || aim == null || !aim.enabled ||
                follow.TrackerSettings.BindingMode != BindingMode.LockToTarget ||
                follow.TrackerSettings.AngularDampingMode != AngularDampingMode.Quaternion ||
                follow.TrackerSettings.PositionDamping != Vector3.zero || follow.TrackerSettings.QuaternionDamping != 0 || aim.Damping != 0 ||
                follow.FollowOffset.x != 0 || follow.FollowOffset.y != 0 || follow.FollowOffset.z >= 0)
                error = "请配置启用的 Follow（LockToTarget、Quaternion、阻尼为零、Offset 仅 Z 为负）和 RotateWithFollowTarget（阻尼为零）。";
            return error == null;
        }

        public void Initialize(GridManager board, Vector3 focus, BattlePlayerController controller)
        {
            if (IsInitialized) return;
            if (!ValidateConfiguration(out string error)) throw new InvalidOperationException(error);
            grid = board;
            playerController = controller;
            yaw = centerAxis.eulerAngles.y;
            centerAxis.position = ClampCenter(focus);
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
            if (!IsInitialized || OutputCamera == null || grid == null) return false;
            Ray ray = OutputCamera.ScreenPointToRay(screenPoint);
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
            if (blockedByUI || !OutputCamera.pixelRect.Contains(screenPoint)) panAnchored = false;
            else if (!panAnchored) panAnchored = TryGetPlanePoint(screenPoint, out panAnchor);
        }

        public void EndPan() { if (panActive) endPanAfterUpdate = true; }
        public void SetEdgeScrollPointer(Vector2? screenPoint) => edgePointer = screenPoint;
        public void CancelInteraction()
        {
            panActive = panAnchored = endPanAfterUpdate = false;
            edgePointer = null;
        }

        /// <summary>供 Inspector 的 Button.onClick 绑定，参数为 -1 或 +1。</summary>
        public void Rotate(int direction) => TryRotate(direction);

        public bool TryRotate(int direction)
        {
            if ((direction != -1 && direction != 1) || !IsInitialized || !isActiveAndEnabled || IsRotating ||
                panActive || (playerController != null && playerController.HasActiveGesture)) return false;
            centerAxis.position = ClampCenter(ViewCenter);
            rotationGoal = yaw + direction * 90;
            rotation = DOTween.To(() => yaw, value =>
                { yaw = value; centerAxis.rotation = Quaternion.Euler(0, yaw, 0); }, rotationGoal, rotationDuration)
                .SetEase(Ease.InOutSine).SetUpdate(true).SetLink(gameObject)
            .OnComplete(() =>
            {
                yaw = Mathf.Repeat(rotationGoal, 360);
                centerAxis.rotation = Quaternion.Euler(0, yaw, 0);
                viewDirectionIndex = (viewDirectionIndex + direction + 4) % 4;
                rotation = null;
                rotationFinishing = true;
                RotationCompleted?.Invoke();
            });
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
            Rect rect = OutputCamera.pixelRect;
            if (!rect.Contains(pointer)) return Vector3.zero;
            float band = Mathf.Min(rect.width, rect.height) * edgeBandFraction;
            Vector2 direction = new Vector2(
                Mathf.Clamp01((pointer.x - rect.xMax + band) / band) - Mathf.Clamp01((rect.xMin + band - pointer.x) / band),
                Mathf.Clamp01((pointer.y - rect.yMax + band) / band) - Mathf.Clamp01((rect.yMin + band - pointer.y) / band));
            direction = Vector2.ClampMagnitude(direction, 1);
            Vector3 right = Vector3.ProjectOnPlane(OutputCamera.transform.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(OutputCamera.transform.forward, Vector3.up).normalized;
            return (right * direction.x + forward * direction.y) * (edgeSpeedInCells * grid.CellSize);
        }

        private void UpdateView(bool force)
        {
            brain.ManualUpdate();
            // 编辑器缩放时 pixelRect 可含小数像素，ViewportPointToRay(0.5) 会有舍入偏移。
            // 无镜头偏移的透视相机，其光轴就是精确的视野中心射线。
            var centerRay = new Ray(OutputCamera.transform.position, OutputCamera.transform.forward);
            if (new Plane(Vector3.up, grid.Origin).Raycast(centerRay, out float distance))
            {
                Vector3 center = centerRay.GetPoint(distance);
                center.y = grid.Origin.y;
                ViewCenter = center;
            }
            Vector2Int resolution = new Vector2Int(Screen.width, Screen.height);
            Transform view = OutputCamera.transform;
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
