using Gameplay.Core;
using Gameplay.Core.Adapters;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Unity view for one piece (fills the <c>IPieceView</c> seam). Builds a flat convex mesh from the domain
    /// <see cref="Shape"/> and mirrors the pose each frame. The shared material is double-sided (Cull Off) so
    /// the one-sided fan mesh stays visible as the piece tumbles under physics (TASK-007). Juice is minimal
    /// for the gray-box slice.
    /// </summary>
    public sealed class PieceView : MonoBehaviour, IPieceView
    {
        private static readonly Color DefaultColor = new Color(0.29f, 0.56f, 0.89f); // blue (TargetPreviewView mirrors this)

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private Color _baseColor = DefaultColor;
        private PieceLifecycle _state = PieceLifecycle.Idle;
        private Mesh _mesh;
        private Transform _shadow;

        /// <summary>TASK-614: пунктир вокруг ждущей детали (<see cref="IdleDashOutline"/>); виден только в Idle.</summary>
        private GameObject _idleDash;

        /// <summary>Под каким именем эта форма ищет свою настройку дерева (<see cref="WoodPieceKey"/>).</summary>
        private string _styleKey;

        /// <summary>Вид детали из уровня. Нужен ТОЛЬКО цвету: схема Иттена раздаёт оттенки по видам,
        /// потому что масса детали в фигуре — свойство вида, а не формы.</summary>
        private string _kind;

        /// <summary>TASK-559: номер экземпляра — от него сдвиг фазы волокна (<see cref="WoodTuning.GrainPick"/>). На поле —
        /// индекс размещения в уровне, в собранной позе кадра приёмки — индекс слота.</summary>
        private int _ordinal;

        /// <summary>TASK-368: упакованный силуэт freeform-детали для шейдера (<see cref="BlockMaterial.PackSilhouette"/>);
        /// <c>null</c> у всех остальных форм. Полем, а не разово: <see cref="ApplyColor"/> собирает блок заново
        /// (Clear) на каждом перекрашивании, и контур обязан класться туда каждый раз.</summary>
        private Vector4[] _silhouette;
        private int _silhouetteCount;

        /// <summary>TASK-557: тот же силуэт, запечённый в поле расстояний (<see cref="SilhouetteField"/>): шейдер берёт
        /// кромку одной выборкой, а не циклом по рёбрам. Принадлежит детали — освобождается в OnDestroy.</summary>
        private SilhouetteField _silhouetteField;

        public string StyleKey => _styleKey;

        public void Initialize(Shape shape, string colorHex) => Initialize(shape, colorHex, null);

        public void Initialize(Shape shape, string colorHex, string kind) => Initialize(shape, colorHex, kind, 0);

        public void Initialize(Shape shape, string colorHex, string kind, int ordinal)
        {
            _kind = kind;
            _ordinal = ordinal;
            if (!string.IsNullOrEmpty(colorHex) && ColorUtility.TryParseHtmlString(colorHex, out var parsed))
                _baseColor = parsed;

            _styleKey = WoodPieceKey.Of(shape);

            var meshFilter = gameObject.AddComponent<MeshFilter>();
            _mesh = PieceMeshBuilder.Build(shape);
            meshFilter.mesh = _mesh;

            // TASK-368: составная freeform-деталь рисует кромку по СИЛУЭТУ, а не по рёбрам частей — иначе стык
            // частей читается лишней линией посреди фигуры. Меш остаётся покрытием (и запасным путём со швом).
            var silhouette = PieceMeshBuilder.SeamlessSilhouette(shape);
            _silhouette = silhouette == null ? null : BlockMaterial.PackSilhouette(silhouette);
            _silhouetteCount = _silhouette == null ? 0 : silhouette.Length;
            _silhouetteField = _silhouette == null ? null : SilhouetteField.Bake(silhouette);

            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _silhouette != null ? BlockMaterial.ConcaveShared : BlockMaterial.Shared;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            // Тень — отдельный дочерний меш позади детали (объём, просьба владельца).
            _shadow = PieceShadow.Attach(transform, _mesh, _silhouette, _silhouetteCount);

            // Пунктир ждущей детали — по СИЛУЭТУ формы: у полимино и freeform это Vertices (объединение частей),
            // у выпуклых — тот же видовой контур, что у меша (купола тесселированы гуще коллайдера).
            _idleDash = IdleDashOutline.Attach(transform,
                shape.ConvexParts != null ? shape.Vertices : ShapeVisualOutline.Build(shape));

            _mpb = new MaterialPropertyBlock();
            RefreshColor();

            // Ползунки стейтов крутят в Play на живом уровне — без подписки новая разница доехала бы
            // только до деталей, созданных после правки.
            WoodTuning.Changed += RefreshColor;
        }

        private void OnDestroy()
        {
            WoodTuning.Changed -= RefreshColor;
            _silhouetteField?.Dispose();
            _silhouetteField = null;
        }

        public void Sync(Vec2 position, float rotation)
        {
            var t = transform;
            t.position = new Vector3(position.X, position.Y, PieceDepth.For(_state, _ordinal));
            t.rotation = Quaternion.Euler(0f, 0f, rotation * Mathf.Rad2Deg);

            // Свет один на всю сцену, поэтому тень смещена в МИРОВЫХ координатах: у кувыркающейся
            // детали локальный сдвиг пришлось бы поворачивать обратно, иначе тень ездит вокруг неё.
            if (_shadow != null)
                _shadow.localPosition = PieceShadow.LocalOffset(rotation, _mesh);
        }

        /// <summary>
        /// TASK-634: вес тени в полёте бита «В рамку». У слотов картинки-цели тени нет, и тень прилетевшей детали
        /// выглядывала бы из-под картинки, поэтому в полёте она «отрывается от земли»: сдвиг тени ×<paramref name="w"/>
        /// (по текущему мировому углу детали — свет один на сцену, см. <see cref="Sync"/>), при w = 0 рендерер выключен.
        /// </summary>
        public void SetShadowWeight(float w)
        {
            if (_shadow == null)
                return;
            w = Mathf.Clamp01(w);
            var full = PieceShadow.LocalOffset(transform.eulerAngles.z * Mathf.Deg2Rad, _mesh);
            // Поправка на масштаб тени вокруг центра меша (часть LocalOffset) от веса не зависит — уходит только сдвиг света.
            var centre = _mesh != null ? _mesh.bounds.center : Vector3.zero;
            var anchor = (1f - WoodTuning.Current.ShadowScale) * new Vector3(centre.x, centre.y, 0f);
            _shadow.localPosition = new Vector3(
                Mathf.Lerp(anchor.x, full.x, w), Mathf.Lerp(anchor.y, full.y, w), full.z);
            var shadowRenderer = _shadow.GetComponent<MeshRenderer>();
            if (shadowRenderer != null)
                shadowRenderer.enabled = w > 0f;
        }

        public void SetState(PieceLifecycle state)
        {
            _state = state;
            RefreshColor();

            // TASK-631: слой — в том же вызове, что цвет и пунктир: тень и пунктир дочерние и едут с деталью, поэтому
            // смена стейта переставляет всё одним кадром. Sync повторяет глубину каждый кадр, но стейт может прийти
            // и без следующего Sync (кадр съёмки зовёт SetState после позы).
            var p = transform.position;
            p.z = PieceDepth.For(_state, _ordinal);
            transform.position = p;
        }

        // Idle (waiting for a tap) reads differently from a released/placed piece. TASK-374 removed the balance "armed"
        // telegraph (co-release is a condition, not a helper — a tapped-but-unreleased group member shows nothing), so
        // a piece is only ever Idle or Active here.
        //
        // Чем именно отличается — не цифра в коде, а настройка (WoodTuning.Tint + IdleDash*): с TASK-614 ждущая деталь
        // бледнее и обведена пунктиром, на тапе цвет возвращается, а пунктир гаснет.
        private void RefreshColor()
        {
            var idle = _state == PieceLifecycle.Idle;
            var state = idle ? PieceVisualState.Idle : PieceVisualState.Active;

            // Ремап цвета уровня к палитре считается ЗДЕСЬ, а не в Initialize: тумблер крутят в Play,
            // и исходный цвет уровня обязан остаться нетронутым, чтобы его можно было вернуть.
            var tuning = WoodTuning.Current;
            ApplyColor(tuning.Tint(tuning.ResolveLevelColor(_baseColor, _kind), state));

            // Тумблер пунктира крутят в Play — поэтому здесь, а не в SetState: Changed приходит сюда же.
            if (_idleDash != null)
                _idleDash.SetActive(idle && tuning.IdleDash);
        }

        public void Dispose()
        {
            if (this != null && gameObject != null)
                Destroy(gameObject);
        }

        private void ApplyColor(Color color)
        {
            if (_renderer == null)
                return;

            // Блок собирается ЗАНОВО, а не дописывается: у формы, с которой сняли свою настройку,
            // прошлые числа остались бы в блоке навсегда и она продолжала бы жить по ним, игнорируя
            // общие. Clear дешевле, чем помнить, какие свойства когда-то были выставлены.
            _mpb.Clear();

            color.a = 1f;
            _mpb.SetColor(BlockMaterial.ColorProperty, color);
            _mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
            WoodTuning.Current.ApplyPieceStyle(_mpb, _styleKey);

            // TASK-559: доска вида и фаза экземпляра. Спрашиваем на КАЖДОЙ перекраске: уровень мог объявиться после
            // рождения детали, а его объявление приходит сюда событием Changed.
            WoodTuning.SetGrainPick(_mpb, WoodTuning.Current.GrainPick(_kind, _ordinal));

            // TASK-368: Clear выше стёр и силуэт — без повторной загрузки живое перекрашивание (стейт, ползунок)
            // оставило бы freeform-деталь с пустым контуром, то есть без фаски и канта.
            if (_silhouette != null)
                BlockMaterial.SetSilhouette(_mpb, _silhouette, _silhouetteCount, _silhouetteField);

            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
