using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Unity view for one VISIBLE static geometry block (TASK-031c). Static geometry is invisible by default
    /// (it only builds a collider); a level opts a block in via <c>visible:true</c> and this renders its outline
    /// as a flat convex-fan mesh (same builder + shared double-sided material as <see cref="PieceView"/>). The
    /// outline is authored in WORLD space, so the mesh is built from it directly and the transform sits at the
    /// origin, nudged slightly behind the pieces on Z so falling pieces read in front.
    /// TASK-567: несколько блоков одного цвета в контакте рисуются ОДНИМ таким вью (<see cref="InitializeMass"/>);
    /// кто чем рисуется, решает <see cref="StaticGeometrySpawner"/>, а не вызывающие.
    /// </summary>
    public sealed class StaticGeometryView : MonoBehaviour
    {
        private const float ZBehindPieces = 0.5f; // pieces live at z∈[0, 0.04) (PieceDepth, TASK-631); larger z = further from the +Z-facing camera

        // TASK-349: a concave VISUAL MASS folds a would-be-separate wall in and tucks under the floor, so it must sit
        // STRICTLY behind the plain statics it overlaps (floor, ribbon) — otherwise same-z opaque statics z-fight and
        // "behind" becomes a draw-order coin-flip. 0.05 further from the orthographic camera is invisible in the frame
        // but makes the depth test deterministic (RFC §3.4). Applied on the concave-fill path only; if the frame ever
        // shows the mass IN FRONT of the ribbon at the tuck-under, the camera sign is flipped — the invariant is "mass
        // furthest from camera among the left-chute statics", not the exact value.
        // TASK-567: на этой же глубине стоят и СЛИТЫЕ массы (InitializeMass) — все за плоской статикой.
        private const float ZMassBehind = 0.55f;

        /// <summary>TASK-567: плотность поля слитой массы — 32, вдвое реже, чем у деталей (рычаг §5.4 плана). На 64 поле
        /// крупнейших масс не влезло в бюджет «≤ 30 мс и ≤ 4 МБ на уровень» (shaft_sort 6.4 МБ, chute_cross ~1 с CPU).
        /// У статики рёбра прямые: знаковое расстояние до прямого ребра линейно, и билинейная выборка держит его нулевую
        /// линию точно на любом шаге — смягчаются только углы, на тексель (~0.03 ед.).</summary>
        public const float MassTexelsPerUnit = 32f;

        private static readonly Color DefaultColor = new Color(0.72f, 0.57f, 0.42f); // neutral block brown

        // TASK-353: the shader's _ConcaveOutline uniform holds at most BlockMaterial.SilhouetteMax vertices. A concave mass
        // with more (none in the current manifest) falls back to the dense per-vertex SDF mesh instead of the analytic path.

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;

        // TASK-353: packed world-space CCW outline uploaded to the shader on the analytic-SDF mass path; null on every
        // other path. Kept as a field because RefreshColor rebuilds the MaterialPropertyBlock from scratch (it Clear()s),
        // so the outline must be re-applied there on each live re-colour, not once at init. Packed to the full
        // SilhouetteMax length (BlockMaterial.PackSilhouette); the live node count is _concaveOutlineCount.
        private Vector4[] _concaveOutline;
        private int _concaveOutlineCount;

        // TASK-557: the same outline baked once into a distance-field texture — the mass is the largest wood area on its
        // level, and the per-pixel edge loop over its outline was measured at 8.6 ms GPU on carrot_bed. Owned here,
        // released in OnDestroy. null → the shader keeps the per-pixel loop (same look).
        private SilhouetteField _concaveField;

        /// <summary>Полка рисуется под тенями деталей, но над фоном.</summary>
        private const int ShelfSortingOrder = -100;

        /// <summary>Под каким именем эта статика ищет свою настройку дерева.</summary>
        private string _styleKey;

        private string _colorHex;

        public string StyleKey => _styleKey;

        /// <summary>TASK-325: render a static block from its domain <see cref="Polygon"/>. A SURFACE (open edge-chain
        /// chute/ramp) draws as a wooden ribbon along its polyline; any other block falls through to the raw-outline
        /// filled path below, byte-for-byte unchanged. The shelf/sandbox sites keep calling the raw-outline overload
        /// (they are always axis-aligned rectangles), so this overload only adds the surface branch.</summary>
        public void Initialize(Polygon polygon, string colorHex)
        {
            if (polygon != null && polygon.IsSurface)
            {
                InitializeSurface(polygon, colorHex);
                return;
            }
            // TASK-346: a CONCAVE filled outline can't be a centroid fan (nor a concave collider — the validator forbids
            // that, so a concave outline reaching here is guaranteed collider:false). Render it as one ear-clipped mass.
            // Dispatch on the GEOMETRY (Geometry.IsConvex — the same test the validator's concavity gate uses), not on a
            // flag, so the two never disagree on which static takes which path.
            if (polygon?.Outline != null && !Geometry.IsConvex(polygon.Outline))
            {
                InitializeConcaveFill(polygon.Outline, colorHex);
                return;
            }
            Initialize(polygon?.Outline, colorHex);
        }

        /// <summary>TASK-346: a concave VISUAL-ONLY static is ONE ear-clipped wood mass (<see cref="PieceMeshBuilder.BuildConcaveFill"/>)
        /// — no shelf sprite (not an axis-aligned rect) and no convex fan (which cannot triangulate a concave outline).
        /// Shades, shadows and recolours exactly like the surface ribbon / convex statics; only the triangulation differs.</summary>
        private void InitializeConcaveFill(Vec2[] worldOutline, string colorHex)
        {
            _colorHex = colorHex;
            _styleKey = WoodPieceKey.OfStatic(worldOutline);

            var meshFilter = gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.AddComponent<MeshRenderer>();

            // TASK-353: primary path — a RAW ear-clip mesh (≈51 tris, ~0 ms) rendered with the analytic-SDF material,
            // which computes edge distance per-fragment from the outline polyline. The old dense per-vertex SDF built
            // 100k+ triangles and froze the load 4.7 s on chute_cross. Fall back to that dense mesh only when the outline
            // will not fit the shader's _ConcaveOutline uniform (>64 verts) or ear-clipping fails, so a pathological
            // static still renders (slowly) instead of blank.
            Mesh mesh = null;
            Vec2[] boundary = null;
            if (worldOutline != null && worldOutline.Length <= BlockMaterial.SilhouetteMax)
                mesh = PieceMeshBuilder.BuildConcaveMass(worldOutline, out boundary);

            if (mesh != null && boundary != null)
            {
                _renderer.sharedMaterial = BlockMaterial.ConcaveShared;
                _concaveOutline = BlockMaterial.PackSilhouette(boundary);
                _concaveOutlineCount = boundary.Length;
                _concaveField = SilhouetteField.Bake(boundary);
            }
            else
            {
                if (worldOutline != null && worldOutline.Length > BlockMaterial.SilhouetteMax)
                    Debug.LogWarning($"StaticGeometryView: concave mass has {worldOutline.Length} verts (> " +
                                     $"{BlockMaterial.SilhouetteMax} shader-uniform limit) — falling back to the dense SDF mesh.");
                mesh = PieceMeshBuilder.BuildConcaveFill(worldOutline, _styleKey);
                _renderer.sharedMaterial = BlockMaterial.Shared;
                _concaveOutline = null;
            }

            meshFilter.mesh = mesh;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            // TASK-349: the mass overlaps the floor (tucks under it) and sits behind the ribbon — put it strictly
            // furthest from the camera so those overlaps resolve deterministically (§3.4).
            transform.position = new Vector3(0f, 0f, ZMassBehind);

            // TASK-567 (долг TASK-472): тень массы — по силуэту (поле, без поля — цикл по контуру). Прежний
            // PieceShadow.Attach(transform, mesh) брал uv2.x меша массы, а он 0 по всей площади: альфа 0, тени не было.
            // Плотный запасной меш (BuildConcaveFill) несёт настоящий uv2.x — он остаётся на прежней тени.
            if (_concaveOutline != null)
                PieceShadow.Attach(transform, mesh, _concaveOutline, _concaveOutlineCount, _concaveField);
            else
                PieceShadow.Attach(transform, mesh);

            _mpb = new MaterialPropertyBlock();
            RefreshColor();
            WoodTuning.Changed += RefreshColor;
        }

        /// <summary>
        /// TASK-567: СЛИТАЯ ВИЗУАЛЬНАЯ МАССА — несколько видимых блоков одного цвета в контакте
        /// (<see cref="StaticMassUnion.Group"/>) одним рендерером. Меш — склейка треугольников членов
        /// (<see cref="PieceMeshBuilder.BuildMassCoverage"/>), кромка и фаска — поле расстояний по отрезкам границы союза
        /// (<see cref="SilhouetteField.Bake(StaticMassUnion.BoundarySegment[], Vec2[][], float)"/>), поэтому по бывшим
        /// стыкам ни фаски, ни контура нет. Тень — квад габарита, вырезанный знаковым полем.
        /// <para>
        /// Возвращает <c>false</c>, НИЧЕГО не добавив на объект, если массу честно нарисовать нечем: поле не запеклось
        /// (нет RGBAHalf), граница вырождена или меш пуст. Вызывающий тогда рисует членов поштучно прежним путём — со
        /// швами, но без обрезанной или ложной кромки. Цикла по контуру у массы нет: несколько колец и дырки он не выражает.
        /// </para>
        /// </summary>
        public bool InitializeMass(Polygon[] members, string colorHex)
        {
            if (members == null || members.Length < 2)
                return false;

            var outlines = new Vec2[members.Length][];
            var total = 0;
            for (var i = 0; i < members.Length; i++)
            {
                outlines[i] = members[i]?.Outline;
                if (outlines[i] == null || outlines[i].Length < 3)
                    return false;
                total += outlines[i].Length;
            }

            var boundary = StaticMassUnion.Boundary(outlines);
            if (boundary.Length == 0)
            {
                Debug.LogWarning("StaticGeometryView: слитая масса без отрезков границы — рисую блоки поштучно.");
                return false;
            }

            var field = SilhouetteField.Bake(boundary, outlines, MassTexelsPerUnit);
            if (field == null)
            {
                Debug.LogWarning("StaticGeometryView: поле массы не запеклось (нет RGBAHalf) — рисую блоки поштучно, со швами.");
                return false;
            }

            var mesh = PieceMeshBuilder.BuildMassCoverage(outlines);
            if (mesh == null)
            {
                field.Dispose();
                Debug.LogWarning("StaticGeometryView: меш слитой массы пуст — рисую блоки поштучно.");
                return false;
            }

            var all = new Vec2[total];
            var k = 0;
            foreach (var outline in outlines)
            foreach (var v in outline)
                all[k++] = v;

            _colorHex = colorHex;
            _styleKey = WoodPieceKey.OfStatic(all);   // ключ — габарит группы, как у одного блока такого размера
            _concaveField = field;
            // Пустой контур (count 0): при наличии поля шейдер цикл не исполняет. Массив нужен самим RefreshColor —
            // по нему он понимает, что блок свойств надо дополнить силуэтом после Clear().
            _concaveOutline = BlockMaterial.PackSilhouette(System.Array.Empty<Vec2>());
            _concaveOutlineCount = 0;

            gameObject.AddComponent<MeshFilter>().mesh = mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = BlockMaterial.ConcaveShared;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            // Все массы — за плоской статикой: лента (surface) и одиночные блоки на ZBehindPieces остаются перед массой,
            // как у ручной массы chute_cross (TASK-349 §3.4).
            transform.position = new Vector3(0f, 0f, ZMassBehind);

            var b = mesh.bounds;
            PieceShadow.Attach(transform, PieceMeshBuilder.BuildQuad(b.min.x, b.min.y, b.max.x, b.max.y), field);

            _mpb = new MaterialPropertyBlock();
            RefreshColor();
            WoodTuning.Changed += RefreshColor;
            return true;
        }

        /// <summary>TASK-325/349: a curved static SURFACE is a thin wooden RIBBON traced along the authored polyline. The
        /// validator still measures piece embeds against <see cref="Geometry.SurfaceRibbonQuads"/> (frozen), but the VIEW
        /// now draws ONE joined strip (<see cref="Geometry.SurfaceRibbonStrip"/> → <see cref="PieceMeshBuilder.BuildRibbonStrip"/>)
        /// so the segment joins carry no link outlines (TASK-349 defect #3) and the grain runs continuously down the chute.
        /// No shelf sprite (that is only for an axis-aligned rectangle) and no convex fan (which cannot triangulate the
        /// concave curve).</summary>
        private void InitializeSurface(Polygon polygon, string colorHex)
        {
            _colorHex = colorHex;
            _styleKey = WoodPieceKey.OfStatic(polygon.Outline);

            // TASK-349: draw the ribbon as ONE joined strip (no per-segment link outlines). SurfaceRibbonQuads stays the
            // validator's frozen embed source; this strip is a VIEW-only re-render of the same polyline + thickness.
            var strip = Geometry.SurfaceRibbonStrip(polygon.Outline, polygon.Surface.Thickness);
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            var mesh = PieceMeshBuilder.BuildRibbonStrip(strip, _styleKey);
            meshFilter.mesh = mesh;

            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = BlockMaterial.Shared;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            transform.position = new Vector3(0f, 0f, ZBehindPieces);
            PieceShadow.Attach(transform, mesh);

            _mpb = new MaterialPropertyBlock();
            RefreshColor();
            WoodTuning.Changed += RefreshColor;
        }

        public void Initialize(Vec2[] worldOutline, string colorHex)
        {
            _colorHex = colorHex;
            _styleKey = WoodPieceKey.OfStatic(worldOutline);

            // Спрайт полки — только когда прямо велено. Он ОДИН на всю статику, а её в кампании 39
            // штук и это не только полки: стена 2.60×3.90, колонна 0.35×8.00, штырьки 0.10×0.35.
            // 9-slice, растянутый на всё это, выглядел одинаково плохо, терял авторский цвет уровня
            // и не слушал ни одной настройки дерева. Подробности — WoodTuning.StaticAsWood.
            if (!WoodTuning.Current.StaticAsWood && TryInitializeShelfSprite(worldOutline))
                return;

            var meshFilter = gameObject.AddComponent<MeshFilter>();
            var mesh = PieceMeshBuilder.Build(worldOutline, _styleKey);
            meshFilter.mesh = mesh;

            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = BlockMaterial.Shared;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            transform.position = new Vector3(0f, 0f, ZBehindPieces);

            // Тень — как у деталей: без неё пол и стена стали бы плоскими наклейками рядом с
            // объёмными деталями, и разница читалась бы как ошибка.
            PieceShadow.Attach(transform, mesh);

            _mpb = new MaterialPropertyBlock();
            RefreshColor();

            // Числа крутят в Play — статика обязана перекрашиваться вместе с деталями.
            WoodTuning.Changed += RefreshColor;
        }

        private void OnDestroy()
        {
            WoodTuning.Changed -= RefreshColor;
            _concaveField?.Dispose();
            _concaveField = null;
        }

        /// <summary>
        /// Цвет статики — АВТОРСКИЙ, мимо палитры. Пробовал прогнать через неё, как детали, — вышло
        /// хуже: палитра подбирает ближайший по ТОНУ, а коричневый пола #B8926B (тон ~30°) и
        /// оранжевый #C7681D (тон ~26°) по тону почти совпадают, различаются насыщенностью и
        /// яркостью, которые матчинг не смотрит. Пол, стены и штыри становились ярко-оранжевыми,
        /// как падающие детали, и переставали читаться мебелью.
        /// Состояние тоже не применяем — у статики нет «ждёт/полетела».
        /// </summary>
        private void RefreshColor()
        {
            if (_renderer == null || _mpb == null)
                return;

            var color = DefaultColor;
            if (!string.IsNullOrEmpty(_colorHex) && ColorUtility.TryParseHtmlString(_colorHex, out var parsed))
                color = parsed;

            var tuning = WoodTuning.Current;
            color = tuning.ResolveStaticColor(color);
            color.a = 1f;

            _mpb.Clear();
            _mpb.SetColor(BlockMaterial.ColorProperty, color);
            _mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
            tuning.ApplyPieceStyle(_mpb, _styleKey);

            // TASK-559: статика всегда на спокойной доске (прямослой), фаза — от уровня и формы. Окружение не спорит
            // с фигурой; разнообразие несут детали.
            WoodTuning.SetGrainPick(_mpb, tuning.GrainPickStatic(_styleKey));

            // TASK-353: on the analytic-SDF mass path the shader reads the silhouette polyline from a per-renderer
            // uniform. Clear() above wiped it, so re-upload here — otherwise a live re-colour (WoodTuning.Changed)
            // would leave the mass with an empty outline and no edge band.
            if (_concaveOutline != null)
                BlockMaterial.SetSilhouette(_mpb, _concaveOutline, _concaveOutlineCount, _concaveField);

            _renderer.SetPropertyBlock(_mpb);
        }

        /// <summary>
        /// Ставит спрайт полки на прямоугольный контур. Возвращает false, если спрайта нет или контур
        /// не прямоугольник — тогда вызывающий рисует прежний меш.
        /// </summary>
        private bool TryInitializeShelfSprite(Vec2[] worldOutline)
        {
            var sprite = WoodArt.Shelf;
            if (sprite == null || !TryAxisAlignedRect(worldOutline, out var center, out var size))
                return false;

            var spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.drawMode = SpriteDrawMode.Sliced;
            spriteRenderer.size = size;
            spriteRenderer.sortingOrder = ShelfSortingOrder;
            spriteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            spriteRenderer.receiveShadows = false;

            // Контур авторится в МИРОВЫХ координатах, поэтому центр прямоугольника и есть позиция.
            transform.position = new Vector3(center.x, center.y, ZBehindPieces);
            return true;
        }

        private static bool TryAxisAlignedRect(Vec2[] outline, out Vector2 center, out Vector2 size)
        {
            center = Vector2.zero;
            size = Vector2.zero;
            if (outline == null || outline.Length != 4)
                return false;

            var minX = float.MaxValue; var minY = float.MaxValue;
            var maxX = float.MinValue; var maxY = float.MinValue;
            foreach (var v in outline)
            {
                minX = Mathf.Min(minX, v.X); maxX = Mathf.Max(maxX, v.X);
                minY = Mathf.Min(minY, v.Y); maxY = Mathf.Max(maxY, v.Y);
            }

            // Каждая вершина обязана лежать в углу габарита — иначе это ромб/трапеция, не полка.
            const float eps = 1e-3f;
            foreach (var v in outline)
            {
                var onX = Mathf.Abs(v.X - minX) < eps || Mathf.Abs(v.X - maxX) < eps;
                var onY = Mathf.Abs(v.Y - minY) < eps || Mathf.Abs(v.Y - maxY) < eps;
                if (!onX || !onY)
                    return false;
            }

            size = new Vector2(maxX - minX, maxY - minY);
            if (size.x < 1e-3f || size.y < 1e-3f)
                return false;

            center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            return true;
        }
    }
}
