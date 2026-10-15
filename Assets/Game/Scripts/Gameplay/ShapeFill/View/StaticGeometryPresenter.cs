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

            // TASK-567: один спаунер на игру, тулу кадров и песочницу — слияние блоков в массу и прежний поблочный путь
            // живут там, а не в трёх копиях цикла.
            foreach (var spawned in StaticGeometrySpawner.Spawn(_model.CurrentLevel, "StaticGeometry"))
                _views.Add(spawned.Go);
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
