using System.Collections.Generic;
using System;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using Wargame.Units;

namespace Wargame.Battle
{
    public sealed class BattleCameraDirector
    {
        private readonly BattleCameraRig rig;
        private readonly CinemachineCamera tacticalCamera;
        private readonly Func<int> directionProvider;
        private readonly List<UnitBattleCameraSet> unitCameras = new List<UnitBattleCameraSet>();
        private Unit focusedTarget;
        private Unit shotTarget;
        private Unit shooter;
        private CinemachineCamera shotCamera;
        private Camera outputCamera;
        private Transform fallbackAim;
        private BattleShotResultPopup resultPopup;

        public Unit FocusedTarget => focusedTarget;
        public bool IsShowingShot => shotCamera != null;
        public bool IsShotCameraReady => shotCamera != null && (rig == null || rig.IsCameraSettled(shotCamera));

        public BattleCameraDirector(BattleCameraRig cameraRig, IReadOnlyList<Unit> units)
            : this(cameraRig.TacticalCamera, units, () => cameraRig.ViewDirectionIndex)
        {
            rig = cameraRig;
            outputCamera = cameraRig.OutputCamera;
            rig.RotationCompleted += OnRotationCompleted;
        }

        public BattleCameraDirector(CinemachineCamera tactical, IReadOnlyList<Unit> units, Func<int> direction)
        {
            tacticalCamera = tactical;
            directionProvider = direction ?? (() => 0);
            tacticalCamera.Priority = 10;
            foreach (Unit unit in units)
            {
                UnitBattleCameraSet cameras = unit != null ? unit.GetComponent<UnitBattleCameraSet>() : null;
                if (cameras == null) continue;
                unitCameras.Add(cameras);
                foreach (CinemachineCamera camera in cameras.Cameras) camera.Priority = 0;
            }
        }

        public void RefreshDirection() => OnRotationCompleted();

        public void FocusTarget(Unit target)
        {
            if (shotCamera != null) return;
            focusedTarget = IsValidEnemy(target) ? target : null;
            RestoreFocus();
        }

        public bool BeginShot(Unit owner, Unit target)
        {
            if (owner == null || target == null || !owner.IsAlive || !target.IsAlive) return false;
            UnitBattleCameraSet set = owner.GetComponent<UnitBattleCameraSet>();
            UnitShootVisualController visual = owner.GetComponent<UnitShootVisualController>();
            if (set == null || set.ShotCamera == null) return false;
            shotTarget = target;
            shooter = owner;
            shotCamera = set.ShotCamera;
            var aim = new GameObject("ShotCameraAim");
            aim.hideFlags = HideFlags.DontSave;
            aim.transform.position = target.transform.position + Vector3.up * .7f;
            fallbackAim = aim.transform;
            CinemachineFollow follow = shotCamera.GetComponent<CinemachineFollow>();
            if (follow != null)
            {
                Vector3 behind = owner.transform.position - target.transform.position;
                behind.y = 0f;
                if (behind.sqrMagnitude < .0001f) behind = -owner.transform.forward;
                follow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
                follow.FollowOffset = behind.normalized * 3.2f + Vector3.up * 1.7f;
            }
            set.SetShotTarget(target.transform);
            Activate(shotCamera, 30);
            visual?.SetStartGate(() => IsShotCameraReady);
            return true;
        }

        public void EndShot(bool returnToShooterTactical = false)
        {
            if (resultPopup != null)
            {
                resultPopup.Cancel();
                resultPopup = null;
            }
            Unit previousShooter = shooter;
            if (shotCamera != null)
            {
                shotCamera.LookAt = null;
                shotCamera.Priority = 0;
            }
            if (previousShooter != null)
                previousShooter.GetComponent<UnitShootVisualController>()?.SetStartGate(null);
            if (returnToShooterTactical && previousShooter != null)
            {
                focusedTarget = null;
                rig?.FocusTacticalAt(previousShooter.transform.position);
            }
            shotCamera = null;
            shotTarget = null;
            shooter = null;
            if (fallbackAim != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(fallbackAim.gameObject);
                else UnityEngine.Object.DestroyImmediate(fallbackAim.gameObject);
                fallbackAim = null;
            }
            RestoreFocus();
        }

        public void ShowResult(ShootResult result)
        {
            if (shotCamera == null || outputCamera == null || string.IsNullOrEmpty(result.TargetName))
            { EndShot(); return; }
            if (resultPopup != null) resultPopup.Cancel();
            resultPopup = BattleShotResultPopup.Create(result.TargetName, BattleUiText.ShotOutcome(result),
                result.TargetPosition + Vector3.up * 1.5f, outputCamera, () =>
                {
                    resultPopup = null;
                    EndShot(true);
                });
        }

        public void UnitUnavailable(Unit unit)
        {
            if (unit == null) return;
            if (unit == focusedTarget) focusedTarget = null;
            if (unit == shooter) { EndShot(); return; }
            if (unit == shotTarget)
            {
                if (fallbackAim != null) fallbackAim.position = unit.transform.position + Vector3.up * .7f;
                if (shotCamera != null && fallbackAim != null) shotCamera.LookAt = fallbackAim;
                shotTarget = null;
            }
            if (shotCamera == null) RestoreFocus();
        }

        public void Dispose()
        {
            if (rig != null) rig.RotationCompleted -= OnRotationCompleted;
            EndShot();
            focusedTarget = null;
            RestoreFocus();
            unitCameras.Clear();
        }

        private void OnRotationCompleted() => RestoreFocus();

        private void RestoreFocus()
        {
            foreach (UnitBattleCameraSet set in unitCameras)
                if (set != null)
                    foreach (CinemachineCamera camera in set.Cameras)
                        camera.Priority = 0;
            tacticalCamera.Priority = 10;
            if (IsValidEnemy(focusedTarget) && focusedTarget.TryGetComponent(out UnitBattleCameraSet targetSet))
            {
                CinemachineCamera focus = targetSet.GetFocusCamera(directionProvider());
                if (focus != null) focus.Priority = 20;
                else focusedTarget = null;
            }
        }

        private void Activate(CinemachineCamera selected, int priority)
        {
            RestoreFocus();
            foreach (UnitBattleCameraSet set in unitCameras)
                if (set != null && set.ShotCamera != null) set.ShotCamera.Priority = 0;
            selected.Priority = priority;
        }

        private static bool IsValidEnemy(Unit unit) => unit != null && unit.IsAlive &&
            unit.isActiveAndEnabled && unit.Faction == Faction.Enemy;
    }
}
