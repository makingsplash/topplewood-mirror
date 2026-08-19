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
        private bool _armed;
        private Mesh _mesh;
        private Transform _shadow;

        /// <summary>Под каким именем эта форма ищет свою настройку дерева (<see cref="WoodPieceKey"/>).</summary>
        private string _styleKey;

        /// <summary>Вид детали из уровня. Нужен ТОЛЬКО цвету: схема Иттена раздаёт оттенки по видам,
        /// потому что масса детали в фигуре — свойство вида, а не формы.</summary>
        private string _kind;

        public string StyleKey => _styleKey;

        public void Initialize(Shape shape, string colorHex) => Initialize(shape, colorHex, null);

        public void Initialize(Shape shape, string colorHex, string kind)
        {
            _kind = kind;
            if (!string.IsNullOrEmpty(colorHex) && ColorUtility.TryParseHtmlString(colorHex, out var parsed))
                _baseColor = parsed;

            _styleKey = WoodPieceKey.Of(shape);

            var meshFilter = gameObject.AddComponent<MeshFilter>();
            _mesh = PieceMeshBuilder.Build(shape);
            meshFilter.mesh = _mesh;

            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = BlockMaterial.Shared;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            // Тень — отдельный дочерний меш позади детали (объём, просьба владельца).
            _shadow = PieceShadow.Attach(transform, _mesh);

            _mpb = new MaterialPropertyBlock();
            RefreshColor();

            // Ползунки стейтов крутят в Play на живом уровне — без подписки новая разница доехала бы
            // только до деталей, созданных после правки.
            WoodTuning.Changed += RefreshColor;
        }

        private void OnDestroy() => WoodTuning.Changed -= RefreshColor;

        public void Sync(Vec2 position, float rotation)
        {
            var t = transform;
            t.position = new Vector3(position.X, position.Y, 0f);
            t.rotation = Quaternion.Euler(0f, 0f, rotation * Mathf.Rad2Deg);

            // Свет один на всю сцену, поэтому тень смещена в МИРОВЫХ координатах: у кувыркающейся
            // детали локальный сдвиг пришлось бы поворачивать обратно, иначе тень ездит вокруг неё.
            if (_shadow != null)
                _shadow.localPosition = PieceShadow.LocalOffset(rotation, _mesh);
        }

        public void SetState(PieceLifecycle state)
        {
            _state = state;
            RefreshColor();
        }

        public void SetArmed(bool armed)
        {
            _armed = armed;
            RefreshColor();
        }

        // Subtle feedback: dim while Idle (waiting for a tap), full colour once released/placed. When armed in the
        // balance-mode coalescence group, brighten toward white to telegraph "waiting for its pair" (TASK-023).
        //
        // Насколько именно приглушать — теперь не цифра в коде, а настройка (WoodTuning.Tint): владелец
        // разбирает второй стейт («полетел — стал активный») и крутит разницу ползунками в Play.
        private void RefreshColor()
        {
            var state = _armed
                ? PieceVisualState.Armed
                : _state == PieceLifecycle.Idle
                    ? PieceVisualState.Idle
                    : PieceVisualState.Active;

            // Ремап цвета уровня к палитре считается ЗДЕСЬ, а не в Initialize: тумблер крутят в Play,
            // и исходный цвет уровня обязан остаться нетронутым, чтобы его можно было вернуть.
            var tuning = WoodTuning.Current;
            ApplyColor(tuning.Tint(tuning.ResolveLevelColor(_baseColor, _kind), state));
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

            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
