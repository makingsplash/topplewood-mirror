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
    /// </summary>
    public sealed class TargetPreviewView : MonoBehaviour
    {
        private static readonly Color DefaultColor = new Color(0.29f, 0.56f, 0.89f); // blue, mirrors PieceView

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
                var fit = Mathf.Min(
                    bandWorldW * pad / Mathf.Max(figureSize.x, 1e-3f),
                    bandWorldH * pad / Mathf.Max(figureSize.y, 1e-3f));
                var scale = Mathf.Clamp(Mathf.Min(config.Scale, fit), config.MinScale, config.Scale);
                t.localScale = Vector3.one * scale;
            }
            else
            {
                // Bringup fallback (no Camera.main): keep legacy behaviour so the preview never crashes.
                t.position = new Vector3(0f, 0f, config.ZPlane);
                t.localScale = Vector3.one * config.Scale;
            }

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

                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(BlockMaterial.ColorProperty, color);
                mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
                renderer.SetPropertyBlock(mpb);
            }
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
