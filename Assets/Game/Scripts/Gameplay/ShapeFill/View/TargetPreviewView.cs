using System.Collections.Generic;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Renders the target as a small "picture-on-the-box" preview overlay (RFC §4.2, GAME_RULES §7): the same
    /// pieces the player drops, drawn in their target poses in real shape+colour, centred, uniformly scaled
    /// (<see cref="TargetPreviewConfig.Scale"/>) and anchored to a camera viewport point above the field.
    /// Pure static reference — no collisions, no physics tick, not in the save snapshot. The domain never
    /// references it, so headless simulation is unaffected.
    /// A white square backdrop (TASK-110, <see cref="TargetPreviewConfig.BackdropEnabled"/>) is drawn behind
    /// the silhouette so it reads apart from the screen background; it lives inside this preview root, so it
    /// shares the band anchor and auto-fit scale and adds nothing to physics/save/poseHash.
    /// </summary>
    public sealed class TargetPreviewView : MonoBehaviour
    {
        private static readonly Color DefaultColor = new Color(0.29f, 0.56f, 0.89f); // blue, mirrors PieceView

        // Local +z nudge of the backdrop so it renders behind the slot meshes (opaque depth-order, same trick
        // StaticGeometryView uses). Kept a private const — the ticket asked for colour/alpha/padding config, not z.
        private const float BackdropZOffset = 0.1f;

        public void Initialize(
            TargetFigure target,
            IReadOnlyDictionary<string, PieceType> kinds,
            TargetPreviewConfig config,
            UnityEngine.Camera camera,
            float previewBandFraction)
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
                // Anchor to the CENTRE of the reserved top band (TASK-033): the framing presenter keeps the
                // top PreviewBandFraction of the viewport empty for this card. band=0.20 ⇒ viewportY=0.90
                // (identical to the legacy default, so no positional regression).
                var band = Mathf.Clamp01(previewBandFraction);
                var anchorViewportY = 1f - band * 0.5f;
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

                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = BlockMaterial.Shared;
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
                _slots.Add(new Slot { Renderer = renderer, LevelColor = color, Kind = slot.Kind });
            }

            Repaint();

            // Палитру крутят ползунком в Play — без подписки превью осталось бы в старых цветах,
            // пока поле уже перекрасилось. Сначала снимаем: Initialize зовётся заново на каждый
            // уровень, и без этого подписки копились бы.
            WoodTuning.Changed -= Repaint;
            WoodTuning.Changed += Repaint;
        }

        private void OnDestroy() => WoodTuning.Changed -= Repaint;

        private struct Slot
        {
            public MeshRenderer Renderer;
            public Color LevelColor;
            public string Kind;
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
                mpb.SetColor(BlockMaterial.ColorProperty, color);
                mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
                slot.Renderer.SetPropertyBlock(mpb);
            }
        }

        /// <summary>
        /// TASK-110: build the white square backdrop as a child of the preview root, centred on the figure
        /// (already at local (0,0)), one axis long: <paramref name="side"/> = max-axis + 2·padding. Rendered
        /// OPAQUE via the shared block material (colour from <see cref="TargetPreviewConfig.BackdropColorHex"/>,
        /// alpha forced to 1 — the opaque path does not blend alpha). Same mesh triangulation as the pieces.
        /// </summary>
        private static void BuildBackdrop(Transform parent, float side, TargetPreviewConfig config)
        {
            var half = side * 0.5f;
            var outline = new[]
            {
                new Vec2(-half, -half),
                new Vec2(half, -half),
                new Vec2(half, half),
                new Vec2(-half, half),
            };

            var backdrop = new GameObject("PreviewBackdrop");
            var backdropTransform = backdrop.transform;
            backdropTransform.SetParent(parent, false);
            backdropTransform.localPosition = new Vector3(0f, 0f, BackdropZOffset);

            // Арт владельца: деревянная рамка со белым полем внутри — она и есть подложка.
            // 9-slice, потому что кант обязан остаться одной толщины на любой стороне фигуры.
            // Спрайта нет — остаётся прежний белый квад из меша (никакой регрессии).
            var frame = WoodArt.Frame;
            if (frame != null)
            {
                var frameRenderer = backdrop.AddComponent<SpriteRenderer>();
                frameRenderer.sprite = frame;
                frameRenderer.drawMode = SpriteDrawMode.Sliced;
                // Кант рисуется ПОВЕРХ стороны, поэтому рамку берём шире белого поля: иначе
                // деревянный борт налез бы на детали превью.
                frameRenderer.size = Vector2.one * side * (1f + 2f * WoodArt.FramePadFraction);
                frameRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                frameRenderer.receiveShadows = false;

                // Тень под картиной — просьба владельца: без неё рамка лежит на стене плоско.
                FrameShadow.Attach(frameRenderer);
                return;
            }

            var meshFilter = backdrop.AddComponent<MeshFilter>();
            meshFilter.mesh = PieceMeshBuilder.Build(outline);

            var renderer = backdrop.AddComponent<MeshRenderer>();
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
