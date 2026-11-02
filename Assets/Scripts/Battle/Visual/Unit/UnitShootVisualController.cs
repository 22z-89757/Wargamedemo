using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Units
{
    public interface IUnitShootVisual
    {
        bool IsConfigured { get; }
        bool IsPlaying { get; }
        void SetStartGate(Func<bool> canStart);
        void PlayShot(GridManager grid, Unit target, Vector2Int logicalOrigin, Vector2Int firingOrigin,
            Action onProjectileArrived, Action<bool> onCompleted);
        void StopShot(bool notify = true);
    }

    [DisallowMultipleComponent, RequireComponent(typeof(Unit))]
    public sealed class UnitShootVisualController : MonoBehaviour, IUnitShootVisual
    {
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Transform muzzle;
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField, Min(0.1f)] private float peekCellsPerSecond = 5f;
        [SerializeField, Min(0.1f)] private float projectileCellsPerSecond = 12f;
        [SerializeField, Min(0.01f)] private float turnDuration = 0.12f;
        [SerializeField, Min(0f)] private float targetHeight = 0.7f;
        private Sequence sequence;
        private GameObject projectile;
        private Vector3 restPosition;
        private Action<bool> completion;
        private Action projectileArrived;
        private Func<bool> startGate;
        private Coroutine startRoutine;
        private GridManager pendingGrid;
        private Unit pendingTarget;
        private Vector2Int pendingLogicalOrigin, pendingFiringOrigin;

        public bool IsConfigured => modelRoot != null && muzzle != null && projectilePrefab != null;
        public bool IsPlaying => completion != null;
        public bool IsWaitingToStart => completion != null && sequence == null;

        public void Configure(Transform model, Transform shotMuzzle, GameObject shotProjectile)
        { modelRoot = model; muzzle = shotMuzzle; projectilePrefab = shotProjectile; }

        public void SetStartGate(Func<bool> canStart) => startGate = canStart;

        public bool ValidateConfiguration(out string error)
        {
            if (modelRoot == null) error = "缺少射击旋转用 modelRoot。";
            else if (muzzle == null) error = "缺少射击 muzzle。";
            else if (projectilePrefab == null) error = "缺少子弹预制体。";
            else { error = null; return true; }
            return false;
        }

        public void PlayShot(GridManager grid, Unit target, Vector2Int logicalOrigin, Vector2Int firingOrigin,
            Action onProjectileArrived, Action<bool> onCompleted)
        {
            StopShot(true);
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!ValidateConfiguration(out string error)) throw new InvalidOperationException(error);
            projectileArrived = onProjectileArrived ?? throw new ArgumentNullException(nameof(onProjectileArrived));
            completion = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            restPosition = transform.position;
            pendingGrid = grid;
            pendingTarget = target;
            pendingLogicalOrigin = logicalOrigin;
            pendingFiringOrigin = firingOrigin;
            if (startGate != null)
            {
                startRoutine = StartCoroutine(WaitForStartGate());
                return;
            }
            BeginVisualSequence();
        }

        private IEnumerator WaitForStartGate()
        {
            yield return null;
            while (completion != null && pendingTarget != null && pendingTarget.IsAlive &&
                   startGate != null && !startGate())
                yield return null;
            startRoutine = null;
            if (completion == null) yield break;
            if (pendingTarget == null || !pendingTarget.IsAlive)
            {
                Finish(true);
                yield break;
            }
            BeginVisualSequence();
        }

        private void BeginVisualSequence()
        {
            GridManager grid = pendingGrid;
            Unit target = pendingTarget;
            Vector2Int logicalOrigin = pendingLogicalOrigin;
            Vector2Int firingOrigin = pendingFiringOrigin;
            startGate = null;
            Vector3 firingPosition = grid.GridToWorld(firingOrigin);
            Vector3 targetPosition = target.transform.position + Vector3.up * targetHeight;
            float peekSeconds = Vector3.Distance(restPosition, firingPosition) /
                (grid.CellSize * Mathf.Max(0.1f, peekCellsPerSecond));

            projectile = Instantiate(projectilePrefab, muzzle.position, Quaternion.identity);
            projectile.name = projectilePrefab.name;
            projectile.SetActive(false);
            float projectileSeconds = Vector3.Distance(muzzle.position, targetPosition) /
                (grid.CellSize * Mathf.Max(0.1f, projectileCellsPerSecond));

            sequence = DOTween.Sequence().SetLink(gameObject);
            if (firingOrigin != logicalOrigin)
                sequence.Append(transform.DOMove(firingPosition, peekSeconds).SetEase(Ease.InOutSine));
            Vector3 direction = targetPosition - firingPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.000001f)
                sequence.Append(modelRoot.DORotateQuaternion(Quaternion.LookRotation(direction), turnDuration)
                    .SetEase(Ease.OutSine));
            sequence.AppendCallback(() =>
            {
                if (projectile == null) return;
                projectile.transform.position = muzzle.position;
                projectile.SetActive(true);
            });
            sequence.Append(projectile.transform.DOMove(targetPosition, Mathf.Max(0.01f, projectileSeconds))
                .SetEase(Ease.Linear));
            sequence.AppendCallback(NotifyProjectileArrived);
            if (firingOrigin != logicalOrigin)
                sequence.Append(transform.DOMove(restPosition, peekSeconds).SetEase(Ease.InOutSine));
            sequence.OnComplete(() => Finish(false));
        }

        public void StopShot(bool notify = true)
        { if (completion != null) Finish(true, notify); }

        private void NotifyProjectileArrived()
        {
            Action callback = projectileArrived;
            projectileArrived = null;
            callback?.Invoke();
            DestroyProjectile();
        }

        private void Finish(bool cancelled, bool notify = true)
        {
            Action<bool> callback = completion;
            completion = null;
            projectileArrived = null;
            if (startRoutine != null)
            {
                StopCoroutine(startRoutine);
                startRoutine = null;
            }
            startGate = null;
            pendingGrid = null;
            pendingTarget = null;
            sequence?.Kill();
            sequence = null;
            transform.position = restPosition;
            DestroyProjectile();
            if (notify) callback?.Invoke(cancelled);
        }

        private void DestroyProjectile()
        {
            if (projectile == null) return;
            if (Application.isPlaying) Destroy(projectile); else DestroyImmediate(projectile);
            projectile = null;
        }

        private void OnDisable() => StopShot();
        private void OnDestroy() => StopShot();
    }
}
