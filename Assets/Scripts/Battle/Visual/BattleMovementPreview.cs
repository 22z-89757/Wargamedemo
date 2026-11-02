using System.Collections.Generic;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Battle
{
    /// <summary>复用范围格和路径渲染，不创建格子碰撞体。</summary>
    public sealed class BattleMovementPreview : MonoBehaviour
    {
        [SerializeField] private Material overlayMaterial;
        private readonly List<MeshRenderer> tiles = new List<MeshRenderer>();
        private readonly List<MeshRenderer> terrain = new List<MeshRenderer>();
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private LineRenderer pathLine, targetLine;
        private GridManager grid;
        private MaterialPropertyBlock properties;
        public static readonly Color WalkColor = new Color(0.1f, 0.6f, 1f, 0.3f);
        public static readonly Color DashColor = new Color(1f, 0.76f, 0.12f, 0.3f);

        public void Configure(Material material) => overlayMaterial = material;

        public void Initialize(GridManager value)
        {
            if (grid != null) return;
            properties = new MaterialPropertyBlock();
            grid = value;
            pathLine = CreateLine("MovePath", 0.065f * grid.CellSize);
            targetLine = CreateLine("MoveTarget", 0.06f * grid.CellSize);
            targetLine.loop = true;
            var vertices = new List<Vector3>();
            for (int x = 0; x <= grid.Width; x++)
            {
                vertices.Add(grid.Origin + new Vector3(x * grid.CellSize, 0.018f, 0));
                vertices.Add(grid.Origin + new Vector3(x * grid.CellSize, 0.018f, grid.Height * grid.CellSize));
            }
            for (int y = 0; y <= grid.Height; y++)
            {
                vertices.Add(grid.Origin + new Vector3(0, 0.018f, y * grid.CellSize));
                vertices.Add(grid.Origin + new Vector3(grid.Width * grid.CellSize, 0.018f, y * grid.CellSize));
            }
            var indices = new int[vertices.Count];
            var colors = new Color[vertices.Count];
            for (int i = 0; i < indices.Length; i++) { indices[i] = i; colors[i] = new Color(0.22f, 0.32f, 0.4f, 0.45f); }
            var mesh = new Mesh { name = "BattleGridLines" };
            mesh.SetVertices(vertices);
            mesh.colors = colors;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            ownedMeshes.Add(mesh);
            var lines = new GameObject("BoardGrid", typeof(MeshFilter), typeof(MeshRenderer));
            lines.transform.SetParent(transform, false);
            lines.transform.position = Vector3.zero;
            lines.GetComponent<MeshFilter>().sharedMesh = mesh;
            lines.GetComponent<MeshRenderer>().sharedMaterial = overlayMaterial;
            RefreshTerrain();
            grid.Changed += RefreshTerrain;
        }

        private LineRenderer CreateLine(string label, float width)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = overlayMaterial;
            line.widthMultiplier = width;
            line.positionCount = 0;
            line.numCornerVertices = line.numCapVertices = 3;
            return line;
        }

        private MeshRenderer GetTile(List<MeshRenderer> pool, int index, string label)
        {
            if (index < pool.Count) return pool[index];
            var go = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "GridTile" };
            mesh.vertices = new[] { new Vector3(-.5f,0,-.5f), new Vector3(-.5f,0,.5f), new Vector3(.5f,0,.5f), new Vector3(.5f,0,-.5f) };
            mesh.triangles = new[] { 0,1,2,0,2,3 };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            ownedMeshes.Add(mesh);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = overlayMaterial;
            pool.Add(renderer);
            return renderer;
        }

        private void ShowTile(MeshRenderer tile, Vector2Int cell, Color color, float elevation)
        {
            tile.gameObject.SetActive(true);
            tile.transform.position = grid.GridToWorld(cell) + Vector3.up * elevation;
            tile.transform.localScale = Vector3.one * grid.CellSize * .9f;
            properties.SetColor("_Color", color);
            tile.SetPropertyBlock(properties);
        }

        private void RefreshTerrain()
        {
            if (grid == null) return;
            int count = 0;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = new Vector2Int(x, y);
                grid.TryGetCell(cell, out GridCell data);
                if (data.BlockState == BlockState.Unblocked) continue;
                ShowTile(GetTile(terrain, count++, "Terrain"), cell,
                    data.BlockState == BlockState.FullBlocked ? new Color(.8f,.15f,.15f,.5f) : new Color(.7f,.45f,.12f,.42f), .022f);
            }
            for (int i = count; i < terrain.Count; i++) terrain[i].gameObject.SetActive(false);
        }

        public void ShowRange(GridReachabilityResult range, int oneApBudget)
        {
            int count = 0;
            if (range != null)
                foreach (var item in range.ReachableCosts)
                    ShowTile(GetTile(tiles, count++, "Reachable"), item.Key,
                        item.Value <= oneApBudget ? WalkColor : DashColor, .035f);
            for (int i = count; i < tiles.Count; i++) tiles[i].gameObject.SetActive(false);
        }

        public void ShowPath(Vector3 start, Vector3 destination, int actionPointCost)
        {
            pathLine.positionCount = 2;
            targetLine.positionCount = 4;
            Color color = actionPointCost == 1 ? new Color(.2f,.85f,1) : new Color(1,.85f,.15f);
            pathLine.startColor = pathLine.endColor = targetLine.startColor = targetLine.endColor = color;
            pathLine.SetPosition(0, start + Vector3.up * .085f);
            pathLine.SetPosition(1, destination + Vector3.up * .085f);
            Vector3 center = destination + Vector3.up * .08f;
            float half = grid.CellSize * .45f;
            targetLine.SetPositions(new[] { center + new Vector3(-half,0,-half), center + new Vector3(-half,0,half),
                center + new Vector3(half,0,half), center + new Vector3(half,0,-half) });
        }

        public void ClearPath()
        {
            if (pathLine != null) pathLine.positionCount = 0;
            if (targetLine != null) targetLine.positionCount = 0;
        }

        public void Clear()
        {
            ShowRange(null, 0);
            ClearPath();
        }

        private void OnDestroy()
        {
            if (grid != null) grid.Changed -= RefreshTerrain;
            foreach (var mesh in ownedMeshes) if (mesh != null) Destroy(mesh);
        }
    }
}
