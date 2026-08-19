using System.Collections.Generic;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Тень под рамкой превью — «картиной с рисунком» на стене. Владелец попросил её отдельно от
    /// теней деталей.
    ///
    /// Сделана НЕ копией спрайта, хотя это первое, что приходит в голову: чёрный силуэт того же
    /// спрайта на рамке размером в четыре единицы читается второй рамкой, а не тенью — резкий край
    /// выдаёт подделку. Поэтому тень здесь — прямоугольный меш под рамкой с тем же шейдером
    /// <c>Game/Wood Piece Shadow</c>, что у деталей: он размывает край по расстоянию до кромки,
    /// и получается мягкое пятно.
    ///
    /// Материал СВОЙ, а не общий с деталями: мягкость в шейдере задана в мировых единицах, а рамка
    /// вчетверо крупнее кубика — на общем материале пришлось бы выбирать, кому из них хорошо.
    /// Здесь мягкость задаётся в долях стороны рамки и переводится в единицы при создании.
    ///
    /// Числа — в <see cref="WoodTuning"/>, крутятся в Play; живые тени помнятся списком, чтобы
    /// ползунок доезжал и до уже созданных.
    /// </summary>
    public static class FrameShadow
    {
        /// <summary>Дальше от камеры, чем рамка (камера смотрит вдоль +Z).</summary>
        private const float ZBehind = 0.05f;

        private static Material _material;

        private readonly struct Live
        {
            public readonly MeshRenderer Renderer;
            public readonly Vector2 Size;

            public Live(MeshRenderer renderer, Vector2 size)
            {
                Renderer = renderer;
                Size = size;
            }
        }

        private static readonly List<Live> LiveShadows = new List<Live>(8);

        /// <summary>
        /// Вешает тень под спрайтовую рамку. Возвращает её рендерер (или null, если вешать не на что
        /// или шейдер тени вырезан стриппингом).
        /// </summary>
        public static MeshRenderer Attach(SpriteRenderer frame)
        {
            if (frame == null || frame.sprite == null)
                return null;

            var material = Material;
            if (material == null)
                return null;

            // Sliced-рамка знает свой размер полем size; у обычного спрайта его берём из габарита.
            var size = frame.drawMode == SpriteDrawMode.Simple
                ? (Vector2)frame.sprite.bounds.size
                : frame.size;
            if (size.x < 1e-4f || size.y < 1e-4f)
                return null;

            var half = size * 0.5f;
            var outline = new[]
            {
                new Vec2(-half.x, -half.y),
                new Vec2(half.x, -half.y),
                new Vec2(half.x, half.y),
                new Vec2(-half.x, half.y),
            };

            var go = new GameObject("FrameShadow");
            go.transform.SetParent(frame.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = PieceMeshBuilder.Build(outline);

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // И порядок, и Z: порядок решает спор со спрайтом рамки, Z — с остальной сценой.
            renderer.sortingLayerID = frame.sortingLayerID;
            renderer.sortingOrder = frame.sortingOrder - 1;

            LiveShadows.Add(new Live(renderer, size));
            ApplyTo(LiveShadows[LiveShadows.Count - 1]);

            // Диагностика намеренно оставлена: тень под рамкой один раз уже «была создана и не
            // видна», и без этих чисел причину пришлось бы искать перебором. Одна строка на уровень.
            Debug.Log($"[wood] тень рамки: рамка {size.x:0.##}×{size.y:0.##}, " +
                      $"сдвиг {renderer.transform.localPosition}, масштаб {renderer.transform.localScale.x:0.###}, " +
                      $"order {renderer.sortingOrder} (рамка {frame.sortingOrder}), " +
                      $"габарит {renderer.bounds.size.x:0.##}×{renderer.bounds.size.y:0.##}, " +
                      $"alpha {material.GetFloat(ShadowAlphaId):0.##}, soft {material.GetFloat(ShadowSoftId):0.###}");
            return renderer;
        }

        /// <summary>Перебрать живые тени рамок и переложить в них текущие числа (ползунки в Play).</summary>
        public static void RefreshLive()
        {
            for (var i = LiveShadows.Count - 1; i >= 0; i--)
            {
                if (LiveShadows[i].Renderer == null)
                {
                    LiveShadows.RemoveAt(i);
                    continue;
                }
                ApplyTo(LiveShadows[i]);
            }
        }

        private static Material Material
        {
            get
            {
                if (_material == null)
                {
                    var shader = Shader.Find("Game/Wood Piece Shadow");
                    if (shader == null)
                        return null;
                    _material = new Material(shader) { name = "FrameShadowMaterial" };
                    _material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                return _material;
            }
        }

        private static void ApplyTo(Live shadow)
        {
            var tuning = WoodTuning.Current;

            // Смещение и мягкость — в ДОЛЯХ стороны рамки, а не в мировых единицах: сдвиг, нормальный
            // для кубика (0.06), под картиной не виден вовсе, а карточка превью ещё и масштабируется
            // под полосу экрана на каждом уровне.
            var t = shadow.Renderer.transform;
            t.localPosition = new Vector3(tuning.FrameShadowOffsetX * shadow.Size.x,
                                          tuning.FrameShadowOffsetY * shadow.Size.y,
                                          ZBehind);
            t.localScale = Vector3.one * tuning.FrameShadowScale;

            var material = shadow.Renderer.sharedMaterial;
            if (material == null)
                return;
            material.SetFloat(ShadowAlphaId, tuning.FrameShadowAlpha);
            material.SetFloat(ShadowSoftId,
                Mathf.Max(0.005f, tuning.FrameShadowSoft * Mathf.Max(shadow.Size.x, shadow.Size.y)));
        }

        private static readonly int ShadowAlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int ShadowSoftId = Shader.PropertyToID("_Soft");
    }
}
