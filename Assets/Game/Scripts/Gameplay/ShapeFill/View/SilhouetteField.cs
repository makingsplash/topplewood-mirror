using System;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// TASK-557: поле расстояний силуэта, ЗАПЕЧЁННОЕ в текстуру один раз на рендерер. Подменяет в шейдере
    /// <c>Game/Wood Block</c> (вариант <c>_CONCAVE_SDF</c>) цикл по рёбрам контура (<c>SilhouetteEdge</c>, до 64 рёбер на
    /// пиксель) одной билинейной выборкой.
    ///
    /// Почему можно: контур живёт в ОБЪЕКТНОМ пространстве рендерера и не меняется — у вогнутой массы он неподвижен
    /// вообще, у freeform-детали кувыркается вместе с ней, но в её собственных координатах тот же. Поле считается
    /// один раз при создании вида. Замер на телефоне владельца (TASK-557 01-dev-implementer §2): цикл стоил 4.9–8.6 мс
    /// GPU, причём на `burger_stack`/`twin_peaks` это кромка freeform-ДЕТАЛЕЙ, а не массы — поэтому запекается любой
    /// силуэт, а не только масса.
    ///
    /// Что в текселе (RGBAHalf): r — расстояние до контура СО ЗНАКОМ (внутри +, снаружи −), gb — наружная нормаль
    /// кромки. Знак нужен ради билинейной выборки: беззнаковое расстояние у кромки — «галочка», и интерполяция через
    /// ребро дала бы ненулевой минимум, то есть контур уехал бы внутрь. Нормаль и расстояние — ровно правило
    /// <c>SilhouetteEdge</c> (нормаль ребра на ребре, градиент поля в клине вершины), так что в центрах текселей поле
    /// совпадает с аналитикой; между ними — билинейно. Цена: шов фаски на биссектрисе угла размыт на тексель
    /// (<see cref="TexelsPerUnit"/>), а не обрезан пикселем.
    /// </summary>
    public sealed class SilhouetteField : IDisposable
    {
        public static readonly int TextureId = Shader.PropertyToID("_SilhouetteField");

        /// <summary>xy — объектная точка центра нулевого текселя минус полтекселя (угол текстуры), zw — 1/размер в
        /// объектных единицах. z = 0 у рендерера без поля — шейдер тогда считает цикл по рёбрам, как раньше.</summary>
        public static readonly int RectId = Shader.PropertyToID("_SilhouetteFieldRect");

        /// <summary>Текселей на объектную единицу. 64 — тексель ≈ 0.016 ед.: у кампании (масштаб рисунка 0.4) фаска
        /// 0.036 ед. — это два с лишним текселя, контур 0.006 ед. держится на точном нуле поля, а не на текселе.</summary>
        public const float TexelsPerUnit = 64f;

        /// <summary>Потолок стороны текстуры. Габарит шире 16 ед. получает тексель крупнее — плавно, не отказом.</summary>
        public const int MaxSide = 1024;

        /// <summary>Поля текселей за габаритом: снаружи кромки нужно хотя бы по текселю с отрицательным расстоянием,
        /// иначе билинейная выборка у самого края опиралась бы на зажатый край текстуры.</summary>
        private const int Pad = 2;

        public Texture2D Texture { get; private set; }
        public Vector4 Rect { get; }

        private SilhouetteField(Texture2D texture, Vector4 rect)
        {
            Texture = texture;
            Rect = rect;
        }

        /// <summary>Запечь поле CCW-контура (объектные координаты). <c>null</c> — контура нет или платформа не умеет
        /// RGBAHalf; вызывающий тогда остаётся на цикле по рёбрам, то есть на прежнем виде.</summary>
        public static SilhouetteField Bake(Vec2[] ccw)
        {
            if (ccw == null || ccw.Length < 3 || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
                return null;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in ccw)
            {
                minX = Mathf.Min(minX, v.X); maxX = Mathf.Max(maxX, v.X);
                minY = Mathf.Min(minY, v.Y); maxY = Mathf.Max(maxY, v.Y);
            }
            var extent = Mathf.Max(maxX - minX, maxY - minY);
            var texel = Mathf.Max(1f / TexelsPerUnit, extent / (MaxSide - 2 * Pad));
            var w = Mathf.CeilToInt((maxX - minX) / texel) + 2 * Pad;
            var h = Mathf.CeilToInt((maxY - minY) / texel) + 2 * Pad;
            var originX = minX - Pad * texel;
            var originY = minY - Pad * texel;

            var data = new ushort[w * h * 4];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                Evaluate(ccw, originX + (x + 0.5f) * texel, originY + (y + 0.5f) * texel,
                    out var signedDistance, out var nx, out var ny);
                var i = (y * w + x) * 4;
                data[i] = Mathf.FloatToHalf(signedDistance);
                data[i + 1] = Mathf.FloatToHalf(nx);
                data[i + 2] = Mathf.FloatToHalf(ny);
                data[i + 3] = 0;
            }

            var texture = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true)
            {
                name = "SilhouetteField",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
                // Текстура принадлежит виду (Dispose в его OnDestroy) и держится только MaterialPropertyBlock'ом —
                // без флага UnloadUnusedAssets мог бы счесть её мусором при живом рендерере.
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            texture.SetPixelData(data, 0);
            texture.Apply(false, true);

            return new SilhouetteField(texture, new Vector4(originX, originY, 1f / (w * texel), 1f / (h * texel)));
        }

        /// <summary>Положить поле в блок рендерера. Звать после каждого <c>mpb.Clear()</c>.</summary>
        public void Apply(MaterialPropertyBlock mpb)
        {
            if (mpb == null || Texture == null)
                return;
            mpb.SetTexture(TextureId, Texture);
            mpb.SetVector(RectId, Rect);
        }

        public void Dispose()
        {
            // Вне Play (кадры редактора, EditMode-тесты) Destroy запрещён — там только DestroyImmediate.
            if (Texture != null && Application.isPlaying)
                UnityEngine.Object.Destroy(Texture);
            else if (Texture != null)
                UnityEngine.Object.DestroyImmediate(Texture);
            Texture = null;
        }

        /// <summary>
        /// Расстояние со знаком до контура и наружная нормаль кромки в точке (px, py) — правило шейдерного
        /// <c>SilhouetteEdge</c> (SilhouetteSdf.hlsl): ближайшее ребро с зажатым параметром; на самом ребре нормаль
        /// ребра, в клине вершины — направление от точки к вершине (снаружи — обратное, чтобы нормаль смотрела наружу
        /// и там). Знак — чётность пересечений луча с контуром.
        /// </summary>
        public static void Evaluate(Vec2[] ccw, float px, float py, out float signedDistance, out float nx, out float ny)
        {
            var count = ccw.Length;
            var bestSq = float.MaxValue;
            float qx = px, qy = py, ex = 0f, ey = 1f;
            var atVertex = false;
            var inside = false;
            for (var i = 0; i < count; i++)
            {
                var a = ccw[i];
                var b = ccw[(i + 1) % count];
                float abx = b.X - a.X, aby = b.Y - a.Y;
                var ee = Mathf.Max(abx * abx + aby * aby, 1e-12f);
                var t = Mathf.Clamp01(((px - a.X) * abx + (py - a.Y) * aby) / ee);
                float cx, cy;
                if (t <= 0f) { cx = a.X; cy = a.Y; }
                else if (t >= 1f) { cx = b.X; cy = b.Y; }
                else { cx = a.X + abx * t; cy = a.Y + aby * t; }
                float dx = px - cx, dy = py - cy;
                var dsq = dx * dx + dy * dy;
                if (dsq < bestSq)
                {
                    bestSq = dsq;
                    qx = cx; qy = cy;
                    ex = aby; ey = -abx;   // наружу для CCW-контура
                    atVertex = t <= 0f || t >= 1f;
                }

                if ((a.Y > py) != (b.Y > py) && px < a.X + (py - a.Y) * abx / aby)
                    inside = !inside;
            }

            var distance = Mathf.Sqrt(bestSq);
            signedDistance = inside ? distance : -distance;

            if (atVertex && bestSq > 1e-12f)
            {
                var sign = inside ? 1f : -1f;
                nx = (qx - px) / distance * sign;
                ny = (qy - py) / distance * sign;
                return;
            }
            var len = Mathf.Sqrt(ex * ex + ey * ey);
            if (len < 1e-6f) { nx = 0f; ny = 1f; return; }
            nx = ex / len;
            ny = ey / len;
        }
    }
}
