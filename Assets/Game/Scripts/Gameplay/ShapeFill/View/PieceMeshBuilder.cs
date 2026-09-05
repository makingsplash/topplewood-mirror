using System;
using System.Collections.Generic;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Builds a flat convex fan mesh from a domain <see cref="Shape"/>. Shared by <see cref="PieceView"/>
    /// (the physical pieces) and <see cref="TargetPreviewView"/> (the mini preview) so both render from a
    /// single source of truth — no duplicated triangulation (RFC §4.4). v0 shapes are convex.
    /// Arc primitives are tessellated for the VIEW by <see cref="ShapeVisualOutline"/> (higher segment count
    /// than the ≤8-vertex physics collider, TASK-074) — the physics vertices are never used for rendering.
    ///
    /// <para>
    /// Меш несёт ЧЕТЫРЕ канала, и три из них существуют ради шейдера <c>Game/Wood Block</c>:
    /// uv0 — нормализованные по габариту координаты (запасной путь для чужих мешей),
    /// uv1 — расстояние до кромки В МИРОВЫХ ЕДИНИЦАХ (x) и маркер «меш собран здесь» (y),
    /// uv2 — габарит детали, uv3 — НОРМАЛЬ КРОМКИ этого сектора.
    /// </para>
    /// <para>
    /// Нормаль кромки — то, из-за чего меш собран НЕиндексированным (по три вершины на треугольник,
    /// вершины контура дублируются между соседними секторами). Владелец сказал «материал на всех кусках
    /// смотрится по-разному»: свет и фаска считались по uv, а uv нормализованы по габариту — у широкого
    /// бруска градиент шёл иначе, чем у узкого, у треугольника иначе, чем у обоих. Зная нормаль своей
    /// кромки, шейдер светит по МИРОВОМУ направлению света одинаково на любой детали и при любом
    /// повороте. Общая вершина усреднила бы нормали двух соседних кромок — поэтому вершины и разведены.
    /// Цена — втрое больше вершин на деталь (у квадрата со скруглением это 48 штук), при таком числе
    /// деталей на экране это ничто.
    /// </para>
    /// </summary>
    public static class PieceMeshBuilder
    {
        // View outline, NOT shape.Vertices: dome primitives render at ShapeVisualOutline.VisualArcSegments so the
        // mesh stays smooth while the collider stays at ≤8 vertices (TASK-074). Do not collapse this back to
        // shape.Vertices — that would re-facet the wheels. Straight-edged shapes pass through unchanged.
        // Ключ считается по ДОМЕННОЙ форме, а не по визуальному контуру: у круга и купола контур
        // тесселируется гуще, и ключ поехал бы от числа сегментов (см. WoodPieceKey).
        // TASK-178: a CONCAVE polyomino cannot be a single centroid-fan (its centroid can fall in the notch, outside
        // the body, inverting triangles). It is rendered as its authored convex parts — each part is fanned separately
        // into ONE combined mesh, so the silhouette fills solid with correct per-edge wood normals. Corner rounding is
        // skipped for polyominoes (rounding each rectangular part would notch the internal seams); rounded silhouette
        // corners for tetrominoes are art-director's call (tech-debt in the artifact). Convex primitives are unchanged.
        public static Mesh Build(Shape shape)
            => shape.ConvexParts != null
                ? BuildCompound(shape.ConvexParts, WoodPieceKey.Of(shape))
                : Build(ShapeVisualOutline.Build(shape), WoodPieceKey.Of(shape));

        /// <summary>
        /// Радиус скругления углов для детали ОПОРНОГО размера (<see cref="WoodTuning.DetailRefSize"/>);
        /// деталь другого размера получает его в своей пропорции. Углы режутся в геометрии,
        /// а не в шейдере: у деталей произвольный выпуклый контур, и «расстояние до угла» в шейдере
        /// восстановить нечем — фаска знает только расстояние до кромки.
        /// Живёт в <see cref="WoodTuning"/>: владелец крутит радиус ползунком прямо в Play, и
        /// <see cref="RebuildAll"/> перекладывает новую форму во все живые меши.
        /// </summary>
        public static float CornerRadius
        {
            get => WoodTuning.Current.CornerRadius;
            set => WoodTuning.Current.CornerRadius = value;
        }

        /// <summary>Build a flat convex fan mesh from a raw CCW outline (TASK-031: visible static geometry reuses
        /// the same triangulation as pieces). The outline must be CONVEX — v0's fan cannot triangulate a concave
        /// polygon. This is the SOLID-block path only; a CONCAVE static is not authored as this single outline but as
        /// one of three other shapes: a curved SURFACE renders through <see cref="BuildRibbonStrip"/> (ONE joined ribbon
        /// strip, TASK-349; TASK-325 drew per-segment quads), a filled concave POLYOMINO through <c>Build(Shape)</c>'s compound path (authored
        /// <c>ConvexParts</c>), and a concave VISUAL-ONLY static through <see cref="BuildConcaveFill"/> (ear-clipped,
        /// TASK-346). So "static may be any shape" and "this fan needs a convex outline" do not contradict — a
        /// non-convex static simply does not take THIS path (dev-architect RFC 03 §1, TASK-346 §2).</summary>
        public static Mesh Build(Vec2[] rawVerts) => Build(rawVerts, WoodPieceKey.Of(rawVerts));

        /// <summary>TASK-325 (S2): build ONE mesh from a set of convex world-space parts with NO corner rounding —
        /// the ribbon of a static SURFACE (each polyline segment offset to a convex quad, <see cref="Geometry.SurfaceRibbonQuads"/>).
        /// Reuses the polyomino compound fan verbatim (same four wood-shader channels, same live-rebuild registration),
        /// so a wooden chute shades and rebuilds exactly like a compound piece — no new triangulation code.</summary>
        public static Mesh BuildParts(Vec2[][] parts, string key) => BuildCompound(parts, key);

        /// <summary><paramref name="key"/> — под каким именем эта форма ищет свою настройку
        /// (<see cref="WoodTuning.PieceOverrides"/>). Хранится вместе с контуром, чтобы
        /// <see cref="RebuildAll"/> перекладывал форму по тем же числам, что и при первой сборке.</summary>
        public static Mesh Build(Vec2[] rawVerts, string key)
        {
            var mesh = new Mesh { name = "PieceMesh" };
            Fill(mesh, rawVerts, key);
            Register(mesh, rawVerts, key);
            return mesh;
        }

        /// <summary>
        /// TASK-353: build the CONCAVE VISUAL MASS as a RAW ear-clip mesh — the PRIMARY path. The mesh is just the
        /// ear-clipped silhouette (<see cref="Geometry.TriangulateConcave"/>, ≈51 triangles, ~0 ms): it carries NO
        /// per-vertex distance. Distance-to-edge and the nearest edge's outward normal are instead computed per-fragment
        /// by the shader's <c>_CONCAVE_SDF</c> variant from the returned <paramref name="boundary"/> polyline (the caller
        /// loads it into the <c>_ConcaveOutline</c> uniform via a <c>MaterialPropertyBlock</c>).
        /// <para>
        /// Why this replaced the dense per-vertex SDF (<see cref="BuildConcaveFill"/>) as the main path: on chute_cross the
        /// only concave visible mass in the manifest, the SDF refine built 100 556 triangles and froze the load for 4.7 s
        /// (TASK-353 measurement). The mass is a thin chute wall (half-width ~0.67 &lt; the SdfBandDepth 0.9), so nearly
        /// every vertex fell inside the fine band and the "few thousand triangles" promise never held. Encoding distance in
        /// the mesh at all also coupled the triangle budget to the owner's LIVE band-width tuning; the analytic SDF is
        /// exact at any band width for ~51 triangles and decouples the two completely (dev-architect RFC §3, direction 3).
        /// </para>
        /// Returns the raw mesh and, via <paramref name="boundary"/>, the CCW-normalised outline the shader consumes.
        /// Returns <c>null</c> (with <paramref name="boundary"/> null) when the outline is degenerate or not a simple
        /// polygon — the caller then falls back to <see cref="BuildConcaveFill"/>, which draws an honest bbox and shouts.
        /// No corner rounding (would notch the silhouette) and no <see cref="RebuildAll"/> registration (radius-independent).
        /// </summary>
        public static Mesh BuildConcaveMass(Vec2[] outline, out Vec2[] boundary)
        {
            boundary = null;
            if (outline == null || outline.Length < 3)
                return null;
            if (!Geometry.TriangulateConcave(outline, out var ccw, out var baseTris))
                return null;

            var mesh = new Mesh { name = "StaticMassMesh" };
            FillRawMass(mesh, ccw, baseTris);
            boundary = ccw;
            return mesh;
        }

        /// <summary>
        /// TASK-346/349: fill a CONCAVE simple outline with a DENSE per-vertex SDF mesh. FALLBACK path since TASK-353 —
        /// the main path (<see cref="BuildConcaveMass"/>) is a raw ear-clip + analytic SDF in the shader; this dense mesh
        /// only runs when the outline exceeds the shader's <c>_ConcaveOutline</c> uniform limit (&gt;64 verts) or its
        /// triangulation fails, so a pathological static still renders (slowly) rather than blank.
        /// <para>
        /// It ear-clips the outline (<see cref="Geometry.TriangulateConcave"/>) into ONE indexed mesh, then feeds the
        /// shader what its per-vertex path consumes: a NATIVE mesh (uv2.y = 1) carrying, per vertex, the true distance to
        /// the silhouette (<see cref="Geometry.NearestBoundaryEdge"/>) and that nearest edge's outward normal. The interior
        /// is refined (<see cref="RefineForSdf"/>) so every ear-clip CHORD carries interior vertices with distance &gt; 0 —
        /// which is what stops the shader painting an outline band along the chords. That is exactly why it was expensive:
        /// the refine is the ONLY source of non-zero interior distance, so a thin wall subdivides its whole area. This path
        /// keeps that cost as the correctness safety net for outlines the uniform cannot hold.
        /// </para>
        /// No corner rounding and no <see cref="RebuildAll"/> registration. <paramref name="key"/> keeps style parity with
        /// the convex statics for the non-simple-contour fallback only.
        /// </summary>
        public static Mesh BuildConcaveFill(Vec2[] outline, string key)
        {
            var mesh = new Mesh { name = "StaticFillMesh" };
            FillTriangulated(mesh, outline, key);
            return mesh;
        }

        /// <summary>TASK-353: fill the raw ear-clip mesh of a concave mass — positions + bbox-normalised uv0 + bbox size
        /// (uv3, the shader's <c>k</c> reads it), NO per-vertex distance (uv2.x = 0) and NO edge normal (uv4 = 0): both are
        /// computed per-fragment by the <c>_CONCAVE_SDF</c> variant from the outline uniform. uv2.y = 1 is a harmless
        /// marker unread on that path; it matters only if the keyword is stripped from a device build (then the mesh falls
        /// into the per-vertex branch with distance 0 = a solid edge band — the tech-debt guarded on the first device
        /// build).</summary>
        private static void FillRawMass(Mesh mesh, Vec2[] boundary, int[] triangles)
        {
            var n = boundary.Length;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in boundary)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            var sizeX = Mathf.Max(maxX - minX, 1e-4f);
            var sizeY = Mathf.Max(maxY - minY, 1e-4f);
            var size = new Vector2(sizeX, sizeY);

            var vertices = new Vector3[n];
            var uv = new Vector2[n];     // bbox-normalized — read only by the FOREIGN fallback branch (keyword stripped)
            var uv2 = new Vector2[n];    // (0, 1): distance unused on the _CONCAVE_SDF path (shader computes it); marker 1
            var uv3 = new Vector2[n];    // bbox size (world units) — feeds the shader's per-piece k
            var uv4 = new Vector2[n];    // zero — edge normal computed per-fragment on the _CONCAVE_SDF path
            for (var i = 0; i < n; i++)
            {
                var p = boundary[i];
                vertices[i] = new Vector3(p.X, p.Y, 0f);
                uv[i] = new Vector2((p.X - minX) / sizeX, (p.Y - minY) / sizeY);
                uv2[i] = new Vector2(0f, 1f);
                uv3[i] = size;
                uv4[i] = Vector2.zero;
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, uv2);
            mesh.SetUVs(2, uv3);
            mesh.SetUVs(3, uv4);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        // TASK-349 SDF-refine tuning — FALLBACK path only since TASK-353 (the main path is the shader's analytic SDF,
        // BuildConcaveMass). Edges split fine within the silhouette band and stay coarse in the deep interior. The
        // "few thousand triangles" this comment once promised did NOT hold on a thin wall (chute_cross: 100 556 tris,
        // 4.7 s freeze) because nearly every vertex fell inside the band — which is exactly why the dense path was
        // demoted to a fallback for outlines the shader uniform (≤64 verts) cannot hold.
        private const float SdfRefineNear = 0.22f; // max edge length for a triangle touching the silhouette band
        private const float SdfRefineFar = 2.0f;   // max edge length in the deep interior (coarse; no bands there)
        private const float SdfBandDepth = 0.9f;   // "near the silhouette" = a vertex within this distance of the outline
        private const int SdfMaxVerts = 60000;     // runaway guard — one static mass; far above any real need

        private static void FillTriangulated(Mesh mesh, Vec2[] outline, string key)
        {
            if (outline == null || outline.Length < 3)
            {
                mesh.Clear();
                return;
            }

            if (!Geometry.TriangulateConcave(outline, out var boundary, out var baseTris))
            {
                // A non-simple contour slipped past the validator: never hang or emit a silently-wrong mesh — draw the
                // bounding box as an honest, obviously-wrong fallback and shout, so the bad data is found loud.
                Debug.LogError("PieceMeshBuilder.BuildConcaveFill: ear clipping stalled on a non-simple outline — " +
                               "rendering its bounding box as a fallback. Fix the authored static.");
                Fill(mesh, BoundingBoxQuad(outline), key);
                return;
            }

            Geometry.RefineForSdf(boundary, baseTris, SdfRefineNear, SdfRefineFar, SdfBandDepth, SdfMaxVerts,
                out var verts, out var triangles, out var hitCeiling);
            if (hitCeiling)
                Debug.LogWarning("PieceMeshBuilder.FillTriangulated: SDF refinement hit its ceiling — a faint chord " +
                                 "ghost may survive; lower SdfRefineNear or raise SdfMaxVerts for this outline.");

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in verts)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            var sizeX = Mathf.Max(maxX - minX, 1e-4f);
            var sizeY = Mathf.Max(maxY - minY, 1e-4f);
            var size = new Vector2(sizeX, sizeY);

            var bn = boundary.Length;
            var n = verts.Length;
            var vertices = new Vector3[n];
            var uv = new Vector2[n];     // bbox-normalized — the FOREIGN fallback path reads this (unused when native)
            var uv2 = new Vector2[n];    // (dist, 1): NATIVE marker → shader draws the band from the true edge distance
            var uv3 = new Vector2[n];    // bbox size (world units)
            var uv4 = new Vector2[n];    // outward normal of the nearest outline edge (drives the directional bevel)
            for (var i = 0; i < n; i++)
            {
                var p = verts[i];
                var dist = Geometry.NearestBoundaryEdge(boundary, p, out var e);
                // boundary is CCW-normalised by TriangulateConcave, so the outward normal of edge e (a→b) is (dy, −dx).
                var a = boundary[e];
                var b = boundary[(e + 1) % bn];
                var edge = b - a;
                var len = edge.Length;
                var normal = len > 1e-6f ? new Vector2(edge.Y, -edge.X) / len : Vector2.up;

                vertices[i] = new Vector3(p.X, p.Y, 0f);
                uv[i] = new Vector2((p.X - minX) / sizeX, (p.Y - minY) / sizeY);
                uv2[i] = new Vector2(dist, 1f);
                uv3[i] = size;
                uv4[i] = normal;
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, uv2);
            mesh.SetUVs(2, uv3);
            mesh.SetUVs(3, uv4);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        /// <summary>
        /// TASK-349: build ONE continuous wood mesh for a static SURFACE ribbon from the joined strip
        /// (<see cref="Geometry.SurfaceRibbonStrip"/>). Unlike the TASK-325 per-segment quads (each drew its own
        /// perimeter, so the cut edges between segments outlined as a chain of "links" — the task's defect #3), the
        /// strip is one connected mesh whose edge band is driven by the CROSS distance to the two rails: the long sides
        /// bevel (<c>edge → 0</c>), the internal join cuts do not (<c>edge ≈ half</c>). The grain runs continuously down
        /// the whole chute (one world-space mesh) instead of resetting per segment. VIEW-ONLY — the ribbon polyline and
        /// <see cref="Geometry.SurfaceRibbonQuads"/> (the validator's embed source) are untouched, so poseHash is
        /// unchanged. No <see cref="RebuildAll"/> registration (thickness-driven, radius-independent).
        /// </summary>
        public static Mesh BuildRibbonStrip(Geometry.RibbonStrip strip, string key)
        {
            var mesh = new Mesh { name = "StaticRibbonMesh" };
            FillRibbonStrip(mesh, strip, key);
            return mesh;
        }

        private static void FillRibbonStrip(Mesh mesh, Geometry.RibbonStrip strip, string key)
        {
            if (strip.IsEmpty)
            {
                mesh.Clear();
                return;
            }

            var sv = strip.Vertices;
            var n = sv.Length;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in sv)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            var sizeX = Mathf.Max(maxX - minX, 1e-4f);
            var sizeY = Mathf.Max(maxY - minY, 1e-4f);
            var size = new Vector2(sizeX, sizeY);

            var vertices = new Vector3[n];
            var uv = new Vector2[n];
            var uv2 = new Vector2[n];
            var uv3 = new Vector2[n];
            var uv4 = new Vector2[n];
            for (var i = 0; i < n; i++)
            {
                var p = sv[i];
                // Distance to the NEAREST rail: 0 on the two long sides (band), half on the centreline (no band) —
                // so only the rails bevel/outline and the segment-join cuts (mid-ribbon in cross) stay clean.
                var edge = strip.Half - Mathf.Abs(strip.Cross[i]);
                var nrm = strip.Normal[i];
                vertices[i] = new Vector3(p.X, p.Y, 0f);
                uv[i] = new Vector2((p.X - minX) / sizeX, (p.Y - minY) / sizeY);
                uv2[i] = new Vector2(edge, 1f);
                uv3[i] = size;
                uv4[i] = new Vector2(nrm.X, nrm.Y);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, uv2);
            mesh.SetUVs(2, uv3);
            mesh.SetUVs(3, uv4);
            mesh.SetTriangles(strip.Triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        /// <summary>Axis-aligned bounding-box quad (CCW) of an outline — the ear-clip fallback shape.</summary>
        private static Vec2[] BoundingBoxQuad(Vec2[] outline)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in outline)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            return new[] { new Vec2(minX, minY), new Vec2(maxX, minY), new Vec2(maxX, maxY), new Vec2(minX, maxY) };
        }

        // ------------------------------------------------------- пересборка формы

        private readonly struct LiveMesh
        {
            public readonly WeakReference<Mesh> Mesh;
            public readonly Vec2[] Outline;    // single convex outline (null for a compound)
            public readonly Vec2[][] Parts;    // TASK-178: convex parts (null for a single outline)
            public readonly string Key;

            public LiveMesh(Mesh mesh, Vec2[] outline, Vec2[][] parts, string key)
            {
                Mesh = new WeakReference<Mesh>(mesh);
                Outline = outline;
                Parts = parts;
                Key = key;
            }
        }

        // Слабые ссылки: деталь уничтожается вместе со своим мешем, а реестр не должен её удерживать.
        private static readonly List<LiveMesh> Live = new List<LiveMesh>(128);

        private static void Register(Mesh mesh, Vec2[] outline, string key)
            => Live.Add(new LiveMesh(mesh, outline, null, key));

        private static void RegisterCompound(Mesh mesh, Vec2[][] parts, string key)
            => Live.Add(new LiveMesh(mesh, null, parts, key));

        /// <summary>
        /// Переложить форму во все живые меши — их читают и деталь, и её тень (тот же объект Mesh),
        /// поэтому тень подхватывает новые углы сама. Зовётся при смене радиуса скругления.
        /// Мёртвые записи выметаются здесь же: отдельного момента «деталь уничтожили» у нас нет.
        /// </summary>
        public static void RebuildAll()
        {
            for (var i = Live.Count - 1; i >= 0; i--)
            {
                if (!Live[i].Mesh.TryGetTarget(out var mesh) || mesh == null)
                {
                    Live.RemoveAt(i);
                    continue;
                }
                if (Live[i].Parts != null)
                    FillCompound(mesh, Live[i].Parts, Live[i].Key);
                else
                    Fill(mesh, Live[i].Outline, Live[i].Key);
            }
        }

        // ------------------------------------------------------------- сборка меша

        private static void Fill(Mesh mesh, Vec2[] rawVerts, string key)
        {
            var tuning = WoodTuning.Current;

            // Скругление углов — до расчёта габарита и uv: иначе uv растянулись бы по прежнему,
            // более острому контуру, и волокно поехало бы относительно силуэта.
            //
            // Радиус — доля от размера ЭТОЙ детали, а не мировая константа. На детали кампании 0.3
            // (cap из grand_seesaw) мировые 0.095 срезали по 32% стороны с каждого угла: квадрат
            // превращался в таблетку, и никакой шейдер этого уже не спасал. Габарит меряем по
            // ИСХОДНОМУ контуру — скруглённый заведомо меньше, и радиус поехал бы сам от себя.
            var radius = tuning.CornerRadiusFor(key, ShortSide(rawVerts));
            var verts = RoundCorners(rawVerts, radius, Mathf.Max(1, tuning.CornerSegments));
            var n = verts.Length;
            if (n < 3)
            {
                mesh.Clear();
                return;
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            float cx = 0f, cy = 0f;
            for (var i = 0; i < n; i++)
            {
                var v = verts[i];
                cx += v.X; cy += v.Y;
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            cx /= n; cy /= n;

            // Вырожденный габарит (деталь толщиной в ноль по одной оси) обнулил бы делитель.
            var sizeX = Mathf.Max(maxX - minX, 1e-4f);
            var sizeY = Mathf.Max(maxY - minY, 1e-4f);
            var size = new Vector2(sizeX, sizeY);

            // Веер строится от ЦЕНТРОИДА, а не от нулевой вершины контура: шейдеру нужна вершина
            // ВНУТРИ формы, иначе кромкой оказывается вся деталь целиком и фаске негде лечь.
            // Побочная выгода: у куполов и колёс фаска идёт по силуэту, а не по bbox.
            var center = new Vector2(cx, cy);

            var count = n * 3;
            var vertices = new Vector3[count];
            var uv = new Vector2[count];      // нормализованные по габариту — запасной путь и волокно
            var uv2 = new Vector2[count];     // x: расстояние до кромки В ЕДИНИЦАХ, y: маркер
            var uv3 = new Vector2[count];     // габарит детали
            var uv4 = new Vector2[count];     // нормаль кромки сектора (наружу), в системе детали
            var triangles = new int[count];

            for (var i = 0; i < n; i++)
            {
                var b = new Vector2(verts[i].X, verts[i].Y);
                var c = new Vector2(verts[(i + 1) % n].X, verts[(i + 1) % n].Y);

                // Контур идёт против часовой, поэтому наружная нормаль ребра b→c — это (dy, -dx).
                var edge = c - b;
                var len = edge.magnitude;
                var normal = len > 1e-6f ? new Vector2(edge.y, -edge.x) / len : Vector2.up;

                // Высота центроида над этим ребром — она же расстояние до кромки в его секторе.
                // Модуль: у почти вырожденного сектора знак мог бы уйти в минус и вывернуть фаску.
                var height = Mathf.Abs(Vector2.Dot(center - b, normal));

                var t = i * 3;
                Vertex(t + 0, center, height);
                Vertex(t + 1, b, 0f);
                Vertex(t + 2, c, 0f);

                triangles[t + 0] = t + 0;
                triangles[t + 1] = t + 1;
                triangles[t + 2] = t + 2;

                void Vertex(int index, Vector2 p, float distanceToEdge)
                {
                    vertices[index] = new Vector3(p.x, p.y, 0f);
                    uv[index] = new Vector2((p.x - minX) / sizeX, (p.y - minY) / sizeY);
                    // uv2.y = 1 — маркер «расстояние до кромки посчитано». Меши, собранные не здесь
                    // (кварды зоны воздуха, линии силуэта), придут с uv2 = 0, и шейдер по этому
                    // маркеру откатится на оценку по габариту вместо того, чтобы залить их кромкой.
                    uv2[index] = new Vector2(distanceToEdge, 1f);
                    uv3[index] = size;
                    uv4[index] = normal;
                }
            }

            // Clear перед записью: у пересобираемого меша число вершин меняется вместе с радиусом
            // скругления, и без очистки Unity ругается на несогласованные каналы.
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, uv2);
            mesh.SetUVs(2, uv3);
            mesh.SetUVs(3, uv4);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        // ------------------------------------------------------- TASK-178: составной меш полимино

        /// <summary>Меш вогнутого полимино: каждый выпуклый кусок — свой веер, всё в один меш. Скругление углов
        /// пропущено (скруглять внутренние стыки нельзя), силуэт заливается сплошняком. Каналы шейдера считаются
        /// как у <see cref="Fill"/>, но габарит (uv0/uv3) берётся по ВСЕЙ детали.</summary>
        private static Mesh BuildCompound(Vec2[][] parts, string key)
        {
            var mesh = new Mesh { name = "PieceMesh" };
            FillCompound(mesh, parts, key);
            RegisterCompound(mesh, parts, key);
            return mesh;
        }

        private static void FillCompound(Mesh mesh, Vec2[][] parts, string key)
        {
            // Габарит по всем кускам — общий масштаб волокна/фаски на всю деталь (как у одиночного контура).
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            var triCount = 0;
            foreach (var part in parts)
            {
                if (part == null || part.Length < 3)
                    continue;
                triCount += part.Length;
                foreach (var v in part)
                {
                    if (v.X < minX) minX = v.X;
                    if (v.X > maxX) maxX = v.X;
                    if (v.Y < minY) minY = v.Y;
                    if (v.Y > maxY) maxY = v.Y;
                }
            }
            if (triCount < 3)
            {
                mesh.Clear();
                return;
            }

            var sizeX = Mathf.Max(maxX - minX, 1e-4f);
            var sizeY = Mathf.Max(maxY - minY, 1e-4f);
            var size = new Vector2(sizeX, sizeY);

            var count = triCount * 3;
            var vertices = new Vector3[count];
            var uv = new Vector2[count];
            var uv2 = new Vector2[count];
            var uv3 = new Vector2[count];
            var uv4 = new Vector2[count];
            var triangles = new int[count];

            var t = 0;
            foreach (var part in parts)
            {
                if (part == null || part.Length < 3)
                    continue;
                var np = part.Length;

                float cx = 0f, cy = 0f;
                for (var i = 0; i < np; i++) { cx += part[i].X; cy += part[i].Y; }
                var center = new Vector2(cx / np, cy / np);

                for (var i = 0; i < np; i++)
                {
                    var b = new Vector2(part[i].X, part[i].Y);
                    var c = new Vector2(part[(i + 1) % np].X, part[(i + 1) % np].Y);
                    var edge = c - b;
                    var len = edge.magnitude;
                    var normal = len > 1e-6f ? new Vector2(edge.y, -edge.x) / len : Vector2.up;
                    var height = Mathf.Abs(Vector2.Dot(center - b, normal));

                    Vertex(t + 0, center, height, normal);
                    Vertex(t + 1, b, 0f, normal);
                    Vertex(t + 2, c, 0f, normal);
                    triangles[t + 0] = t + 0;
                    triangles[t + 1] = t + 1;
                    triangles[t + 2] = t + 2;
                    t += 3;
                }
            }

            void Vertex(int index, Vector2 p, float distanceToEdge, Vector2 normal)
            {
                vertices[index] = new Vector3(p.x, p.y, 0f);
                uv[index] = new Vector2((p.x - minX) / sizeX, (p.y - minY) / sizeY);
                uv2[index] = new Vector2(distanceToEdge, 1f);
                uv3[index] = size;
                uv4[index] = normal;
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, uv2);
            mesh.SetUVs(2, uv3);
            mesh.SetUVs(3, uv4);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        /// <summary>Короткая сторона габарита контура — по ней считается, насколько деталь мельче
        /// опорной (<see cref="WoodTuning.DetailFactor"/>). Именно короткая: на бруске 2.8×0.3
        /// грубым выглядит поперечный угол, а не продольный.</summary>
        private static float ShortSide(Vec2[] verts)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in verts)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            return Mathf.Max(Mathf.Min(maxX - minX, maxY - minY), 1e-4f);
        }

        /// <summary>
        /// Срезает каждый выпуклый угол контура короткой дугой (квадратичная безье через сам угол —
        /// от настоящей дуги на глаз не отличается, зато нет деления на синус половинного угла).
        /// Радиус зажимается по КОРОТКОЙ соседней стороне, иначе на узком бруске два соседних
        /// скругления съели бы сторону целиком и контур вывернулся бы.
        /// Почти прямые участки (тесселированная дуга купола/колеса от ShapeVisualOutline) пропускаются:
        /// скруглять там нечего, а вершины они бы утроили.
        /// </summary>
        private static Vec2[] RoundCorners(Vec2[] verts, float radius, int segments)
        {
            var n = verts.Length;
            if (n < 3 || radius <= 1e-4f)
                return verts;

            var result = new List<Vec2>(n * (segments + 1));
            for (var i = 0; i < n; i++)
            {
                var cur = new Vector2(verts[i].X, verts[i].Y);
                var prev = new Vector2(verts[(i - 1 + n) % n].X, verts[(i - 1 + n) % n].Y);
                var next = new Vector2(verts[(i + 1) % n].X, verts[(i + 1) % n].Y);

                var toPrev = prev - cur;
                var toNext = next - cur;
                var lenPrev = toPrev.magnitude;
                var lenNext = toNext.magnitude;
                if (lenPrev < 1e-5f || lenNext < 1e-5f)
                {
                    result.Add(verts[i]);
                    continue;
                }

                var dirPrev = toPrev / lenPrev;
                var dirNext = toNext / lenNext;
                if (Vector2.Dot(dirPrev, dirNext) < -0.985f)   // ≈ развёрнутый угол
                {
                    result.Add(verts[i]);
                    continue;
                }

                var r = Mathf.Min(radius, lenPrev * 0.45f, lenNext * 0.45f);
                var a = cur + dirPrev * r;
                var b = cur + dirNext * r;

                for (var s = 0; s <= segments; s++)
                {
                    var t = s / (float)segments;
                    var p = Vector2.Lerp(Vector2.Lerp(a, cur, t), Vector2.Lerp(cur, b, t), t);
                    result.Add(new Vec2(p.x, p.y));
                }
            }

            return result.ToArray();
        }
    }
}
