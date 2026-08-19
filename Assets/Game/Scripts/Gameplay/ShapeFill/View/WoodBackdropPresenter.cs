using System;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.View
{
    /// <summary>
    /// Ставит в сцену деревянную стену-фон (спрайт от владельца). До этого фона в игре не было
    /// вообще: у камеры стоял сплошной цвет — на это владелец и указал («фон ты не сделал»).
    ///
    /// Точка входа, а не объект сцены: сцена bringup собирается кодом (см. остальные презентеры),
    /// префабов и ручных ссылок тут нет. Ставить спрайт руками в сцену нельзя ещё и потому, что
    /// камера переезжает на каждый уровень — подгонкой занимается <see cref="WoodBackdropFit"/>.
    /// </summary>
    public sealed class WoodBackdropPresenter : IStartable, IDisposable
    {
        /// <summary>Сортировка глубоко в минус: фон обязан быть под всеми спрайтами (полка, рамка).</summary>
        private const int SortingOrder = -10000;

        private GameObject _root;

        public void Start()
        {
            var sprite = WoodArt.Background;
            if (sprite == null)
                return;   // WoodArt уже предупредил в лог; камера останется с прежним цветом

            _root = new GameObject("[Wood] Backdrop");
            var renderer = _root.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = SortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // UnityEngine.Camera уточняем полностью: короткое «Camera» в этом неймспейсе — namespace.
            _root.AddComponent<WoodBackdropFit>().Bind(UnityEngine.Camera.main, renderer);
        }

        public void Dispose()
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }
}
