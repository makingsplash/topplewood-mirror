using System.Collections.Generic;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Renders the target as a small "picture-on-the-box" preview overlay (RFC §4.2, GAME_RULES §7): the same
    /// pieces the player drops, drawn in their target poses in real shape+colour, centred, uniformly scaled
    /// (<see cref="TargetPreviewConfig.Scale"/>) and anchored to the centre of the preview band above the field —
    /// the band hangs under the safe top (TASK-576), so the card stays clear of a notch / status bar.
    /// Pure static reference — no collisions, no physics tick, not in the save snapshot. The domain never
    /// references it, so headless simulation is unaffected.
    /// A canvas backdrop (TASK-110/238/267, <see cref="TargetPreviewConfig.BackdropEnabled"/>) is drawn behind
    /// the silhouette so it reads apart from the screen background. In the shipping build it is a light-wood
    /// TEXTURE sprite (<see cref="WoodArt.Canvas"/>, TASK-267 owner verdict: a solid tone read flat/foreign)
    /// framed by the wooden sprite: the frame's centre is transparent and the wood canvas shows through it. When
    /// the canvas sprite is absent it degrades to the former warm-parchment mesh
    /// (<see cref="TargetPreviewConfig.BackdropColorHex"/>, <c>#EBE2CF</c>). It lives inside this preview root,
    /// so it shares the band anchor and auto-fit scale and adds nothing to physics/save/poseHash.
    /// </summary>
    public sealed class TargetPreviewView : MonoBehaviour
    {
        private static readonly Color DefaultColor = new Color(0.29f, 0.56f, 0.89f); // blue, mirrors PieceView

        // Local +z nudge of the backdrop so it renders behind the slot meshes (opaque depth-order, same trick
        // StaticGeometryView uses). Kept a private const — the ticket asked for colour/alpha/padding config, not z.
        private const float BackdropZOffset = 0.1f;

        // TASK-238: кремовый холст сидит ЧУТЬ позади рамки (больше z = дальше от камеры), но БЛИЖЕ, чем
        // тень рамки (FrameShadow.ZBehind = 0.05 в локали рамки). Так холст закрывает тёмную сердцевину
        // тени, которая иначе просвечивала бы сквозь ставший прозрачным центр рамки, не задевая вынос
        // тени на стену. Держим < 0.05, чтобы порядок «тень → холст → рамка → детали» не сломался.
        private const float BackdropCanvasBehind = 0.025f;

        public void Initialize(
            TargetFigure target,
            IReadOnlyDictionary<string, PieceType> kinds,
            TargetPreviewConfig config,
            UnityEngine.Camera camera,
            float previewBandFraction,
            CameraFramingMath.SafeInsets safe)
        {
            if (target?.Slots == null || kinds == null || config == null)
                return;

            ComputeExtent(target, out var figureCenter, out var figureSize);

            // TASK-110: the white square backdrop side. max-axis (a square, not the bbox rectangle) + padding.
            // S ≥ each figure axis, so switching the auto-fit denominator to it only shrinks scale (Scale stays
            // the ceiling), and the whole square is guaranteed inside the reserved band on every aspect.
            var backdropSide = Mathf.Max(figureSize.x, figureSize.y) + 2f * config.BackdropPadding;

            var t = transform;
            if (camera != null)
            {
                // Anchor to the CENTRE of the reserved top band (TASK-033): the framing presenter keeps a
                // PreviewBandFraction band of the viewport empty for this card, hung under the safe top (TASK-576:
                // a notch / status bar pushed the card under it, while the top buttons already sat in the safe area).
                // band=0.20, safeTop=0 ⇒ viewportY=0.90 (the legacy value, bit for bit).
                var band = Mathf.Clamp01(previewBandFraction);
                var anchorViewportY = CameraFramingMath.PreviewBandCentreViewportY(band, safe.Top);
                var anchor = camera.ViewportToWorldPoint(
                    new Vector3(config.AnchorViewportX, anchorViewportY, config.CameraDistance));
                anchor.z = config.ZPlane;
                t.position = anchor;

                // Auto-fit into the band: config.Scale is the CEILING, never exceeded. Shrink so the figure
                // bbox (with padding) lies fully inside the band's world extents; clamp to MinScale.
                var orthoSize = camera.orthographicSize;
                var aspect = camera.aspect <= 0f ? 1f : camera.aspect;
                var bandWorldH = band * 2f * orthoSize;
                var bandWorldW = 2f * orthoSize * aspect;
                const float pad = 0.85f; // keep the card off the band edges
                // With the backdrop on, fit the SQUARE (S×S) into the band, not the figure bbox: the square is
                // wider than the bbox on the short axis, so fitting the bbox would let the square spill out of
                // the band on non-square figures (Готово №2). S ≥ each axis ⇒ fit only decreases (Scale ceiling
                // intact). Backdrop off ⇒ the legacy figure-bbox path is unchanged (no regression).
                // Рамка шире белого квадрата на FramePadFraction с каждой стороны, и раньше подгонка
                // её не учитывала: карточка «влезала» по квадрату, а деревянный багет вылезал за
                // верхний край экрана и обрезался. Проявилось, когда потолок размера подняли до 0.75.
                var framePad = WoodArt.Frame != null ? 1f + 2f * WoodArt.FramePadFraction : 1f;
                var fitExtentX = (config.BackdropEnabled ? backdropSide : figureSize.x) * framePad;
                var fitExtentY = (config.BackdropEnabled ? backdropSide : figureSize.y) * framePad;
                var fit = Mathf.Min(
                    bandWorldW * pad / Mathf.Max(fitExtentX, 1e-3f),
                    bandWorldH * pad / Mathf.Max(fitExtentY, 1e-3f));
                // TASK-563: the card shares the band with the top-corner buttons (RESTART, settings gear) — keep it
                // between their inner edges. The zones reach well past the band's middle, where the card is anchored,
                // so the clamp is horizontal only. On today's phones the band height binds first (the card spans
                // ~31–69 % of the width at 21:9, the corners end at ~24 %), so this changes nothing until a wider card.
                var halfRoom = TopCornerHalfRoom(aspect, config.AnchorViewportX, safe);
                fit = Mathf.Min(fit, 2f * halfRoom * bandWorldW / Mathf.Max(fitExtentX, 1e-3f));
                // Потолок размера — из настроек стиля, а не из конфига: владелец крутит его
                // ползунком в Play вместе с остальным деревом. Конфиг остаётся запасным значением.
                var ceiling = WoodTuning.Current.PreviewScale > 0f
                    ? WoodTuning.Current.PreviewScale
                    : config.Scale;
                var scale = Mathf.Clamp(Mathf.Min(ceiling, fit), config.MinScale, ceiling);
                t.localScale = Vector3.one * scale;
            }
            else
            {
                // Bringup fallback (no Camera.main): keep legacy behaviour so the preview never crashes.
                t.position = new Vector3(0f, 0f, config.ZPlane);
                t.localScale = Vector3.one * config.Scale;
            }

            // TASK-110: white square backdrop, created FIRST (before the slots) at a small +z nudge so it draws
            // BEHIND the slot meshes (camera looks along +Z ⇒ larger z = farther). The whole preview root sits at
            // ZPlane = -0.5, so the backdrop stays in front of the field and background, behind the silhouette.
            if (config.BackdropEnabled)
                BuildBackdrop(t, backdropSide, config);

            foreach (var slot in target.Slots)
            {
                if (!kinds.TryGetValue(slot.Kind, out var type) || type?.Shape == null)
                    continue;

                var child = new GameObject($"PreviewSlot_{slot.Kind}");
                var childTransform = child.transform;
                childTransform.SetParent(t, false);
                childTransform.localPosition = new Vector3(
                    slot.Position.X - figureCenter.x,
                    slot.Position.Y - figureCenter.y,
                    0f);
                childTransform.localRotation = Quaternion.Euler(0f, 0f, slot.Rotation * Mathf.Rad2Deg);

                var meshFilter = child.AddComponent<MeshFilter>();
                meshFilter.mesh = PieceMeshBuilder.Build(type.Shape);

                // TASK-368: картинка-цель рисует freeform-деталь так же, как поле, — без шва между частями.
                var silhouette = PieceMeshBuilder.SeamlessSilhouette(type.Shape);
                var packed = silhouette == null ? null : BlockMaterial.PackSilhouette(silhouette);

                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = packed != null ? BlockMaterial.ConcaveShared : BlockMaterial.Shared;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var color = DefaultColor;
                if (!string.IsNullOrEmpty(type.Color) && ColorUtility.TryParseHtmlString(type.Color, out var parsed))
                    color = parsed;
                color.a = 1f; // full opacity — a readable reference picture, unlike the paler on-field ghost.

                // Картинка-цель обязана быть того же цвета, что детали на поле, иначе игрок сверяет
                // синее с зелёным. Поэтому ремап палитры (WoodTuning.PaletteToGame) идёт и здесь.
                // Вид ОБЯЗАН попасть в слот: цветовая схема уровня раздаёт оттенки по видам, и без
                // него превью красилось прежней общей палитрой — «фигуры другого цвета, чем у нас».
                _slots.Add(new Slot
                {
                    Renderer = renderer,
                    LevelColor = color,
                    Kind = slot.Kind,
                    Ordinal = _slots.Count,
                    Silhouette = packed,
                    SilhouetteCount = packed == null ? 0 : silhouette.Length,
                    SilhouetteField = packed == null ? null : SilhouetteField.Bake(silhouette),
                });
            }

            Repaint();

            // Палитру крутят ползунком в Play — без подписки превью осталось бы в старых цветах,
            // пока поле уже перекрасилось. Сначала снимаем: Initialize зовётся заново на каждый
            // уровень, и без этого подписки копились бы.
            WoodTuning.Changed -= Repaint;
            WoodTuning.Changed += Repaint;
        }

        private void OnDestroy()
        {
            WoodTuning.Changed -= Repaint;
            foreach (var slot in _slots)
                slot.SilhouetteField?.Dispose();
        }

        private struct Slot
        {
            public MeshRenderer Renderer;
            public Color LevelColor;
            public string Kind;

            /// <summary>TASK-559: номер слота — фаза волокна этой детали превью (доска — по виду, как на поле).</summary>
            public int Ordinal;

            /// <summary>TASK-368: упакованный силуэт freeform-детали (null у остальных форм).</summary>
            public Vector4[] Silhouette;
            public int SilhouetteCount;

            /// <summary>TASK-557: поле расстояний того же силуэта (кромка одной выборкой); null — цикл по рёбрам.</summary>
            public SilhouetteField SilhouetteField;
        }

        private readonly List<Slot> _slots = new List<Slot>();

        /// <summary>Перекрасить детали превью под текущую палитру. Цвет уровня помним исходным —
        /// выключенный тумблер должен вернуть авторские цвета.</summary>
        private void Repaint()
        {
            var tuning = WoodTuning.Current;
            var mpb = new MaterialPropertyBlock();
            foreach (var slot in _slots)
            {
                if (slot.Renderer == null)
                    continue;
                var color = tuning.ResolveLevelColor(slot.LevelColor, slot.Kind);
                color.a = 1f;
                // Блок один на все слоты: Clear, чтобы контур freeform-слота не доехал до следующего слота.
                mpb.Clear();
                mpb.SetColor(BlockMaterial.ColorProperty, color);
                mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
                // TASK-559: превью несёт те же доски, что детали на поле, — иначе оно читалось бы другой фигурой.
                WoodTuning.SetGrainPick(mpb, tuning.GrainPick(slot.Kind, slot.Ordinal));
                if (slot.Silhouette != null)
                    BlockMaterial.SetSilhouette(mpb, slot.Silhouette, slot.SilhouetteCount, slot.SilhouetteField);
                slot.Renderer.SetPropertyBlock(mpb);
            }
        }

        /// <summary>
        /// Build the preview backdrop as a child of the preview root, centred on the figure (already at
        /// local (0,0)). Two shapes in the shipping (framed) case, one in the fallback:
        /// <list type="bullet">
        /// <item>Wooden frame present (shipping): a light-wood CANVAS (<see cref="BuildCanvas"/>) sits behind the
        /// frame and the wooden frame sprite (9-slice) draws on top. The frame sprite's centre is transparent
        /// (TASK-238 re-issue, the former baked-white centre → alpha 0), so the canvas shows through the window
        /// while the wooden border overlaps its edges. The canvas is the wood-texture sprite
        /// <see cref="WoodArt.Canvas"/> (TASK-267 owner verdict — a solid tone read flat); when that sprite is
        /// absent it degrades to the former warm-parchment mesh coloured by
        /// <see cref="TargetPreviewConfig.BackdropColorHex"/> (<c>#EBE2CF</c>).</item>
        /// <item>No frame sprite (bringup fallback, <c>WoodArt.Frame == null</c>): a single coloured square
        /// mesh at <see cref="BackdropZOffset"/>, same colour source — no regression from the legacy path.</item>
        /// </list>
        /// All rendered OPAQUE via the shared block material (<paramref name="side"/> = max-axis + 2·padding;
        /// alpha forced to 1 — the opaque path does not blend alpha; translucency stays TASK-111 tech-debt).
        /// </summary>
        private static void BuildBackdrop(Transform parent, float side, TargetPreviewConfig config)
        {
            var frame = WoodArt.Frame;
            if (frame != null)
            {
                // Рамка шире холста на FramePadFraction с каждой стороны — багет обязан лечь ПОВЕРХ края
                // холста, иначе деревянный борт налез бы на детали превью.
                var frameSize = side * (1f + 2f * WoodArt.FramePadFraction);

                // TASK-238/267: холст ЗА рамкой. Центр спрайта рамки прозрачен (перевыпуск: запечённый белый
                // центр → alpha 0), поэтому холст виден сквозь окно, а деревянный борт накрывает его края.
                // TASK-267 (вердикт владельца: сплошной тон плохо смотрится) — холст стал СПРАЙТОМ деревянной
                // фактуры (C2 «limed oak darker», не сливается с ореховой стеной) вместо однотонного меша;
                // спрайта нет ⇒ деградируем на прежний кремовый меш (см. BuildCanvas).
                //
                // TASK-267 доработка (вердикт владельца по живому кадру: «фактура ок, но бэк выходит за
                // рамки»): холст НЕ тянем во всю рамку (frameSize). У wood_frame.png скруглённые ВНЕШНИЕ углы
                // с альфой — квадратный холст размером в рамку выглядывал светлыми углами из-под них. Прежняя
                // посылка «лишнее скрыто непрозрачным багетом» ложна на ВНЕШНЕМ контуре с альфа-скруглением.
                // Ужимаем до СЕРЕДИНЫ опакового кольца багета: side·(1+FramePadFraction). Геометрия (окно ≈
                // side, внешняя кромка = frameSize = side·1.28): окно покрыто с запасом (+0.07·side на сторону
                // по осям), но углы квадрата холста (диаг. радиус side·1.14/2·√2 ≈ 0.806·side) остаются
                // глубоко под опаковым деревом — внешний угол рамы на диаг. радиусе ≈ 0.905·side. Одна точка
                // на обе ветки BuildCanvas (спрайт И кремовый меш-фолбэк): у меша был тот же дефект.
                var canvasSize = side * (1f + WoodArt.FramePadFraction);
                BuildCanvas(parent, canvasSize, config, BackdropZOffset + BackdropCanvasBehind);

                var backdrop = new GameObject("PreviewBackdrop");
                var backdropTransform = backdrop.transform;
                backdropTransform.SetParent(parent, false);
                backdropTransform.localPosition = new Vector3(0f, 0f, BackdropZOffset);

                var frameRenderer = backdrop.AddComponent<SpriteRenderer>();
                frameRenderer.sprite = frame;
                frameRenderer.drawMode = SpriteDrawMode.Sliced;
                frameRenderer.size = Vector2.one * frameSize;
                frameRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                frameRenderer.receiveShadows = false;

                // Тень под картиной — просьба владельца: без неё рамка лежит на стене плоско.
                FrameShadow.Attach(frameRenderer);
                return;
            }

            // Спрайта рамки нет (bringup) — рисуем прежний одиночный цветной квад (никакой регрессии).
            BuildCanvasMesh(parent, "PreviewBackdrop", side, config, BackdropZOffset);
        }

        /// <summary>
        /// Build the preview canvas behind the wooden frame (TASK-267). If <see cref="WoodArt.Canvas"/> is
        /// present, it is a light-wood TEXTURE sprite stretched to <paramref name="side"/> world units at local
        /// <paramref name="zOffset"/> — same draw-mode idiom as the frame sprite
        /// (<see cref="SpriteDrawMode.Sliced"/> with a zero border ⇒ pure stretch, resolution-independent). The
        /// caller sizes <paramref name="side"/> to sit INSIDE the frame's opaque ring (mid-ring,
        /// <c>S·(1+FramePadFraction)</c>), NOT at its outer edge: the frame has rounded alpha corners and a
        /// canvas at the full frame extent peeked out from under them (TASK-267 rework, owner verdict on the
        /// live frame). Owner verdict (2026-08-30): a solid tone read flat and foreign on
        /// the wooden scene; the wood texture (variant C2 «limed oak darker») separates from the walnut wall.
        /// If the sprite is absent (<c>WoodArt.Canvas == null</c>) it degrades to the former OPAQUE parchment
        /// mesh (<see cref="BuildCanvasMesh"/>, colour from <see cref="TargetPreviewConfig.BackdropColorHex"/>) —
        /// no regression. Drawn opaque; translucency stays TASK-111 tech-debt.
        /// </summary>
        private static void BuildCanvas(Transform parent, float side, TargetPreviewConfig config, float zOffset)
        {
            var sprite = WoodArt.Canvas;
            if (sprite == null)
            {
                // Спрайта фактуры нет — прежний однотонный кремовый холст (никакой регрессии).
                BuildCanvasMesh(parent, "PreviewCanvas", side, config, zOffset);
                return;
            }

            var canvas = new GameObject("PreviewCanvas");
            var canvasTransform = canvas.transform;
            canvasTransform.SetParent(parent, false);
            canvasTransform.localPosition = new Vector3(0f, 0f, zOffset);

            var renderer = canvas.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            // Тянем спрайт на весь квад тем же идиомом, что рамку: Sliced + size. Кайма спрайта нулевая
            // (spriteBorder=0), поэтому «центр» 9-slice = весь спрайт → чистое растяжение до side без тайлинга.
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = Vector2.one * side;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// One OPAQUE coloured square-canvas mesh (<paramref name="side"/> wide) as a child of the preview
        /// root, centred on the figure. Colour from <see cref="TargetPreviewConfig.BackdropColorHex"/>
        /// (invalid ⇒ white), alpha forced to 1 (the opaque material does not blend alpha). Same mesh
        /// triangulation as the pieces. Placed at local <paramref name="zOffset"/> along +z.
        /// </summary>
        private static void BuildCanvasMesh(Transform parent, string name, float side,
            TargetPreviewConfig config, float zOffset)
        {
            var half = side * 0.5f;
            var outline = new[]
            {
                new Vec2(-half, -half),
                new Vec2(half, -half),
                new Vec2(half, half),
                new Vec2(-half, half),
            };

            var canvas = new GameObject(name);
            var canvasTransform = canvas.transform;
            canvasTransform.SetParent(parent, false);
            canvasTransform.localPosition = new Vector3(0f, 0f, zOffset);

            var meshFilter = canvas.AddComponent<MeshFilter>();
            meshFilter.mesh = PieceMeshBuilder.Build(outline);

            var renderer = canvas.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BlockMaterial.Shared;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var color = Color.white;
            if (!string.IsNullOrEmpty(config.BackdropColorHex)
                && ColorUtility.TryParseHtmlString(config.BackdropColorHex, out var parsed))
                color = parsed;
            color.a = 1f; // opaque material — alpha is not blended (translucency is tracked as tech-debt).

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BlockMaterial.ColorProperty, color);
            mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
            renderer.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// TASK-563: the widest half-width (viewport fraction) the card may take around <paramref name="anchorX"/>
        /// without entering a top-corner button's quadrant (<see cref="HudCornerZone.TopButtons"/>, the given safe
        /// insets), less a small gap. ONE layout value with the buttons themselves, so the reserve cannot drift. The
        /// insets come in as an argument (TASK-576) — the batch capture passes zero, so its card ignores the window.
        /// </summary>
        private static float TopCornerHalfRoom(float aspect, float anchorX, CameraFramingMath.SafeInsets safe)
        {
            const float gap = 0.01f;
            var room = Mathf.Min(anchorX, 1f - anchorX);
            foreach (var zone in HudCornerZone.TopButtons)
            {
                zone.ToViewport(aspect, safe.Side(zone), safe.Top, out var side, out _);
                room = Mathf.Min(room, zone.Right ? 1f - side - anchorX : anchorX - side);
            }
            return Mathf.Max(room - gap, 0f);
        }

        /// <summary>
        /// Centre AND size of the figure's bounding box, preferring the union outline; falls back to slots
        /// (TASK-033: the size feeds the band auto-fit). Size is zero when the target has no geometry.
        /// </summary>
        private static void ComputeExtent(TargetFigure target, out Vector2 center, out Vector2 size)
        {
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;
            var any = false;

            var outline = target.Outline?.Outline;
            if (outline != null && outline.Length > 0)
            {
                foreach (var v in outline)
                {
                    minX = Mathf.Min(minX, v.X);
                    minY = Mathf.Min(minY, v.Y);
                    maxX = Mathf.Max(maxX, v.X);
                    maxY = Mathf.Max(maxY, v.Y);
                }
                any = true;
            }
            else if (target.Slots != null)
            {
                foreach (var slot in target.Slots)
                {
                    minX = Mathf.Min(minX, slot.Position.X);
                    minY = Mathf.Min(minY, slot.Position.Y);
                    maxX = Mathf.Max(maxX, slot.Position.X);
                    maxY = Mathf.Max(maxY, slot.Position.Y);
                }
                any = target.Slots.Count > 0;
            }

            if (any)
            {
                center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
                size = new Vector2(maxX - minX, maxY - minY);
            }
            else
            {
                center = Vector2.zero;
                size = Vector2.zero;
            }
        }
    }
}
