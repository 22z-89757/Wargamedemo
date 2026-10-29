using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Units
{
    /// <summary>只负责 Transform 和模型表现；费用、路径与占用由 MoveAbility 管理。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Unit))]
    public sealed class UnitVisualController : MonoBehaviour
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
        public bool IsMoving => completion != null;

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
                selectionRing.widthMultiplier = cellSize * 0.045f;
                for (int i = 0; i < 48; i++)
                {
                    float angle = i * Mathf.PI * 2 / 48;
                    selectionRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * cellSize * 0.43f, 0.045f,
                        Mathf.Sin(angle) * cellSize * 0.43f));
                }
            }
            selectionPulse?.Kill();
            if (selectionRing == null) return;
            selectionRing.gameObject.SetActive(selected);
            selectionRing.transform.localScale = Vector3.one;
            if (selected)
                selectionPulse = selectionRing.transform.DOScale(1.08f, 0.65f).SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine).SetLink(gameObject);
        }

        public void PlayMovement(GridManager grid, IReadOnlyList<Vector2Int> path, Action<bool> onCompleted)
        {
            StopMovement(true);
            if (path == null || path.Count < 2) throw new ArgumentException("移动路径必须包含起终点。", nameof(path));
            destination = grid.GridToWorld(path[path.Count - 1]);
            completion = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            transform.position = grid.GridToWorld(path[0]);
            if (modelRoot != null)
            {
                modelRestPosition = modelRoot.localPosition;
                bob = modelRoot.DOLocalMoveY(modelRestPosition.y + bobHeight * grid.CellSize, 0.14f)
                    .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(gameObject);
            }
            movement = DOTween.Sequence().SetLink(gameObject);
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 from = grid.GridToWorld(path[i - 1]);
                Vector3 to = grid.GridToWorld(path[i]);
                float seconds = Vector3.Distance(from, to) / (grid.CellSize * Mathf.Max(0.1f, cellsPerSecond));
                movement.Append(transform.DOMove(to, seconds).SetEase(Ease.Linear));
                if (modelRoot != null)
                    movement.Join(modelRoot.DORotateQuaternion(Quaternion.LookRotation(to - from), Mathf.Min(seconds, turnDuration))
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
