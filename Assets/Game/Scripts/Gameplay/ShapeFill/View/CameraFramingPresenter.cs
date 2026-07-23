using System;
using System.Collections.Generic;
using Gameplay.Core;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.View
{
    /// <summary>
    /// Fits the orthographic <c>Camera.main</c> to the level content on load (TASK-009, RFC 07 §1). The
    /// bringup scene's camera is fixed, so tall levels (L5 snowman stacks to y≈8) fall outside the viewport
    /// and their top pieces become untappable → the level is unwinnable. This view-side seam frames the union
    /// AABB of the pieces + target slots + static geometry so every piece is on screen and tappable.
    ///
    /// Registered BEFORE <see cref="TargetPreviewPresenter"/> (RFC 07 §1.1): the preview anchors to a
    /// <c>Camera.main</c> viewport point, so the camera must be framed first. Both re-fire on LevelLoaded, so a
    /// reload (WIN→next / retry) reframes for the new level. Mirrors <see cref="TargetPreviewPresenter"/>:
    /// observes <see cref="GameModel"/> only — the domain stays view-agnostic and headless-safe.
    /// </summary>
    public sealed class CameraFramingPresenter : IStartable, IDisposable
    {
        private readonly GameModel _model;
        private readonly CameraFramingConfig _config;
        private IDisposable _subscription;

        public CameraFramingPresenter(GameModel model, CameraFramingConfig config)
        {
            _model = model;
            _config = config;
        }

        public void Start()
        {
            _subscription = _model.LevelLoaded.Subscribe(_ => Frame());

            // A level may already be loaded before this entry point starts (GameControllerRunner runs first and
            // fires LevelLoaded synchronously). Frame the current level now — Frame self-guards on null.
            Frame();
        }

        private void Frame()
        {
            var level = _model.CurrentLevel;
            if (level == null)
                return;

            var camera = UnityEngine.Camera.main;
            if (camera == null || !camera.orthographic)
                return;

            if (!TryComputeContentBounds(level, out var min, out var max))
                return;

            var centerX = (min.X + max.X) * 0.5f;
            var centerY = (min.Y + max.Y) * 0.5f;
            var halfH = (max.Y - min.Y) * 0.5f;
            var halfW = (max.X - min.X) * 0.5f;
            var aspect = camera.aspect <= 0f ? 1f : camera.aspect;

            // Ortho size = half the viewport height; fit both axes (width via aspect), pad, then clamp.
            camera.orthographicSize = Mathf.Max(_config.MinOrthoSize, Mathf.Max(halfH, halfW / aspect) + _config.Margin);

            var t = camera.transform;
            t.position = new Vector3(centerX, centerY, t.position.z);
        }

        /// <summary>Union AABB (world space) of every piece's start silhouette, the target slots and the static
        /// geometry — the full content the player must see and reach.</summary>
        private static bool TryComputeContentBounds(LevelData level, out Vec2 min, out Vec2 max)
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

            if (!any)
                return false;

            min = new Vec2(minX, minY);
            max = new Vec2(maxX, maxY);
            return true;
        }

        private static void Accumulate(Vec2 v, ref float minX, ref float minY, ref float maxX, ref float maxY)
        {
            if (v.X < minX) minX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.X > maxX) maxX = v.X;
            if (v.Y > maxY) maxY = v.Y;
        }

        // Slots carry only a Kind; recover each Kind's shape from the pieces the level ships (mirrors
        // TargetPreviewPresenter.BuildKindMap).
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

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
