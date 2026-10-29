using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Battle
{
    [RequireComponent(typeof(Camera))]
    public sealed class BattleCameraController : MonoBehaviour
    {
        [SerializeField] private GridManager grid;
        private Camera view;
        private float lastAspect;
        public void Configure(GridManager value) { grid = value; Fit(); }
        private void Start() => Fit();
        private void Update() { if (view != null && !Mathf.Approximately(lastAspect, view.aspect)) Fit(); }
        private void Fit()
        {
            if (grid == null) return;
            view = GetComponent<Camera>();
            view.orthographic = true;
            transform.rotation = Quaternion.Euler(60, 0, 0);
            Vector3 center = grid.Origin + new Vector3(grid.Width, 0, grid.Height) * grid.CellSize * .5f;
            transform.position = center - transform.forward * Mathf.Max(grid.Width, grid.Height) * grid.CellSize * 1.5f;
            lastAspect = Mathf.Max(.1f, view.aspect);
            view.orthographicSize = Mathf.Max(grid.Height * grid.CellSize * Mathf.Sin(60 * Mathf.Deg2Rad) * .5f,
                grid.Width * grid.CellSize * .5f / lastAspect) * 1.5f;
            view.nearClipPlane = .1f;
            view.farClipPlane = Mathf.Max(100, grid.CellSize * (grid.Width + grid.Height) * 4);
        }
    }
}
