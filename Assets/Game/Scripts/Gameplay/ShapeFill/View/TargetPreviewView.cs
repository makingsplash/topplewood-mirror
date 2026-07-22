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
        private static readonly Color DefaultColor = new Color(0.78f, 0.56f, 0.31f); // wood, mirrors PieceView

        public void Initialize(
            TargetFigure target,
            IReadOnlyDictionary<string, PieceType> kinds,
            TargetPreviewConfig config,
            UnityEngine.Camera camera)
        {
            if (target?.Slots == null || kinds == null || config == null)
                return;

            var figureCenter = ComputeCenter(target);

            var t = transform;
            if (camera != null)
            {
                var anchor = camera.ViewportToWorldPoint(
                    new Vector3(config.AnchorViewportX, config.AnchorViewportY, config.CameraDistance));
                anchor.z = config.ZPlane;
                t.position = anchor;
            }
            else
            {
                t.position = new Vector3(0f, 0f, config.ZPlane);
            }
            t.localScale = Vector3.one * config.Scale;

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

        /// <summary>Centre of the figure's bounding box, preferring the union outline; falls back to slots.</summary>
        private static Vector2 ComputeCenter(TargetFigure target)
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

            return any ? new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f) : Vector2.zero;
        }
    }
}
