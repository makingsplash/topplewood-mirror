using System;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.View
{
    /// <summary>
    /// Листва по верхним углам экрана — арт владельца, две ветки: своя в левый угол, своя в правый.
    /// Под каждой мягкая тень, чтобы ветка читалась лежащей НА стене, а не наклеенной поверх кадра.
    ///
    /// Точка входа, а не объект сцены: сцена собирается кодом, префабов нет — тот же идиом, что у
    /// <see cref="WoodBackdropPresenter"/>. Привязка к УГЛАМ КАМЕРЫ, а не к мировым координатам:
    /// камера переезжает и меняет масштаб на каждый уровень (<see cref="CameraFramingPresenter"/>),
    /// и ветка, поставленная в мир один раз, на следующем уровне уехала бы за кадр.
    ///
    /// Размер — в ДОЛЯХ ШИРИНЫ кадра, не в мировых единицах: на широком уровне (ортокамера 6.75)
    /// и на узком (5.72) ветка должна занимать одну и ту же часть экрана, иначе на одних уровнях
    /// она лезет на поле, а на других превращается в веточку в углу.
    /// </summary>
    public sealed class WoodFoliagePresenter : IStartable, ILateTickable, IDisposable
    {
        /// <summary>Порядок сортировки: над фоном-стеной (−10000), но под полкой (−100) и деталями.</summary>
        private const int SortingOrder = -9000;

        private const int ShadowSortingOrder = -9001;

        private GameObject _root;
        private Corner _left, _right;
        private UnityEngine.Camera _camera;

        private sealed class Corner
        {
            public Transform Root;
            public SpriteRenderer Sprite;
            public Transform Shadow;
            public Vector2 Anchor;   // (0,1) левый верх, (1,1) правый верх
            public float AspectHW;   // высота / ширина спрайта
        }

        public void Start()
        {
            var left = WoodArt.LeavesLeft;
            var right = WoodArt.LeavesRight;
            if (left == null && right == null)
                return;   // WoodArt уже предупредил в лог

            _root = new GameObject("[Wood] Foliage");
            _camera = UnityEngine.Camera.main;

            if (left != null)
                _left = Build(left, new Vector2(0f, 1f), "Left");
            if (right != null)
                _right = Build(right, new Vector2(1f, 1f), "Right");

            LateTick();
        }

        private Corner Build(Sprite sprite, Vector2 anchor, string name)
        {
            var go = new GameObject("Leaves" + name);
            go.transform.SetParent(_root.transform, false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = SortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Тень — та же картинка, залитая чёрным с прозрачностью и сдвинутая. Отдельный спрайт,
            // а не шейдер теней деталей: там тень строится по МЕШУ детали, а у листвы силуэт живёт
            // в альфе текстуры, и меша с кромкой у неё нет.
            var shadowGo = new GameObject("Shadow");
            shadowGo.transform.SetParent(go.transform, false);
            var shadowRenderer = shadowGo.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = sprite;
            shadowRenderer.sortingOrder = ShadowSortingOrder;
            shadowRenderer.color = new Color(0f, 0f, 0f, WoodTuning.Current.FoliageShadowAlpha);
            shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;

            return new Corner
            {
                Root = go.transform,
                Sprite = renderer,
                Shadow = shadowGo.transform,
                Anchor = anchor,
                AspectHW = sprite.bounds.size.x > 1e-4f ? sprite.bounds.size.y / sprite.bounds.size.x : 1f,
            };
        }

        /// <summary>
        /// Каждый кадр, а не разово: камера переезжает и меняет orthographicSize на каждый уровень,
        /// а окно Game в редакторе меняет aspect на лету. Тот же довод, что у <see cref="WoodBackdropFit"/>.
        /// </summary>
        public void LateTick() => Fit();

        public void Fit()
        {
            if (_camera == null)
                _camera = UnityEngine.Camera.main;
            if (_camera == null || !_camera.orthographic)
                return;

            var tuning = WoodTuning.Current;
            var worldHeight = _camera.orthographicSize * 2f;
            var worldWidth = worldHeight * (_camera.aspect <= 0f ? 1f : _camera.aspect);
            var center = _camera.transform.position;

            Place(_left, tuning, center, worldWidth, worldHeight);
            Place(_right, tuning, center, worldWidth, worldHeight);
        }

        private static void Place(Corner corner, WoodTuning tuning, Vector3 camCenter,
            float worldWidth, float worldHeight)
        {
            if (corner?.Root == null || corner.Sprite.sprite == null)
                return;

            // Ширина ветки — доля ширины кадра; высота следует пропорции картинки.
            var width = worldWidth * Mathf.Max(tuning.FoliageWidth, 0.01f);
            var spriteWidth = corner.Sprite.sprite.bounds.size.x;
            var scale = spriteWidth > 1e-4f ? width / spriteWidth : 1f;
            corner.Root.localScale = new Vector3(scale, scale, 1f);

            var height = width * corner.AspectHW;

            // Угол кадра, потом сдвиг внутрь на долю размера самой ветки: так она свисает из-за
            // края, а не висит квадратом в углу. Отрицательный отступ = вылезает за кадр.
            var left = camCenter.x - worldWidth * 0.5f;
            var top = camCenter.y + worldHeight * 0.5f;

            var x = corner.Anchor.x < 0.5f
                ? left + width * (0.5f - tuning.FoliageOverhang)
                : left + worldWidth - width * (0.5f - tuning.FoliageOverhang);
            var y = top - height * (0.5f - tuning.FoliageOverhang) - worldHeight * tuning.FoliageDropY;

            corner.Root.position = new Vector3(x, y, tuning.FoliageZ);

            // Тень — ДОЧЕРНИЙ объект, поэтому сдвиг задаётся в локальных единицах спрайта, а доли
            // берутся от его собственного размера. Так тень держится за веткой при любом масштабе
            // кадра: и на узком уровне, и на широком она отстаёт на ту же долю своей ширины.
            var spriteHeight = corner.Sprite.sprite.bounds.size.y;
            corner.Shadow.localPosition = new Vector3(
                tuning.FoliageShadowOffsetX * spriteWidth,
                tuning.FoliageShadowOffsetY * spriteHeight,
                0.05f);
            corner.Shadow.localScale = Vector3.one * tuning.FoliageShadowScale;

            var shadowRenderer = corner.Shadow.GetComponent<SpriteRenderer>();
            if (shadowRenderer != null)
                shadowRenderer.color = new Color(0f, 0f, 0f, tuning.FoliageShadowAlpha);
        }

        public void Dispose()
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            _root = null;
            _left = _right = null;
        }
    }
}
