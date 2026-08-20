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
    /// </summary>
    public sealed class StaticGeometryView : MonoBehaviour
    {
        private const float ZBehindPieces = 0.5f; // pieces live at z=0; larger z = further from the +Z-facing camera

        private static readonly Color DefaultColor = new Color(0.72f, 0.57f, 0.42f); // neutral block brown

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;

        /// <summary>Полка рисуется под тенями деталей, но над фоном.</summary>
        private const int ShelfSortingOrder = -100;

        /// <summary>Под каким именем эта статика ищет свою настройку дерева.</summary>
        private string _styleKey;

        private string _colorHex;

        public string StyleKey => _styleKey;

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

        private void OnDestroy() => WoodTuning.Changed -= RefreshColor;

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
