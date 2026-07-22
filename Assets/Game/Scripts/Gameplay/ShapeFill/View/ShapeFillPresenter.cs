using System;
using Gameplay.Core;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.View
{
    /// <summary>
    /// Unity presenter that renders the level's static silhouette overlay when a level loads. Observes
    /// <see cref="GameModel"/> only (domain stays view-agnostic); block views are handled by the factory.
    /// </summary>
    public sealed class ShapeFillPresenter : IStartable, IDisposable
    {
        private readonly GameModel _model;
        private IDisposable _subscription;
        private GameObject _silhouette;

        public ShapeFillPresenter(GameModel model)
        {
            _model = model;
        }

        public void Start()
        {
            _subscription = _model.LevelLoaded.Subscribe(_ => OnLevelLoaded());

            // A level may already be loaded before this entry point starts: GameControllerRunner is
            // registered first and its Start() calls LoadLevel(), which fires LevelLoaded synchronously.
            // Render the current level now so the silhouette isn't missed (OnLevelLoaded self-guards on null).
            OnLevelLoaded();
        }

        private void OnLevelLoaded()
        {
            if (_silhouette != null)
                UnityEngine.Object.Destroy(_silhouette);

            var level = _model.CurrentLevel;
            if (level?.Silhouette?.Outline == null)
                return;

            _silhouette = new GameObject("Silhouette");
            _silhouette.AddComponent<SilhouetteView>().Initialize(level.Silhouette);
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
