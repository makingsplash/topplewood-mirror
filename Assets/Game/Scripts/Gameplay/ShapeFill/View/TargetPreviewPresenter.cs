using System;
using System.Collections.Generic;
using Gameplay.Core;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.View
{
    /// <summary>
    /// Builds the target preview overlay when a level loads (RFC §4.1, GAME_RULES §7). Mirrors
    /// <see cref="ShapeFillPresenter"/>: observes <see cref="GameModel"/> only, so the domain stays
    /// view-agnostic. The preview depends solely on the level's target + piece types, not on piece
    /// runtime state, so it is rebuilt only on level load (TASK-044 retired the in-level save snapshot).
    /// </summary>
    public sealed class TargetPreviewPresenter : IStartable, IDisposable
    {
        private readonly GameModel _model;
        private readonly TargetPreviewConfig _config;
        private readonly CameraFramingConfig _framingConfig;
        private IDisposable _subscription;
        private GameObject _preview;

        // Reads CameraFramingConfig so the preview knows the reserved top band (TASK-033) — the framing config
        // is the single source of truth for the band. Both configs are already RegisterInstance'd, so
        // VContainer resolves this extra ctor parameter without a new registration.
        public TargetPreviewPresenter(GameModel model, TargetPreviewConfig config, CameraFramingConfig framingConfig)
        {
            _model = model;
            _config = config;
            _framingConfig = framingConfig;
        }

        public void Start()
        {
            _subscription = _model.LevelLoaded.Subscribe(_ => Rebuild());

            // A level may already be loaded before this entry point starts (GameControllerRunner runs first
            // and fires LevelLoaded synchronously). Build the current level now — Rebuild self-guards on null.
            Rebuild();
        }

        private void Rebuild()
        {
            Clear();

            var level = _model.CurrentLevel;
            if (level?.Target?.Slots == null)
                return;

            _preview = new GameObject("TargetPreview");
            _preview.AddComponent<TargetPreviewView>()
                .Initialize(level.Target, BuildKindMap(level), _config, UnityEngine.Camera.main,
                    _framingConfig.PreviewBandFraction);
        }

        // The preview shows the target only, so map from the pieces the player is given (Decoys are not
        // part of the target and are intentionally excluded).
        private static IReadOnlyDictionary<string, PieceType> BuildKindMap(LevelData level)
        {
            var map = new Dictionary<string, PieceType>();
            if (level.Pieces == null)
                return map;

            foreach (var placement in level.Pieces)
            {
                if (placement.Type != null && !map.ContainsKey(placement.Type.Kind))
                    map[placement.Type.Kind] = placement.Type;
            }
            return map;
        }

        private void Clear()
        {
            if (_preview != null)
                UnityEngine.Object.Destroy(_preview);
            _preview = null;
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
            Clear();
        }
    }
}
