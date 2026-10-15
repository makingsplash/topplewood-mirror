using System;
using System.Collections.Generic;
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
    ///
    /// TASK-567: второй путь запекания — ПО ОТРЕЗКАМ (<see cref="Bake(StaticMassUnion.BoundarySegment[], Vec2[][], float)"/>)
    /// для слитой визуальной массы статики. У союза блоков бывает несколько колец и дырки, а одна CCW-полилиния этого
    /// не выражает: расстояние берётся до ближайшего из «непогребённых» отрезков (<see cref="StaticMassUnion.Boundary"/>),
    /// знак — тестом «внутри любого члена», нормаль — по тому же правилу ребро/клин. Путь по кольцу не тронут: детали и
    /// одиночные массы запекаются им байт-в-байт.
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

        /// <summary>Раскладка поля по отрезкам: размер текстуры, угол и шаг тексела. Отдельно от запекания — тест
        /// инвентаризации меряет по ней память и время без GPU (батч-тесты идут без графики, RGBAHalf там может не быть).</summary>
        public readonly struct SegmentLayout
        {
            public readonly int Width;
            public readonly int Height;
            public readonly float OriginX;
            public readonly float OriginY;
            public readonly float Texel;

            public SegmentLayout(int width, int height, float originX, float originY, float texel)
            {
                Width = width;
                Height = height;
                OriginX = originX;
                OriginY = originY;
                Texel = texel;
            }

            /// <summary>Память текстуры RGBAHalf: 4 канала × 2 байта на тексел.</summary>
            public long Bytes => (long)Width * Height * 8;
        }

        /// <summary>TASK-567: запечь поле СЛИТОЙ МАССЫ по отрезкам границы союза. <paramref name="members"/> — мировые
        /// контуры членов (знак — «внутри любого»), <paramref name="texelsPerUnit"/> — плотность поля (у статики её
        /// можно понизить: расстояние до прямого ребра линейно, билинейная выборка держит его нулевую линию точно).
        /// <c>null</c> — отрезков нет или платформа не умеет RGBAHalf; вызывающий тогда рисует блоки поштучно.</summary>
        public static SilhouetteField Bake(StaticMassUnion.BoundarySegment[] boundary, Vec2[][] members, float texelsPerUnit)
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
                return null;
            var data = BakeSegmentsData(boundary, members, texelsPerUnit, out var layout);
            if (data == null)
                return null;

            var texture = new Texture2D(layout.Width, layout.Height, TextureFormat.RGBAHalf, false, true)
            {
                name = "SilhouetteFieldMass",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
                hideFlags = HideFlags.DontUnloadUnusedAsset, // см. Bake(Vec2[]) — владеет вид, держит только MPB
            };
            texture.SetPixelData(data, 0);
            texture.Apply(false, true);

            var w = layout.Width * layout.Texel;
            var h = layout.Height * layout.Texel;
            return new SilhouetteField(texture, new Vector4(layout.OriginX, layout.OriginY, 1f / w, 1f / h));
        }

        /// <summary>CPU-половина <see cref="Bake(StaticMassUnion.BoundarySegment[], Vec2[][], float)"/>: тексели RGBAHalf
        /// и раскладка. Габарит — по членам (отрезки лежат на их границе), та же раскладка, что у пути по кольцу.</summary>
        public static ushort[] BakeSegmentsData(StaticMassUnion.BoundarySegment[] boundary, Vec2[][] members,
            float texelsPerUnit, out SegmentLayout layout)
        {
            layout = default;
            if (boundary == null || boundary.Length == 0 || members == null || members.Length == 0 || texelsPerUnit <= 0f)
                return null;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var poly in members)
            {
                if (poly == null)
                    continue;
                foreach (var v in poly)
                {
                    minX = Mathf.Min(minX, v.X); maxX = Mathf.Max(maxX, v.X);
                    minY = Mathf.Min(minY, v.Y); maxY = Mathf.Max(maxY, v.Y);
                }
            }
            if (minX > maxX || minY > maxY)
                return null;

            var extent = Mathf.Max(maxX - minX, maxY - minY);
            var texel = Mathf.Max(1f / texelsPerUnit, extent / (MaxSide - 2 * Pad));
            var w = Mathf.CeilToInt((maxX - minX) / texel) + 2 * Pad;
            var h = Mathf.CeilToInt((maxY - minY) / texel) + 2 * Pad;
            var originX = minX - Pad * texel;
            var originY = minY - Pad * texel;
            layout = new SegmentLayout(w, h, originX, originY, texel);

            // УЗКАЯ ПОЛОСА (narrow-band SDF). Полный счёт — каждый тексел против каждого отрезка плюс тест «внутри» по
            // всем членам — на крупных массах стоил 50–110 мс (замер TASK-567 §5), а нужен шейдеру только у кромки: фаска,
            // кант, контур и AO насыщаются на своей ширине, тень снаружи силуэта — альфа 0 при любом отрицательном
            // значении. Поэтому каждый отрезок обновляет лишь тексели в своей полосе шириной BandDistance (в порядке
            // обхода и со строгим «<», как EvaluateSegments, — ближайший выбирается тот же), знак считается сканлайном
            // по строке тем же предикатом чётности, а тексели вне всех полос получают ±BandDistance. В полосе поле
            // совпадает с EvaluateSegments побайтно (тест SegmentField_NarrowBand_MatchesFullEvaluationInBand).
            var count = w * h;
            var bestSq = new float[count];
            var nearX = new float[count];
            var nearY = new float[count];
            var edgeN = new int[count];
            var atVertex = new bool[count];
            for (var i = 0; i < count; i++)
                bestSq[i] = float.MaxValue;

            var band = BandDistance;
            var bandSq = band * band;
            for (var si = 0; si < boundary.Length; si++)
            {
                var s = boundary[si];
                float abx = s.B.X - s.A.X, aby = s.B.Y - s.A.Y;
                var ee = Mathf.Max(abx * abx + aby * aby, 1e-12f);
                var x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(s.A.X, s.B.X) - band - originX) / texel - 0.5f));
                var x1 = Mathf.Min(w - 1, Mathf.CeilToInt((Mathf.Max(s.A.X, s.B.X) + band - originX) / texel - 0.5f));
                var y0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(s.A.Y, s.B.Y) - band - originY) / texel - 0.5f));
                var y1 = Mathf.Min(h - 1, Mathf.CeilToInt((Mathf.Max(s.A.Y, s.B.Y) + band - originY) / texel - 0.5f));
                for (var y = y0; y <= y1; y++)
                {
                    var py = originY + (y + 0.5f) * texel;
                    for (var x = x0; x <= x1; x++)
                    {
                        var px = originX + (x + 0.5f) * texel;
                        var t = Mathf.Clamp01(((px - s.A.X) * abx + (py - s.A.Y) * aby) / ee);
                        float cx, cy;
                        if (t <= 0f) { cx = s.A.X; cy = s.A.Y; }
                        else if (t >= 1f) { cx = s.B.X; cy = s.B.Y; }
                        else { cx = s.A.X + abx * t; cy = s.A.Y + aby * t; }
                        float dx = px - cx, dy = py - cy;
                        var dsq = dx * dx + dy * dy;
                        var i = y * w + x;
                        if (dsq > bandSq || dsq >= bestSq[i])
                            continue;
                        bestSq[i] = dsq;
                        nearX[i] = cx;
                        nearY[i] = cy;
                        edgeN[i] = si;
                        atVertex[i] = t <= 0f || t >= 1f;
                    }
                }
            }

            var data = new ushort[count * 4];
            var crossings = new List<float>[members.Length];
            for (var m = 0; m < members.Length; m++)
                crossings[m] = new List<float>();
            var cursor = new int[members.Length];
            for (var y = 0; y < h; y++)
            {
                var py = originY + (y + 0.5f) * texel;
                RowCrossings(members, py, crossings);
                for (var m = 0; m < members.Length; m++)
                    cursor[m] = 0;

                for (var x = 0; x < w; x++)
                {
                    var px = originX + (x + 0.5f) * texel;
                    var inside = false;
                    for (var m = 0; m < members.Length; m++)
                    {
                        // Чётность «сколько пересечений правее px» (строгое px < x, как в InsideAnyMember): xs
                        // отсортированы, курсор двигается только вправо вместе с px.
                        var xs = crossings[m];
                        while (cursor[m] < xs.Count && !(px < xs[cursor[m]]))
                            cursor[m]++;
                        if (((xs.Count - cursor[m]) & 1) == 1)
                            inside = true;
                    }

                    var i = y * w + x;
                    var o = i * 4;
                    if (bestSq[i] == float.MaxValue)
                    {
                        data[o] = Mathf.FloatToHalf(inside ? band : -band);
                        data[o + 1] = 0;
                        data[o + 2] = Mathf.FloatToHalf(1f);
                        data[o + 3] = 0;
                        continue;
                    }

                    var distance = Mathf.Sqrt(bestSq[i]);
                    float nx, ny;
                    if (atVertex[i] && bestSq[i] > 1e-12f)
                    {
                        var sign = inside ? 1f : -1f;
                        nx = (nearX[i] - px) / distance * sign;
                        ny = (nearY[i] - py) / distance * sign;
                    }
                    else
                    {
                        var n = boundary[edgeN[i]].Normal;
                        var len = Mathf.Sqrt(n.X * n.X + n.Y * n.Y);
                        if (len < 1e-6f) { nx = 0f; ny = 1f; }
                        else { nx = n.X / len; ny = n.Y / len; }
                    }
                    data[o] = Mathf.FloatToHalf(inside ? distance : -distance);
                    data[o + 1] = Mathf.FloatToHalf(nx);
                    data[o + 2] = Mathf.FloatToHalf(ny);
                    data[o + 3] = 0;
                }
            }
            return data;
        }

        /// <summary>
        /// TASK-567: ширина полосы, где поле массы считается точно (мировые ед.). Шейдеру расстояние нужно до самой широкой
        /// полосы стиля: фаска, кант, контур и AO насыщаются на <c>ширина × k</c>, тень — на <c>мягкость × k</c>. 1.5 —
        /// потолок ползунков вкладки стиля (AO «Его глубина» ≤ 1 × «Масштаб рисунка» ≤ 1.5, WoodTunerPanel); по
        /// умолчанию самая широкая полоса ≈ 0.08. Глубже полосы внутри стоит +1.5 (всё насыщено), снаружи −1.5 (альфа тени 0).
        /// </summary>
        public const float BandDistance = 1.5f;

        /// <summary>Пересечения строки y = <paramref name="py"/> с рёбрами каждого члена, по возрастанию x. Предикат и
        /// формула — те же, что в <see cref="InsideAnyMember"/>, чтобы знак сканлайна совпал с тестом по точке побайтно.</summary>
        private static void RowCrossings(Vec2[][] members, float py, List<float>[] crossings)
        {
            for (var m = 0; m < members.Length; m++)
            {
                var xs = crossings[m];
                xs.Clear();
                var poly = members[m];
                if (poly == null || poly.Length < 3)
                    continue;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    var a = poly[i];
                    var b = poly[j];
                    if ((a.Y > py) != (b.Y > py))
                        xs.Add(a.X + (py - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                }
                xs.Sort();
            }
        }

        /// <summary>
        /// TASK-567: расстояние со знаком до границы союза и наружная нормаль — правило <see cref="Evaluate"/>, но по
        /// НАБОРУ отрезков: ближайший отрезок с зажатым параметром; на отрезке — его нормаль, в клине конца — направление
        /// на ближайшую точку (со знаком, чтобы снаружи нормаль тоже смотрела наружу). Знак — «внутри любого члена»
        /// (чётность пересечений по отрезкам здесь неверна: коллинеарный дубль общего края считался бы дважды).
        /// Проверка «внутри» — чистая чётность по контуру члена, без допуска на границе: на внешней границе расстояние
        /// там 0, и знак не влияет; на бывшем общем ребре точка всегда внутри одного из двух соседей.
        /// </summary>
        public static void EvaluateSegments(StaticMassUnion.BoundarySegment[] boundary, Vec2[][] members,
            float px, float py, out float signedDistance, out float nx, out float ny)
        {
            var bestSq = float.MaxValue;
            float qx = px, qy = py, ex = 0f, ey = 1f;
            var atVertex = false;
            for (var i = 0; i < boundary.Length; i++)
            {
                var s = boundary[i];
                float abx = s.B.X - s.A.X, aby = s.B.Y - s.A.Y;
                var ee = Mathf.Max(abx * abx + aby * aby, 1e-12f);
                var t = Mathf.Clamp01(((px - s.A.X) * abx + (py - s.A.Y) * aby) / ee);
                float cx, cy;
                if (t <= 0f) { cx = s.A.X; cy = s.A.Y; }
                else if (t >= 1f) { cx = s.B.X; cy = s.B.Y; }
                else { cx = s.A.X + abx * t; cy = s.A.Y + aby * t; }
                float dx = px - cx, dy = py - cy;
                var dsq = dx * dx + dy * dy;
                if (dsq < bestSq)
                {
                    bestSq = dsq;
                    qx = cx; qy = cy;
                    ex = s.Normal.X; ey = s.Normal.Y;
                    atVertex = t <= 0f || t >= 1f;
                }
            }

            var inside = InsideAnyMember(members, px, py);
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

        private static bool InsideAnyMember(Vec2[][] members, float px, float py)
        {
            foreach (var poly in members)
            {
                if (poly == null || poly.Length < 3)
                    continue;
                var inside = false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    var a = poly[i];
                    var b = poly[j];
                    if ((a.Y > py) != (b.Y > py) && px < a.X + (py - a.Y) * (b.X - a.X) / (b.Y - a.Y))
                        inside = !inside;
                }
                if (inside)
                    return true;
            }
            return false;
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
