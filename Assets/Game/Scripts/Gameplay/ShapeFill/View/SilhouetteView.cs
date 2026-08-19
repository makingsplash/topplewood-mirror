using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Static target overlay (GAME_RULES §Q5): a non-colliding outline drawn with a LineRenderer. Purely
    /// visual — it never affects the pieces. Used for both the union outline and per-slot ghost outlines
    /// (a paler colour), so the player sees WHERE each piece belongs.
    /// </summary>
    public sealed class SilhouetteView : MonoBehaviour
    {
        private static readonly Color OutlineColor = new Color(0.25f, 0.75f, 1f, 0.9f);

        public void Initialize(Polygon polygon) => Initialize(polygon, OutlineColor);

        /// <summary>
        /// Материал контура — СВОЙ, плоский unlit, а не общий <see cref="BlockMaterial"/>.
        ///
        /// На общем материале контур выходил жирным ОРАНЖЕВЫМ прямоугольником: деревянный шейдер
        /// не читает цвета <c>LineRenderer</c> (у него нет вершинного цвета) и красил линию своим
        /// дефолтным <c>_BaseColor</c>. Плюс тянул на неё волокно и фаску по запасному пути для
        /// чужих мешей. Заметил это, только когда впервые снял уровень целиком.
        /// </summary>
        private static Material _lineMaterial;

        private static Material LineMaterial
        {
            get
            {
                if (_lineMaterial == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Unlit")
                                 ?? Shader.Find("Sprites/Default")
                                 ?? Shader.Find("Unlit/Color");
                    _lineMaterial = new Material(shader) { name = "SilhouetteLineMaterial" };
                }
                return _lineMaterial;
            }
        }

        public void Initialize(Polygon polygon, Color color)
        {
            var outline = polygon.Outline;
            var line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = outline.Length;
            line.widthMultiplier = 0.08f;
            line.numCornerVertices = 2;
            line.sharedMaterial = LineMaterial;
            line.textureMode = LineTextureMode.Stretch;
            line.startColor = color;
            line.endColor = color;

            // Цвет через блок свойств: материал общий на все контуры, а союзный контур и призрак
            // слота красятся по-разному (см. ShapeFillPresenter.GhostColor).
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BlockMaterial.ColorProperty, color);
            mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
            line.SetPropertyBlock(mpb);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            for (var i = 0; i < outline.Length; i++)
                line.SetPosition(i, new Vector3(outline[i].X, outline[i].Y, 0.1f));
        }
    }
}
