using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Wargame.Battle;

namespace Wargame.Tests
{
    public sealed class UnitHealthBarTests
    {
        [TestCase(10f, 10f, 10d, 10d)]
        [TestCase(7.5f, 10f, 10d, 8d)]
        [TestCase(7.9f, 10f, 10d, 8d)]
        [TestCase(7.1f, 10f, 10d, 8d)]
        [TestCase(7f, 10f, 10d, 7d)]
        [TestCase(7.0000005f, 10f, 10d, 8d)]
        [TestCase(6.9999995f, 10f, 10d, 7d)]
        [TestCase(.1f, 10f, 10d, 1d)]
        [TestCase(float.Epsilon, 10f, 10d, 1d)]
        [TestCase(0f, 10f, 10d, 0d)]
        [TestCase(-1f, 10f, 10d, 0d)]
        [TestCase(20f, 10f, 10d, 10d)]
        [TestCase(7.5f, 7.5f, 8d, 8d)]
        [TestCase(3.75f, 7.5f, 8d, 4d)]
        [TestCase(2f, 7.5f, 8d, 3d)]
        [TestCase(.25f, .5f, 1d, 1d)]
        public void FractionalHealthBecomesWholeSegments(float health, float max, double total, double filled)
        {
            Assert.That(UnitHealthBarRules.TryGetSegments(health, max, out double actualTotal, out double actualFilled), Is.True);
            Assert.That(actualTotal, Is.EqualTo(total));
            Assert.That(actualFilled, Is.EqualTo(filled));
        }

