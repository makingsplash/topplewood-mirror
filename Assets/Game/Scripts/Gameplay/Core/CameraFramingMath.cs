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

        /// <summary>Union AABB (world space) of every piece's start silhouette, the target slots and the static
        /// geometry — the full content the player must see and reach. Returns false when there is no content.</summary>
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

            if (level.StaticGeometry != null)
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

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        private static void Accumulate(Vec2 v, ref float minX, ref float minY, ref float maxX, ref float maxY)
        {
            if (v.X < minX) minX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.X > maxX) maxX = v.X;
            if (v.Y > maxY) maxY = v.Y;
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
