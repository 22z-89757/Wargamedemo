using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Units
{
    public interface IUnitMovementVisual
    {
        bool IsConfigured { get; }
        bool IsMoving { get; }
        void PlayMovement(GridManager grid, Vector2Int start, Vector2Int target,
            Action<bool> onCompleted);
        void StopMovement(bool notify = true);
    }

    /// <summary>只负责 Transform 和模型表现；费用、路径与占用由 MoveAbility 管理。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Unit))]
    public sealed class UnitMovementVisual : MonoBehaviour, IUnitMovementVisual
    {
        [SerializeField] private Transform modelRoot;
        [SerializeField, Min(0.1f)] private float cellsPerSecond = 4f;
        [SerializeField, Min(0.01f)] private float turnDuration = 0.12f;
        [SerializeField, Min(0)] private float bobHeight = 0.045f;
        [SerializeField] private Material markerMaterial;
        private Sequence movement;
        private Tween bob, selectionPulse;
        private LineRenderer selectionRing;
        private Vector3 modelRestPosition, destination;
        private Action<bool> completion;
        private INavigationPathProvider pathProvider;
        public bool IsConfigured => isActiveAndEnabled;
        public bool IsMoving => completion != null;

        internal INavigationPathProvider PathProvider
        {
            get => pathProvider ??= new NavigationPathProvider(new UnityNavigationQuery());
            set => pathProvider = value ?? throw new ArgumentNullException(nameof(value));
        }

        public void Configure(Transform model, Material marker)
        {
            modelRoot = model;
            markerMaterial = marker;
        }

        public void SetSelected(bool selected, float cellSize = 1f)
        {
            if (selectionRing == null && selected)
            {
                var ring = new GameObject("SelectionRing");
                ring.transform.SetParent(transform, false);
                selectionRing = ring.AddComponent<LineRenderer>();
                selectionRing.useWorldSpace = false;
                selectionRing.loop = true;
                selectionRing.positionCount = 48;
                selectionRing.sharedMaterial = markerMaterial;
                selectionRing.startColor = selectionRing.endColor = new Color(0.25f, 0.95f, 1f, 0.95f);
                selectionRing.widthMultiplier = cellSize * 0.06f;
                for (int i = 0; i < 48; i++)
                {
                    float angle = i * Mathf.PI * 2 / 48;
                    selectionRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * cellSize * 0.6f, 0.045f,
                        Mathf.Sin(angle) * cellSize * 0.6f));
                }
            }
            selectionPulse?.Kill();
            if (selectionRing == null) return;
            selectionRing.gameObject.SetActive(selected);
            selectionRing.transform.localScale = Vector3.one;
            if (selected)
                selectionPulse = selectionRing.transform.DOScale(1.2f, 0.65f).SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine).SetLink(gameObject);
        }

        public void PlayMovement(GridManager grid, Vector2Int start, Vector2Int target, Action<bool> onCompleted)
        {
            StopMovement(true);
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            destination = grid.GridToWorld(target);
            completion = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            Vector3 startWorld = grid.GridToWorld(start);
            transform.position = startWorld;
            if (modelRoot != null) modelRestPosition = modelRoot.localPosition;
            if (!PathProvider.TryCalculate(startWorld, destination, Mathf.Max(0.1f, grid.CellSize),
                    out IReadOnlyList<Vector3> corners, out string error))
            {
                Debug.LogError($"{name} 移动视觉导航失败：{error}", this);
                Finish(false);
                return;
            }
            if (modelRoot != null)
            {
                bob = modelRoot.DOLocalMoveY(modelRestPosition.y + bobHeight * grid.CellSize, 0.14f)
                    .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(gameObject);
            }
            movement = DOTween.Sequence().SetLink(gameObject);
            for (int i = 1; i < corners.Count; i++)
            {
                Vector3 from = corners[i - 1];
                Vector3 to = corners[i];
                float seconds = Vector3.Distance(from, to) / (grid.CellSize * Mathf.Max(0.1f, cellsPerSecond));
                movement.Append(transform.DOMove(to, seconds).SetEase(Ease.Linear));
                Vector3 direction = to - from;
                direction.y = 0f;
                if (modelRoot != null && direction.sqrMagnitude > 0.000001f)
                    movement.Join(modelRoot.DORotateQuaternion(Quaternion.LookRotation(direction), Mathf.Min(seconds, turnDuration))
                        .SetEase(Ease.OutSine));
            }
            movement.OnComplete(() => Finish(false));
        }

        /// <summary>停止表现并对齐已提交的终点；可选择是否通知能力取消。</summary>
        public void StopMovement(bool notify = true)
        {
            if (completion != null) Finish(true, notify);
        }

        private void Finish(bool cancelled, bool notify = true)
        {
            var callback = completion;
            completion = null;
            movement?.Kill();
            movement = null;
            bob?.Kill();
            bob = null;
            transform.position = destination;
            if (modelRoot != null) modelRoot.localPosition = modelRestPosition;
            if (notify) callback?.Invoke(cancelled);
        }

        private void OnDisable()
        {
            StopMovement();
            selectionPulse?.Kill();
            if (selectionRing != null) selectionRing.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            StopMovement();
            selectionPulse?.Kill();
        }
    }
}
