using System.Collections.Generic;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Static target overlay (GAME_RULES §Q5): a non-colliding outline drawn as a stroke mesh (see BuildStrokeMesh). Purely
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
                    var shader = RuntimeShaders.UrpUnlit ?? RuntimeShaders.SpritesDefault;
                    _lineMaterial = new Material(shader) { name = "SilhouetteLineMaterial" };
                }
                return _lineMaterial;
            }
        }

        private const float LineWidth = 0.08f;
        private const float LineDepth = 0.1f;
        public const int JoinSegments = 12;

        public void Initialize(Polygon polygon, Color color)
        {
            var mesh = BuildStrokeMesh(polygon.Outline, LineWidth * 0.5f, LineDepth);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = LineMaterial;

            // Цвет через блок свойств: материал общий на все контуры, а союзный контур и призрак
            // слота красятся по-разному (см. ShapeFillPresenter.GhostColor).
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BlockMaterial.ColorProperty, color);
            mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
            meshRenderer.SetPropertyBlock(mpb);
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        /// <summary>
        /// Обводка замкнутого контура СВОИМ мешем: каждое ребро данных — отдельный прямоугольник
        /// ширины <c>2·halfWidth</c>, центрированный РОВНО на ребре, а стыки закрыты кругом радиуса
        /// <c>halfWidth</c> (круглое соединение). Ребро по данным остаётся ребром на экране при любом
        /// соседстве: геометрия одного ребра не зависит от соседних.
        ///
        /// Раньше рисовал <c>LineRenderer</c> (TASK-445), и у него соседство ребра — не сосед, а соавтор:
        /// вершина ленты сдвигается по биссектрисе стыка, а у короткого ребра рядом с острым углом
        /// (свес крыши 0.2 над столбом, ступенька 0.1 у основания) сдвиги концов расходятся, и
        /// вертикальная стенка будки <c>kennel_ball</c> рисовалась наклонной (x −0.56 вверху, −0.49
        /// внизу при данных −0.5/−0.5). Тот же класс у любой фигуры «крыша над стеной».
        ///
        /// Треугольники двусторонние — видимость не зависит от того, куда смотрит камера и какой
        /// culling у запасного шейдера.
        ///
        /// Контракт раскладки (на него опирается <c>SilhouetteStrokeTests</c>): сначала по 4 вершины на каждое
        /// невырожденное ребро в порядке контура (a−n, b−n, b+n, a+n), затем по 1 + <see cref="JoinSegments"/>
        /// вершин круга на каждую вершину контура.
        /// </summary>
        public static Mesh BuildStrokeMesh(Vec2[] outline, float halfWidth, float z)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var n = outline?.Length ?? 0;

            for (var i = 0; i < n; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % n];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6f)
                    continue;
                var nx = -dy / len * halfWidth;
                var ny = dx / len * halfWidth;

                var start = vertices.Count;
                vertices.Add(new Vector3(a.X - nx, a.Y - ny, z));
                vertices.Add(new Vector3(b.X - nx, b.Y - ny, z));
                vertices.Add(new Vector3(b.X + nx, b.Y + ny, z));
                vertices.Add(new Vector3(a.X + nx, a.Y + ny, z));
                AddTriangle(triangles, start, start + 1, start + 2);
                AddTriangle(triangles, start, start + 2, start + 3);
            }

            for (var i = 0; i < n; i++)
            {
                var center = vertices.Count;
                vertices.Add(new Vector3(outline[i].X, outline[i].Y, z));
                for (var k = 0; k < JoinSegments; k++)
                {
                    var angle = 2f * Mathf.PI * k / JoinSegments;
                    vertices.Add(new Vector3(outline[i].X + Mathf.Cos(angle) * halfWidth,
                                             outline[i].Y + Mathf.Sin(angle) * halfWidth, z));
                }
                for (var k = 0; k < JoinSegments; k++)
                    AddTriangle(triangles, center, center + 1 + k, center + 1 + (k + 1) % JoinSegments);
            }

            var mesh = new Mesh { name = "SilhouetteStroke" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddTriangle(List<int> triangles, int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
        }
    }
}
