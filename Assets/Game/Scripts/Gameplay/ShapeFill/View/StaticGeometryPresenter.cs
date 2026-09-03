using System;
using System.Collections.Generic;
using Gameplay.Core;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.View
{
    /// <summary>
    /// Renders the level's VISIBLE static geometry (TASK-031c). Static blocks are invisible by default (they only
    /// build colliders), so this spawns a <see cref="StaticGeometryView"/> only for each entry whose parallel
    /// <see cref="LevelData.StaticVisuals"/> descriptor sets <c>Visible</c>. Mirrors <see cref="ShapeFillPresenter"/>:
    /// observes <see cref="GameModel"/> only (domain stays view-agnostic), renders the already-loaded level in
    /// <see cref="Start"/>, and tears the meshes down on every reload. Self-guards on null / length mismatch, so
    /// the 70 pre-TASK-031 levels (no visuals opted in) render nothing.
    /// </summary>
    public sealed class StaticGeometryPresenter : IStartable, IDisposable
    {
        private readonly GameModel _model;
        private readonly List<GameObject> _views = new();
        private IDisposable _subscription;

        public StaticGeometryPresenter(GameModel model)
        {
            _model = model;
        }

        public void Start()
        {
            _subscription = _model.LevelLoaded.Subscribe(_ => OnLevelLoaded());

            // A level may already be loaded before this entry point starts (GameControllerRunner loads level 0 in
            // its own Start, firing LevelLoaded synchronously), so render the current level now — OnLevelLoaded
            // self-guards on null.
            OnLevelLoaded();
        }

        private void OnLevelLoaded()
        {
            Clear();

            var level = _model.CurrentLevel;
            var statics = level?.StaticGeometry;
            var visuals = level?.StaticVisuals;
            if (statics == null || visuals == null)
                return;

            var count = Mathf.Min(statics.Length, visuals.Count);
            for (var i = 0; i < count; i++)
            {
                if (!visuals[i].Visible)
                    continue;

                // TASK-325: a SURFACE (open edge-chain chute) is a valid 2-point polyline; a filled block needs ≥3.
                var poly = statics[i];
                var outline = poly?.Outline;
                var minVerts = poly != null && poly.IsSurface ? 2 : 3;
                if (outline == null || outline.Length < minVerts)
                    continue;

                var go = new GameObject($"StaticGeometry_{i}");
                go.AddComponent<StaticGeometryView>().Initialize(poly, visuals[i].Color);
                _views.Add(go);
            }
        }

        private void Clear()
        {
            foreach (var go in _views)
                if (go != null)
                    UnityEngine.Object.Destroy(go);
            _views.Clear();
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
            Clear();
        }
    }
}
