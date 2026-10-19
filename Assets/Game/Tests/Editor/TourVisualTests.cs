using Gameplay.Tutorial;
using Gameplay.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Gameplay.Tests
{
    /// <summary>TASK-613 run 2: the two parts of the tour visual that break SILENTLY — a frame still renders, just wrong.
    /// (1) Every pass of <see cref="SpotlightDim"/> carries a real stencil state: <c>StencilMaterial.Add</c> hands back
    /// the base material when the ref is 0 and the colour mask is All, and then the dim paints over its own holes.
    /// (2) The prefab channel <see cref="TourAssets"/> resolves both compiled components with the rig's bindings — else
    /// the tour runs on the code-built fallback and only a warning says so.</summary>
    [TestFixture]
    public sealed class TourVisualTests
    {
        private GameObject _canvas;

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("canvas", typeof(RectTransform), typeof(Canvas));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_canvas);

        [Test]
        public void SpotlightDim_EveryPass_HasItsOwnStencilState()
        {
            var dim = SpotlightDim.Create((RectTransform)_canvas.transform, "Dim");
            var root = dim.transform;

            AssertStencil(Pass(root, "Holes"), StencilOp.Replace, CompareFunction.Always, colorMask: 0);
            AssertStencil(Pass(root, "Rings"), StencilOp.Replace, CompareFunction.NotEqual, colorMask: (int)ColorWriteMask.All);
            AssertStencil(Pass(root, "Dim"), StencilOp.Keep, CompareFunction.NotEqual, colorMask: (int)ColorWriteMask.All);
            AssertStencil(Pass(root, "StencilReset"), StencilOp.Zero, CompareFunction.Always, colorMask: 0);
        }

        [Test]
        public void SpotlightDim_PassesAreNotMaskable_SoAParentMaskCannotRewriteThem()
        {
            var dim = SpotlightDim.Create((RectTransform)_canvas.transform, "Dim");
            foreach (var g in dim.GetComponentsInChildren<MaskableGraphic>(true))
                Assert.IsFalse(g.maskable, $"{g.name} is maskable — a parent Mask would replace its stencil material");
        }

        [Test]
        public void TourAssets_ResolvesBothComponents_WithTheRigBindings()
        {
            var assets = Resources.Load<TourAssets>(TourAssets.ResourcePath);
            Assert.IsNotNull(assets, $"Resources/{TourAssets.ResourcePath} is missing — the tour would use the fallback");
            Assert.IsNotNull(assets.Finger, "TourAssets.finger is empty");
            Assert.IsNotNull(assets.Check, "TourAssets.check is empty");

            var rig = assets.Finger.GetComponent<TourFingerRig>();
            Assert.IsNotNull(rig, "the finger prefab has no TourFingerRig (component spec 'controller')");
            var so = new SerializedObject(rig);
            Assert.IsNotNull(so.FindProperty("_finger").objectReferenceValue, "_finger not bound by the UI compiler");
            Assert.IsNotNull(so.FindProperty("_ripple").objectReferenceValue, "_ripple not bound by the UI compiler");
        }

        private static Graphic Pass(Transform root, string name)
        {
            var t = root.Find(name);
            Assert.IsNotNull(t, $"pass '{name}' missing");
            return t.GetComponent<Graphic>();
        }

        private static void AssertStencil(Graphic g, StencilOp op, CompareFunction compare, int colorMask)
        {
            var m = g.material;
            Assert.AreNotSame(Canvas.GetDefaultCanvasMaterial(), m, $"{g.name}: base material, no stencil state at all");
            Assert.AreEqual(1f, m.GetFloat("_Stencil"), $"{g.name}: stencil ref");
            Assert.AreEqual((float)op, m.GetFloat("_StencilOp"), $"{g.name}: stencil op");
            Assert.AreEqual((float)compare, m.GetFloat("_StencilComp"), $"{g.name}: stencil compare");
            Assert.AreEqual((float)colorMask, m.GetFloat("_ColorMask"), $"{g.name}: colour mask");
        }
    }
}
