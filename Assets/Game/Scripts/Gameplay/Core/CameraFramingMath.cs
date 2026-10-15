using System;
using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// Pure, Unity-free core of content-driven camera framing (TASK-085), extracted verbatim from
    /// <c>CameraFramingPresenter</c> so the same math has ONE home. The presenter keeps the Unity contact
    /// (Camera.main, transform) and delegates the geometry here; the headless <see cref="LevelDataValidator"/> reuses
    /// it to PREDICT a level's orthoSize (no camera attached) and derive the author-time tap-spacing threshold. A
    /// single source means the predicted zone can never drift from what the live camera actually frames.
    /// </summary>
    public static class CameraFramingMath
    {
        /// <summary>
        /// Половина габарита коробки, зарезервированной в кадре под корпус вентилятора зоны воздуха: столько
        /// он занимает ЗА входной гранью вдоль потока и столько же на каждую щёку поперёк. Вью обязан
        /// в неё уложиться — см. <c>AirZoneView.BuildFan</c>, там ширина и вылет зажимаются этим же
        /// числом. Держать их согласованными обязательно: разъедутся — либо вентилятор снова уедет за
        /// край на узком телефоне, либо кадр начнёт резервировать пустоту и зря отдалять уровень.
        /// </summary>
        public const float AirZoneFanReserve = 1.15f;

        /// <summary>World-space AABB the camera frames (TASK-442). The DYNAMIC content sets the box: every piece's
        /// start silhouette, the target slots, the air zones with their fan reserve and every <c>surface</c> strip
        /// (chute, ramp, slope — the path a piece travels) in full. Closed static blocks only extend the box
        /// VERTICALLY, and only with the part of them that lies inside that extended X span (each outline edge is
        /// clipped to the span) — a wide floor no longer widens the frame, it runs off the screen edges. With no dynamic content at all the
        /// box falls back to the full static AABB. Returns false when there is no content. The result is always a
        /// subset of the union AABB of dynamic + all static, so the derived orthoSize never grows.</summary>
        public static bool TryContentBounds(LevelData level, out Vec2 min, out Vec2 max)
        {
            min = default;
            max = default;

            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;
            var any = false;

            if (level.Pieces != null)
            {
                foreach (var placement in level.Pieces)
                {
                    if (placement.Type?.Shape == null)
                        continue;
                    foreach (var v in Geometry.WorldVertices(placement.Type.Shape, placement.StartPosition, placement.StartRotation))
                    {
                        Accumulate(v, ref minX, ref minY, ref maxX, ref maxY);
                        any = true;
                    }
                }
            }

            var slots = level.Target?.Slots;
            if (slots != null)
            {
                var kindToShape = BuildKindMap(level);
                foreach (var slot in slots)
                {
                    if (kindToShape.TryGetValue(slot.Kind, out var shape) && shape != null)
                    {
                        foreach (var v in Geometry.WorldVertices(shape, slot.Position, slot.Rotation))
                        {
                            Accumulate(v, ref minX, ref minY, ref maxX, ref maxY);
                            any = true;
                        }
                    }
                    else
                    {
                        Accumulate(slot.Position, ref minX, ref minY, ref maxX, ref maxY);
                        any = true;
                    }
                }
            }

            // Зоны воздуха и КОРПУС ВЕНТИЛЯТОРА. Сама зона почти всегда лежит внутри контента, а вот
            // вентилятор стоит СНАРУЖИ входной грани — и на узком телефоне его резало краем кадра:
            // замер piggy_bank дал 0.45 мировой единицы за левым краем при 19.5:9 и 20:9, при 9:16 и
            // на планшете он влезал. Владелец: «в уровнях на мобиле вентилятор должен быть виден
            // всегда» — значит кадр обязан вмещать и его, а не только область силы.
            //
            // Резерв <see cref="AirZoneFanReserve"/> — это КОРОБКА, в которую вью обязан уложить
            // вентилятор (AirZoneView.BuildFan зажимает по ней и ширину, и вылет за грань). Число
            // живёт здесь одно на всех: его видит и живая камера, и предсказание орто-размера в
            // LevelDataValidator, поэтому кадр и предсказание не могут разъехаться.
            if (level.AirZones != null)
            {
                foreach (var zone in level.AirZones)
                {
                    var region = zone.Region;
                    Accumulate(region.Min, ref minX, ref minY, ref maxX, ref maxY);
                    Accumulate(region.Max, ref minX, ref minY, ref maxX, ref maxY);

                    var dir = zone.Direction;
                    var halfX = (region.Max.X - region.Min.X) * 0.5f;
                    var halfY = (region.Max.Y - region.Min.Y) * 0.5f;
                    var centreX = (region.Min.X + region.Max.X) * 0.5f;
                    var centreY = (region.Min.Y + region.Max.Y) * 0.5f;

                    // Опорная функция: расстояние от центра до ВХОДНОЙ грани вдоль потока. Работает
                    // и для косой зоны, а не только для четырёх сторон света.
                    var extentAlong = MathF.Abs(dir.X) * halfX + MathF.Abs(dir.Y) * halfY;
                    var backX = centreX - dir.X * (extentAlong + AirZoneFanReserve);
                    var backY = centreY - dir.Y * (extentAlong + AirZoneFanReserve);

                    // Поперёк потока корпус тоже не шире резерва — берём обе его щеки.
                    var perpX = -dir.Y;
                    var perpY = dir.X;
                    Accumulate(new Vec2(backX + perpX * AirZoneFanReserve, backY + perpY * AirZoneFanReserve),
                        ref minX, ref minY, ref maxX, ref maxY);
                    Accumulate(new Vec2(backX - perpX * AirZoneFanReserve, backY - perpY * AirZoneFanReserve),
                        ref minX, ref minY, ref maxX, ref maxY);
                    any = true;
                }
            }

            // СТАТИКА (TASK-442). Кадр задаёт динамика выше; статика добавляет только ВЫСОТУ и только ту свою
            // часть, что лежит в X-пролёте динамики. Широкий пол (±7 у волны 31–35) прежде задавал ширину кадра
            // и отдалял уровень в 2–3 раза; теперь он обрезается краями экрана и читается бесконечным — решение
            // владельца 2026-10-10. Без динамики (чистая сцена) — прежний полный AABB статики.
            if (level.StaticGeometry != null)
            {
                if (any)
                {
                    // Surface-лента (жёлоб, рампа, склон) — путь детали, а значит часть динамики: входит в коробку
                    // ЦЕЛИКОМ, по X и по Y, ДО того как зафиксирована полоса. Иначе шар chute_cross, поднимаясь по
                    // левому жёлобу за пролётом динамики, уходил за край экрана (TASK-442, решение гейта).
                    foreach (var poly in level.StaticGeometry)
                    {
                        if (poly?.Outline == null || !poly.IsSurface)
                            continue;
                        foreach (var v in poly.Outline)
                            Accumulate(v, ref minX, ref minY, ref maxX, ref maxY);
                    }

                    var slabMin = minX;
                    var slabMax = maxX;
                    foreach (var poly in level.StaticGeometry)
                    {
                        if (poly != null && !poly.IsSurface)
                            AccumulateStaticInSlab(poly, slabMin, slabMax, ref minY, ref maxY);
                    }
                }
                else
                {
                    foreach (var poly in level.StaticGeometry)
                    {
                        if (poly?.Outline == null)
                            continue;
                        foreach (var v in poly.Outline)
                        {
                            Accumulate(v, ref minX, ref minY, ref maxX, ref maxY);
                            any = true;
                        }
                    }
                }
            }

            if (!any)
                return false;

            min = new Vec2(minX, minY);
            max = new Vec2(maxX, maxY);
            return true;
        }

        /// <summary>The orthographic size that frames <paramref name="min"/>..<paramref name="max"/> into the lower
        /// (1 − <paramref name="previewBand"/>) of the viewport (top band reserved for the target preview), with
        /// ≥ <paramref name="margin"/> clearance and clamped to <paramref name="minOrthoSize"/>. Byte-for-byte the
        /// old presenter body — <c>Mathf.Clamp/Max</c> replaced by the Unity-free equivalents.</summary>
        public static float ComputeOrthoSize(Vec2 min, Vec2 max, float aspect,
            float margin, float previewBand, float minOrthoSize)
        {
            var halfH = (max.Y - min.Y) * 0.5f;
            var halfW = (max.X - min.X) * 0.5f;
            var a = aspect <= 0f ? 1f : aspect;
            var usable = Clamp(1f - previewBand, 0.1f, 1f);
            var sizeForHeight = (halfH + margin) / usable;
            var sizeForWidth = (halfW / a) + margin;
            return MathF.Max(minOrthoSize, MathF.Max(sizeForHeight, sizeForWidth));
        }

        /// <summary>
        /// The full frame (TASK-539): <see cref="TryContentBounds"/> + <see cref="ComputeOrthoSize"/>, the camera centred
        /// on the content and raised by size·band for the preview band — and, when <paramref name="hud"/> is given,
        /// the level's figure (target slots) and pieces (start poses) kept out of that HUD corner. ONE entry point for
        /// the live presenter, the validator's prediction and the editor capture, so all three frame identically.
        ///
        /// The corner is freed with the cheapest move first: the vertical slack a width-bound frame already has
        /// (content raised, never into the preview band), then zoom-out by the smallest orthoSize that clears.
        /// A level whose content does not reach the corner gets exactly the frame it had before. Both moves only
        /// push the corner away from the content, so the smallest clearing size is found by bisection.
        /// <paramref name="safeSide"/>/<paramref name="safeBottom"/> — safe-area insets as viewport fractions.
        /// </summary>
        public static bool TryFrame(LevelData level, float aspect, float margin, float previewBand, float minOrthoSize,
            HudCornerZone? hud, float safeSide, float safeBottom, out float orthoSize, out Vec2 centre)
            => TryFrame(level, aspect, margin, previewBand, minOrthoSize, hud, safeSide, safeBottom,
                null, default, out orthoSize, out centre);

        /// <summary>
        /// <see cref="TryFrame(LevelData,float,float,float,float,HudCornerZone?,float,float,out float,out Vec2)"/> plus
        /// the TOP-corner buttons (TASK-563): the figure and pieces are also kept out of every <paramref name="topZones"/>
        /// quadrant. The top zones normally live inside the preview band, which content never enters, so the frame is
        /// left exactly as the bottom-corner pass made it; only a zone that reaches below the band edge (a deep cutout,
        /// a bigger button) and actually covers content makes the camera zoom out — by the smallest size at which
        /// every zone, bottom and top, is clear. <paramref name="topSafe"/> are this device's safe-area insets as
        /// viewport fractions (left/right for the zone's side, top for its edge). <c>topSafe.Top</c> also moves the
        /// preview band down under the safe top (TASK-576): the frame reserves band + safeTop, so the target card
        /// (<see cref="PreviewBandCentreViewportY"/>) sits below a notch/status bar, in line with the top buttons.
        /// With topSafe.Top = 0 the frame is bit-for-bit the short overload's.
        /// </summary>
        public static bool TryFrame(LevelData level, float aspect, float margin, float previewBand, float minOrthoSize,
            HudCornerZone? hud, float safeSide, float safeBottom, IReadOnlyList<HudCornerZone> topZones,
            SafeInsets topSafe, out float orthoSize, out Vec2 centre)
        {
            var band = previewBand + topSafe.Top;
            if (!TryFrameBottom(level, aspect, margin, band, minOrthoSize, hud, safeSide, safeBottom,
                    out orthoSize, out centre))
                return false;
            if (topZones == null || topZones.Count == 0)
                return true;

            var a = aspect <= 0f ? 1f : aspect;
            if (TopZonesClear(level, orthoSize, centre, a, topZones, topSafe, out var hi))
                return true;

            // Zooming out moves every zone's inner X edge outward (and its bottom up, while the band rule keeps the
            // content below the band): past `hi` all zones are clear of all content, so the smallest clearing size is
            // found by bisection over the full frame — the bottom-corner pass re-run at each size, so its corner stays
            // clear too.
            var lo = orthoSize;
            hi = MathF.Max(hi, lo);
            for (var i = 0; i < 40; i++)
            {
                var mid = (lo + hi) * 0.5f;
                TryFrameBottom(level, aspect, margin, band, mid, hud, safeSide, safeBottom, out var s, out var c);
                if (TopZonesClear(level, s, c, a, topZones, topSafe, out _)) hi = mid;
                else lo = mid;
            }
            return TryFrameBottom(level, aspect, margin, band, hi, hud, safeSide, safeBottom,
                out orthoSize, out centre);
        }

        /// <summary>
        /// Viewport Y of the preview band's centre — where the target card is anchored (TASK-576): the band hangs
        /// under the safe top, so its centre is 1 − safeTop − band/2. The one home of that formula: the view and the
        /// tests read it here, so the card can never drift from the band the frame reserves.
        /// </summary>
        public static float PreviewBandCentreViewportY(float previewBand, float safeTop) => 1f - safeTop - previewBand * 0.5f;

        /// <summary>Safe-area insets as viewport fractions of each screen edge. <see cref="Top"/> also shifts the
        /// preview band down under the safe top (TASK-576).</summary>
        public readonly struct SafeInsets
        {
            public readonly float Left, Right, Bottom, Top;

            public SafeInsets(float left, float right, float bottom, float top)
            {
                Left = left;
                Right = right;
                Bottom = bottom;
                Top = top;
            }

            public float Side(HudCornerZone zone) => zone.Right ? Right : Left;
            public float Edge(HudCornerZone zone) => zone.Top ? Top : Bottom;
        }

        // True when no top zone covers content in this frame. `clearAt` is an orthoSize past which every zone's inner
        // X edge lies beyond all content — the bisection's upper bound.
        private static bool TopZonesClear(LevelData level, float size, Vec2 centre, float aspect,
            IReadOnlyList<HudCornerZone> zones, SafeInsets safe, out float clearAt)
        {
            var clear = true;
            clearAt = size;
            foreach (var zone in zones)
            {
                zone.ToViewport(aspect, safe.Side(zone), safe.Edge(zone), out var side, out var depth);
                var kx = (1f - 2f * side) * aspect;
                if (kx <= 0f)
                    continue; // a zone over half the screen cannot be freed by zooming
                var mirror = zone.Right ? 1f : -1f;
                var polys = ContentPolygons(level, mirror);
                if (ContentInTopCorner(polys, size, centre, aspect, mirror, side, depth))
                    clear = false;
                // Inner edge x0 = mirror·cx + kx·s, and the centre X is the content centre at every size, so the edge
                // passes the content's far X at s = (MaxX − mirror·cx)/kx.
                clearAt = MathF.Max(clearAt, (MaxX(polys) - mirror * centre.X + 2e-3f) / kx);
            }
            return clear;
        }

        private static bool ContentInTopCorner(List<Vec2[]> polys, float size, Vec2 centre, float aspect,
            float mirror, float side, float depth)
        {
            var x0 = mirror * centre.X + (1f - 2f * side) * aspect * size;
            var y0 = centre.Y + (1f - 2f * depth) * size;
            return HighestInCorner(polys, x0) > y0;
        }

        // The TASK-539 bottom-corner frame; `minOrthoSize` doubles as the zoom floor the top pass bisects on.
        private static bool TryFrameBottom(LevelData level, float aspect, float margin, float previewBand,
            float minOrthoSize, HudCornerZone? hud, float safeSide, float safeBottom, out float orthoSize,
            out Vec2 centre)
        {
            orthoSize = 0f;
            centre = default;
            if (!TryContentBounds(level, out var min, out var max))
                return false;

            var size = ComputeOrthoSize(min, max, aspect, margin, previewBand, minOrthoSize);
            var cx = (min.X + max.X) * 0.5f;
            var cy = (min.Y + max.Y) * 0.5f;
            orthoSize = size;
            centre = new Vec2(cx, cy + size * previewBand);
            if (hud == null)
                return true;

            var a = aspect <= 0f ? 1f : aspect;
            hud.Value.ToViewport(a, safeSide, safeBottom, out var side, out var top);
            // How fast the zone's inner X / top Y move per unit of orthoSize, relative to the camera centre.
            var kx = (1f - 2f * side) * a;
            var ky = 2f * top - 1f + previewBand;
            // A zone over half the screen cannot be freed by zooming — leave the frame as it was.
            if (kx <= 0f || ky >= 0f)
                return true;

            var polys = ContentPolygons(level, hud.Value.Right ? 1f : -1f);
            var sx = hud.Value.Right ? cx : -cx; // the corner is handled as the right one; the left is mirrored
            var height = max.Y - min.Y;
            var usable = Clamp(1f - previewBand, 0.1f, 1f);

            // Solved with a strict world gap on BOTH axes: bisection converges ONTO the boundary — in Y, or in X where the
            // zone's inner edge passes a content vertex — and a frame that touches the corner exactly is flipped back
            // into it by float rounding (3 levels at 19.5:9 did: two on the Y boundary, garden_fountain on the X one).
            const float gap = 1e-3f;
            float Need(float s) => cy + ky * s + gap - LowestInCorner(polys, sx + kx * s - gap);
            float Slack(float s) => MathF.Max(0f, s * usable - height * 0.5f - margin);

            var need = Need(size);
            if (need > Slack(size))
            {
                // Past this size the zone's inner edge is right of every content vertex: always clear.
                var hi = MathF.Max(size, (MaxX(polys) - sx + 2f * gap) / kx);
                var lo = size;
                for (var i = 0; i < 40; i++)
                {
                    var mid = (lo + hi) * 0.5f;
                    if (Need(mid) > Slack(mid)) lo = mid;
                    else hi = mid;
                }
                size = hi;
                need = Need(size);
            }

            var shift = need > 0f ? MathF.Min(need, Slack(size)) : 0f;
            orthoSize = size;
            centre = new Vec2(cx, cy + size * previewBand - shift);
            return true;
        }

        // The polygons the HUD corner must stay clear of: every piece at its start pose and every target slot (the
        // figure). X is multiplied by mirrorX so a left corner is solved as a right one.
        private static List<Vec2[]> ContentPolygons(LevelData level, float mirrorX)
        {
            var polys = new List<Vec2[]>();
            if (level.Pieces != null)
            {
                foreach (var placement in level.Pieces)
                {
                    if (placement.Type?.Shape != null)
                        polys.Add(Mirror(Geometry.WorldVertices(
                            placement.Type.Shape, placement.StartPosition, placement.StartRotation), mirrorX));
                }
            }

            var slots = level.Target?.Slots;
            if (slots != null)
            {
                var kindToShape = BuildKindMap(level);
                foreach (var slot in slots)
                {
                    polys.Add(Mirror(kindToShape.TryGetValue(slot.Kind, out var shape) && shape != null
                        ? Geometry.WorldVertices(shape, slot.Position, slot.Rotation)
                        : new[] { slot.Position }, mirrorX));
                }
            }
            return polys;
        }

        private static Vec2[] Mirror(Vec2[] vertices, float mirrorX)
        {
            if (mirrorX > 0f)
                return vertices;
            var result = new Vec2[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
                result[i] = new Vec2(-vertices[i].X, vertices[i].Y);
            return result;
        }

        // Lowest Y of the content that lies at x ≥ x0 (+∞ when none does) — the edge clip of the static slab, with the
        // slab open to the right.
        private static float LowestInCorner(List<Vec2[]> polys, float x0)
        {
            CornerYRange(polys, x0, out var minY, out _);
            return minY;
        }

        // Highest Y of the content that lies at x ≥ x0 (−∞ when none does) — the top-corner twin.
        private static float HighestInCorner(List<Vec2[]> polys, float x0)
        {
            CornerYRange(polys, x0, out _, out var maxY);
            return maxY;
        }

        private static void CornerYRange(List<Vec2[]> polys, float x0, out float minY, out float maxY)
        {
            minY = float.MaxValue;
            maxY = float.MinValue;
            foreach (var outline in polys)
            {
                if (outline.Length == 1)
                {
                    ClipEdgeToSlabY(outline[0], outline[0], x0, float.MaxValue, ref minY, ref maxY);
                    continue;
                }
                for (var i = 0; i < outline.Length; i++)
                    ClipEdgeToSlabY(outline[i], outline[(i + 1) % outline.Length], x0, float.MaxValue, ref minY, ref maxY);
            }
        }

        private static float MaxX(List<Vec2[]> polys)
        {
            var maxX = float.MinValue;
            foreach (var outline in polys)
                foreach (var v in outline)
                    if (v.X > maxX) maxX = v.X;
            return maxX;
        }

        /// <summary>
        /// Does the figure or any piece reach into the HUD corner of the given frame? The measurement behind the
        /// TASK-539 level list and its tests — the same geometry <see cref="TryFrame"/> clears. Bottom and top
        /// (TASK-563) corners alike; <paramref name="safeEdge"/> is the safe inset of the zone's own top/bottom edge.
        /// </summary>
        public static bool ContentInHudCorner(LevelData level, float orthoSize, Vec2 centre, float aspect,
            HudCornerZone hud, float safeSide, float safeEdge)
        {
            var a = aspect <= 0f ? 1f : aspect;
            hud.ToViewport(a, safeSide, safeEdge, out var side, out var depth);
            var mirror = hud.Right ? 1f : -1f;
            var polys = ContentPolygons(level, mirror);
            if (hud.Top)
                return ContentInTopCorner(polys, orthoSize, centre, a, mirror, side, depth);
            var x0 = mirror * centre.X + (1f - 2f * side) * a * orthoSize;
            var y1 = centre.Y + (2f * depth - 1f) * orthoSize;
            return LowestInCorner(polys, x0) < y1;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        private static void Accumulate(Vec2 v, ref float minX, ref float minY, ref float maxX, ref float maxY)
        {
            if (v.X < minX) minX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.X > maxX) maxX = v.X;
            if (v.Y > maxY) maxY = v.Y;
        }

        // Y-экстремумы части статики внутри полосы x∈[xmin,xmax]. У многоугольника, обрезанного выпуклой полосой,
        // экстремумы по Y лежат на концах обрезанных рёбер (новые рёбра на x=xmin/xmax соединяют те же точки),
        // поэтому клипа рёбер достаточно и для вогнутого контура. Только замкнутые блоки: surface-лента сюда не
        // приходит, она уже целиком в коробке.
        private static void AccumulateStaticInSlab(Polygon poly, float xmin, float xmax, ref float minY, ref float maxY)
        {
            var outline = poly?.Outline;
            if (outline == null || outline.Length == 0)
                return;

            if (outline.Length == 1)
            {
                ClipEdgeToSlabY(outline[0], outline[0], xmin, xmax, ref minY, ref maxY);
                return;
            }

            for (var i = 0; i + 1 < outline.Length; i++)
                ClipEdgeToSlabY(outline[i], outline[i + 1], xmin, xmax, ref minY, ref maxY);
            ClipEdgeToSlabY(outline[outline.Length - 1], outline[0], xmin, xmax, ref minY, ref maxY);
        }

        // Liang–Barsky по одной оси: обрезает отрезок a→b полосой x∈[xmin,xmax] и расширяет minY/maxY концами
        // обрезанной части. Ребро целиком вне полосы ничего не даёт.
        private static void ClipEdgeToSlabY(Vec2 a, Vec2 b, float xmin, float xmax, ref float minY, ref float maxY)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            float tEnter, tExit;
            if (dx == 0f)
            {
                if (a.X < xmin || a.X > xmax)
                    return;
                tEnter = 0f;
                tExit = 1f;
            }
            else
            {
                var t0 = (xmin - a.X) / dx;
                var t1 = (xmax - a.X) / dx;
                tEnter = MathF.Max(0f, MathF.Min(t0, t1));
                tExit = MathF.Min(1f, MathF.Max(t0, t1));
                if (tEnter > tExit)
                    return;
            }

            var y0 = a.Y + dy * tEnter;
            var y1 = a.Y + dy * tExit;
            if (y0 < minY) minY = y0;
            if (y0 > maxY) maxY = y0;
            if (y1 < minY) minY = y1;
            if (y1 > maxY) maxY = y1;
        }

        // Slots carry only a Kind; recover each Kind's shape from the pieces the level ships (mirrors
        // TargetPreviewPresenter.BuildKindMap / the former CameraFramingPresenter copy).
        private static IReadOnlyDictionary<string, Shape> BuildKindMap(LevelData level)
        {
            var map = new Dictionary<string, Shape>();
            if (level.Pieces == null)
                return map;

            foreach (var placement in level.Pieces)
            {
                if (placement.Type?.Shape != null && !map.ContainsKey(placement.Type.Kind))
                    map[placement.Type.Kind] = placement.Type.Shape;
            }
            return map;
        }
    }
}
