using UnityEngine;
using UnityEngine.UI;

namespace Wargame.Battle
{
    /// <summary>在一个 Graphic 中生成全部刻度，生命容量变化不增加对象。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UnitHealthBarDividers : MaskableGraphic
    {
        [SerializeField, Min(.1f)] private float lineWidth = 1;
        private double segments = 1;
        private Matrix4x4 lastLocalToWorld;
        private bool hasPixelTransform;
        public double Segments => segments;

        protected override void OnEnable()
        {
            base.OnEnable();
            hasPixelTransform = false;
            Canvas.preWillRenderCanvases += RefreshPixelAlignment;
        }

        protected override void OnDisable()
        {
            Canvas.preWillRenderCanvases -= RefreshPixelAlignment;
            base.OnDisable();
        }

        private void RefreshPixelAlignment()
        {
            if (canvas == null || canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) return;
            Matrix4x4 current = rectTransform.localToWorldMatrix;
            if (hasPixelTransform && current.Equals(lastLocalToWorld)) return;
            lastLocalToWorld = current;
            hasPixelTransform = true;
            // 平移及父级缩放不会自动弄脏 Graphic 的顶点，但会改变屏幕像素边界。
            SetVerticesDirty();
        }

        public void SetSegments(double value)
        {
            if (segments == value) return;
            segments = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = rectTransform.rect;
            if (segments <= 1 || rect.width <= 0 || rect.height <= 0) return;
            double step = rect.width / segments;
            float width = Mathf.Max(.1f, lineWidth);
            Matrix4x4 transformToScreen = rectTransform.localToWorldMatrix;
            // Overlay 的世界 XY 就是屏幕像素；矩阵包含 CanvasScaler 及血条的距离缩放。
            // 只对屏幕水平的血条做像素对齐，旋转或非 Overlay 的 Graphic 保留原几何。
            bool alignPixels = canvas != null && canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay &&
                Mathf.Abs(transformToScreen.m01) < .00001f && Mathf.Abs(transformToScreen.m10) < .00001f &&
                transformToScreen.m00 > 0 && transformToScreen.m11 > 0;
            float scaleX = transformToScreen.m00;
            float originX = transformToScreen.m03;
            if (alignPixels)
            {
                // 先统一取整线宽，再对齐左边缘，不能分别取整左右边缘导致宽度不一。
                width = Mathf.Max(1, Mathf.Round(width * scaleX)) / scaleX;
                float scaleY = transformToScreen.m11;
                float originY = transformToScreen.m13;
                float bottom = Mathf.Round(originY + rect.yMin * scaleY);
                float top = Mathf.Max(bottom + 1, Mathf.Round(originY + rect.yMax * scaleY));
                rect.yMin = (bottom - originY) / scaleY;
                rect.yMax = (top - originY) / scaleY;
            }
            // 刻度比线宽密集时线条互相覆盖，合并其并集，避免极大容量导致海量顶点。
            if (step <= width)
            {
                float left = Mathf.Max(rect.xMin, rect.xMin + (float)step - width * .5f);
                float right = Mathf.Min(rect.xMax, rect.xMax - (float)step + width * .5f);
                if (alignPixels)
                {
                    left = SnapPixel(left, originX, scaleX);
                    right = SnapPixel(right, originX, scaleX);
                }
                if (right > left) AddRect(mesh, rect, left, right);
                return;
            }
            for (double i = 1; i < segments; i++)
            {
                float x = rect.xMin + (float)(step * i);
                float left = x - width * .5f;
                if (alignPixels) left = SnapPixel(left, originX, scaleX);
                AddRect(mesh, rect, left, left + width);
            }
        }

        private static float SnapPixel(float local, float origin, float scale) =>
            (Mathf.Round(origin + local * scale) - origin) / scale;

        private void AddRect(VertexHelper mesh, Rect rect, float left, float right)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(left, rect.yMin), color, Vector2.zero);
            mesh.AddVert(new Vector3(left, rect.yMax), color, Vector2.zero);
            mesh.AddVert(new Vector3(right, rect.yMax), color, Vector2.zero);
            mesh.AddVert(new Vector3(right, rect.yMin), color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
