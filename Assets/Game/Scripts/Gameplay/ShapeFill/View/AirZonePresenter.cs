using System;
using System.Collections.Generic;
using Gameplay.Core;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.View
{
    /// <summary>
    /// Renders the level's directed air-flow zones (TASK-097). A level opts in by authoring <c>airZones</c>; for
    /// every zone this spawns an <see cref="AirZoneView"/> whose visible mesh coincides with the force region
    /// (§D5 — the player must see where the flow acts, never bump an invisible force). Mirrors
    /// <see cref="StaticGeometryPresenter"/>: observes <see cref="GameModel"/> only (the domain stays
    /// view-agnostic), renders the already-loaded level in <see cref="Start"/>, tears the views down on every
    /// reload, and self-guards on null so the 73 pre-TASK-097 levels (no zones) render nothing. The headless
    /// SimRun never builds this entry point, so the sim sees flow only through the physics adapter.
    /// </summary>
    public sealed class AirZonePresenter : IStartable, IDisposable
    {
        private readonly GameModel _model;
        private readonly List<GameObject> _views = new();
        private IDisposable _subscription;

        public AirZonePresenter(GameModel model)
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

            var zones = _model.CurrentLevel?.AirZones;
            if (zones == null)
                return;

            for (var i = 0; i < zones.Length; i++)
            {
                var go = new GameObject($"AirZone_{i}");
                go.AddComponent<AirZoneView>().Initialize(zones[i]);
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