        [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidCapacityHasNoGeometry(float max)
        {
            Assert.That(UnitHealthBarRules.TryGetSegments(1, max, out double total, out double filled), Is.False);
            Assert.That(total, Is.Zero);
            Assert.That(filled, Is.Zero);
        }

        [Test]
        public void IntegerHealthHasNoExtraSegmentAtAnyBoundary()
        {
            for (int max = 1; max <= 200; max++)
                for (int health = 0; health <= max; health++)
                {
                    UnitHealthBarRules.TryGetSegments(health, max, out _, out double filled);
                    Assert.That(filled, Is.EqualTo(health), $"{health}/{max}");
                }
            Assert.That(UnitHealthBarRules.TryGetSegments(float.Epsilon, float.MaxValue, out _, out double tiny), Is.True);
            Assert.That(tiny, Is.EqualTo(1));
        }

        [TestCase(1f, 1d)] [TestCase(5f, 5d)] [TestCase(10f, 10d)]
        [TestCase(7.5f, 8d)] [TestCase(.5f, 1d)]
        public void PrefabKeepsItsWidthAndProducesEqualDividers(float max, double total)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<UnitHealthBarView>("Assets/Prefabs/UI/UnitHealthBar.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.ValidateConfiguration(out string error), Is.True, error);
            var view = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Vector2 size = new Vector2(96, 10);
                view.SetSize(size);
                view.SetHealth(max, max);
                Assert.That(view.TotalSegments, Is.EqualTo(total));
                Assert.That(view.Rect.sizeDelta, Is.EqualTo(size));
                Assert.That(view.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(4));
                Assert.That(view.GetComponentsInChildren<Canvas>(true), Is.Empty);
                var dividers = view.GetComponentInChildren<UnitHealthBarDividers>(true);
                Assert.That(dividers.canvasRenderer, Is.Not.Null, "刻度必须实际提交到 CanvasRenderer");
                view.Rect.ForceUpdateRectTransforms();
                using (var mesh = new VertexHelper())
                {
                    typeof(UnitHealthBarDividers).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Invoke(dividers, new object[] { mesh });
                    Assert.That(mesh.currentVertCount, Is.EqualTo((total - 1) * 4));
                    for (int i = 1; i < total; i++)
                    {
                        UIVertex left = default, right = default;
                        mesh.PopulateUIVertex(ref left, (i - 1) * 4);
                        mesh.PopulateUIVertex(ref right, (i - 1) * 4 + 2);
                        float center = (left.position.x + right.position.x) * .5f;
                        Assert.That(center, Is.EqualTo(-48 + 96 * i / total).Within(.001));
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
        }

        [TestCase(.75f, .65f)]
        [TestCase(1f, 1.1f)]
        [TestCase(1.5f, .65f)]
        [TestCase(1.5f, 1.1f)]
        public void RenderedDividersKeepEqualPixelWidthsAfterMovingAndScaling(float canvasScale, float barScale)
        {
            var root = new GameObject("PixelAlignmentCanvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.scaleFactor = canvasScale;
                var prefab = AssetDatabase.LoadAssetAtPath<UnitHealthBarView>("Assets/Prefabs/UI/UnitHealthBar.prefab");
                var view = UnityEngine.Object.Instantiate(prefab, root.transform, false);
                view.SetHealth(7.5f, 10);
                var dividers = view.GetComponentInChildren<UnitHealthBarDividers>();
                for (int frame = 0; frame < 3; frame++)
                {
                    // 不再修改生命值或尺寸，验证仅位置/父级缩放变化时也重建实际提交的网格。
                    view.Rect.anchoredPosition = new Vector2(13.37f + frame * .31f, 21.19f + frame * .27f);
                    view.Rect.localScale = Vector3.one * (barScale + frame * .13f);
                    Canvas.ForceUpdateCanvases();
                    Mesh mesh = dividers.canvasRenderer.GetMesh();
                    Assert.That(mesh, Is.Not.Null);
                    Assert.That(mesh.vertexCount, Is.EqualTo(36));
                    Vector3[] vertices = mesh.vertices;
                    float expectedWidth = Mathf.Max(1, Mathf.Round(dividers.rectTransform.localToWorldMatrix.m00));
                    for (int i = 0; i < vertices.Length; i += 4)
                    {
                        Vector3 left = dividers.rectTransform.TransformPoint(vertices[i]);
                        Vector3 right = dividers.rectTransform.TransformPoint(vertices[i + 2]);
                        Assert.That(left.x, Is.EqualTo(Mathf.Round(left.x)).Within(.001));
                        Assert.That(right.x, Is.EqualTo(Mathf.Round(right.x)).Within(.001));
                        Assert.That(left.y, Is.EqualTo(Mathf.Round(left.y)).Within(.001));
                        Assert.That(right.y, Is.EqualTo(Mathf.Round(right.y)).Within(.001));
                        Assert.That(right.x - left.x, Is.EqualTo(expectedWidth).Within(.001));
                    }
                }
                view.SetHealth(500, 1000);
                Canvas.ForceUpdateCanvases();
                Assert.That(dividers.canvasRenderer.GetMesh().vertexCount, Is.EqualTo(4), "密集刻度仍合并绘制");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PrefabFillGeometryEndsAtTheRoundedSegmentBoundary()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<UnitHealthBarView>("Assets/Prefabs/UI/UnitHealthBar.prefab");
            var view = UnityEngine.Object.Instantiate(prefab);
            try
            {
                view.SetSize(new Vector2(96, 10));
                view.Rect.ForceUpdateRectTransforms();
                var fill = view.transform.Find("RedFill").GetComponent<Image>();
                foreach (float health in new[] { 7.5f, 7.1f, 7f, .1f })
                {
                    view.SetHealth(health, 10);
                    using (var mesh = new VertexHelper())
                    {
                        typeof(Image).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                            null, new[] { typeof(VertexHelper) }, null).Invoke(fill, new object[] { mesh });
                        Assert.That(mesh.currentVertCount, Is.EqualTo(4));
                        float right = float.MinValue;
                        for (int i = 0; i < mesh.currentVertCount; i++)
                        {
                            UIVertex vertex = default;
                            mesh.PopulateUIVertex(ref vertex, i);
                            right = Mathf.Max(right, vertex.position.x);
                        }
                        Assert.That(right, Is.EqualTo(-48 + 96 * view.FilledSegments / view.TotalSegments).Within(.001));
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
        }

        [Test]
        public void ScaleIsMonotonicLinearAndBounded()
        {
            var settings = new UnitHealthBarSettings();
            Assert.That(settings.TryValidate(out _), Is.True);
            Assert.That(settings.GetScale(0), Is.EqualTo(settings.NearScale));
            Assert.That(settings.GetScale(settings.NearDistance), Is.EqualTo(settings.NearScale));
            Assert.That(settings.GetScale((settings.NearDistance + settings.FarDistance) / 2),
                Is.EqualTo((settings.NearScale + settings.FarScale) / 2).Within(.00001));
            Assert.That(settings.GetScale(settings.FarDistance), Is.EqualTo(settings.FarScale));
            Assert.That(settings.GetScale(1000), Is.EqualTo(settings.FarScale));
            float previous = float.MaxValue;
            for (int d = 0; d < 50; d++)
            {
                Assert.That(settings.GetScale(d), Is.LessThanOrEqualTo(previous));
                previous = settings.GetScale(d);
            }
        }

        [TestCase("size")] [TestCase("offset")] [TestCase("distance")]
        [TestCase("scale")] [TestCase("nan")] [TestCase("infinity")]
        public void InvalidSettingsAreRejected(string field)
        {
            var settings = new UnitHealthBarSettings();
            switch (field)
            {
                case "size": settings.BaseSize.x = 0; break;
                case "offset": settings.WorldOffset = -1; break;
                case "distance": settings.FarDistance = settings.NearDistance; break;
                case "scale": settings.NearScale = settings.FarScale; break;
                case "nan": settings.FarDistance = float.NaN; break;
                case "infinity": settings.NearScale = float.PositiveInfinity; break;
            }
            Assert.That(settings.TryValidate(out string error), Is.False);
            Assert.That(error, Does.Contain("血条"));
        }
    }
}
