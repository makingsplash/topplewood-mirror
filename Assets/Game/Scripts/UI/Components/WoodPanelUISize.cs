using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ui.Components
{
    /// <summary>
    /// Подаёт в шейдер Game/Wood Panel UI размер элемента в пикселях и состояние нажатия.
    /// <para>
    /// Размер нужен потому, что радиус угла и фаска в шейдере считаются в ПИКСЕЛЯХ:
    /// в долях UV один и тот же радиус даёт у широкой подставки эллипс вместо круга.
    /// Значение шлём через <see cref="MaterialPropertyBlock"/>-аналог для Graphic —
    /// собственный экземпляр материала, иначе кнопки с разными пропорциями затирали бы
    /// размер друг друга через общий материал.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public sealed class WoodPanelUISize : UIBehaviour
    {
        private static readonly int RectSizeProperty = Shader.PropertyToID("_RectSize");
        private static readonly int PressProperty = Shader.PropertyToID("_Press");

        [Tooltip("Насколько гаснет панель под пальцем. 0 — не реагировать.")]
        [SerializeField, Range(0f, 1f)] private float _pressAmount = 1f;

        [Tooltip("Скорость возврата после отпускания, единиц в секунду.")]
        [SerializeField, Range(1f, 40f)] private float _releaseSpeed = 14f;

        private Graphic _graphic;
        private Material _instance;
        private float _press;
        private bool _held;

        private Graphic Graphic => _graphic ??= GetComponent<Graphic>();

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureInstance();
            Apply();
        }

        protected override void OnDisable()
        {
            _held = false;
            _press = 0f;
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            // материал наш, инстанс на элемент — убираем за собой, иначе течёт на каждом открытии экрана
            if (_instance != null)
            {
                if (Application.isPlaying) Destroy(_instance);
                else DestroyImmediate(_instance);
                _instance = null;
            }
            base.OnDestroy();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Apply();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            EnsureInstance();
            Apply();
        }
#endif

        private void Update()
        {
            if (_pressAmount <= 0f) return;

            var target = _held ? _pressAmount : 0f;
            if (Mathf.Approximately(_press, target)) return;

            _press = Mathf.MoveTowards(_press, target, _releaseSpeed * Time.unscaledDeltaTime);
            if (_instance != null) _instance.SetFloat(PressProperty, _press);
        }

        /// <summary>Вызывать из обработчиков нажатия кнопки (см. ButtonWithPointerEvents).</summary>
        public void SetHeld(bool held)
        {
            _held = held;
            // мгновенное нажатие, плавный возврат: задержка на вдавливании читается как лаг ввода
            if (held && _instance != null)
            {
                _press = _pressAmount;
                _instance.SetFloat(PressProperty, _press);
            }
        }

        private void EnsureInstance()
        {
            var graphic = Graphic;
            if (graphic == null || graphic.material == null) return;

            // Материал шарится между кнопками — размер у каждой свой, поэтому нужен экземпляр.
            if (_instance == null || graphic.material != _instance)
            {
                var source = graphic.material == _instance ? _instance : graphic.material;
                if (source == null) return;
                _instance = new Material(source) { name = source.name + " (size)" };
                graphic.material = _instance;
            }
        }

        private void Apply()
        {
            if (_instance == null) return;

            var rect = ((RectTransform)transform).rect;
            // При нулевом размере (элемент ещё не прошёл layout) не пишем: шейдер защищён
            // max(size,1), но записать 0 значит на один кадр показать панель со скруглением
            // во весь элемент — это заметный щелчок при открытии экрана.
            if (rect.width < 1f || rect.height < 1f) return;

            _instance.SetVector(RectSizeProperty, new Vector4(rect.width, rect.height, 0f, 0f));
        }
    }
}
