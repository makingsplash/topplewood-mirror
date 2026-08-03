using System;
using System.Collections.Generic;
using Gameplay.Core;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.View
{
    /// <summary>
    /// Unity presenter for the shape_fill scene: surfaces gray-box outcome (Won/Lost) and, as an optional
    /// assist, the full-size on-field ghost overlay — union outline plus paler per-slot ghosts showing where
    /// each piece belongs. The ghost is now gated behind <see cref="TargetPreviewConfig.ShowOnFieldGhost"/>
    /// (default OFF): the mini preview (<see cref="TargetPreviewPresenter"/>) is the primary target reference
    /// (GAME_RULES §7, RFC §4.6). Observes <see cref="GameModel"/> only (domain stays view-agnostic); piece
    /// views are handled by the factory.
    /// </summary>
    public sealed class ShapeFillPresenter : IStartable, IDisposable
    {
        private static readonly Color GhostColor = new Color(0.6f, 0.85f, 1f, 0.35f);

        private readonly GameModel _model;
        private readonly TargetPreviewConfig _config;
        private readonly List<GameObject> _ghosts = new();
        private IDisposable _subscription;
        private IDisposable _outcomeSubscription;

        public ShapeFillPresenter(GameModel model, TargetPreviewConfig config)
        {
            _model = model;
            _config = config;
        }

        public void Start()
        {
            _subscription = _model.LevelLoaded.Subscribe(_ => OnLevelLoaded());

            // Gray-box outcome feedback (no UI yet): the domain resolves Won/Lost silently, so surface each
            // transition once via the console. Outcome is a ReactiveProperty (None → Won|Lost), so it only emits
            // on change and re-loading a level resets it to None — filtering None gives exactly one log per result.
            _outcomeSubscription = _model.Outcome
                .Where(outcome => outcome != GameOutcome.None)
                .Subscribe(outcome => Debug.Log(
                    outcome == GameOutcome.Won ? "[ShapeFill] LEVEL WON" : "[ShapeFill] LEVEL LOST"));

            // A level may already be loaded before this entry point starts: GameControllerRunner is registered
            // first and its Start() calls LoadLevel(), which fires LevelLoaded synchronously. Render the current
            // level now so the overlay isn't missed (OnLevelLoaded self-guards on null).
            OnLevelLoaded();
        }

        private void OnLevelLoaded()
        {
            ClearGhosts();

            // The full-size on-field ghost is now an optional assist (default OFF): the mini preview
            // (TargetPreviewPresenter) is the primary target reference (GAME_RULES §7, RFC §4.6).
            if (!_config.ShowOnFieldGhost)
                return;

            var level = _model.CurrentLevel;
            if (level?.Target?.Outline?.Outline == null)
                return;

            // Union outline.
            var union = new GameObject("TargetOutline");
            union.AddComponent<SilhouetteView>().Initialize(level.Target.Outline);
            _ghosts.Add(union);

            // Per-slot ghosts: build each slot's shape at its target pose (paler).
            if (level.Target.Slots == null)
                return;

            foreach (var slot in level.Target.Slots)
            {
                var shape = FindShape(level, slot.Kind);
                if (shape == null)
                    continue;

                // Trace the VIEW outline (ShapeVisualOutline), not shape.Vertices, so a slot ghost matches its
                // piece silhouette exactly — a dome piece renders smooth (TASK-074) and its ghost must too.
                // This is view-only: the collider and honesty detector still read shape.Vertices unchanged.
                var visual = ShapeVisualOutline.Build(shape);
                var world = Geometry.WorldVertices(visual, slot.Position, slot.Rotation);
                var ghost = new GameObject($"SlotGhost_{slot.Kind}");
                ghost.AddComponent<SilhouetteView>().Initialize(new Polygon(world), GhostColor);
                _ghosts.Add(ghost);
            }
        }

        private static Shape FindShape(LevelData level, string kind)
        {
            if (level.Pieces == null)
                return null;
            foreach (var placement in level.Pieces)
            {
                if (placement.Type != null && placement.Type.Kind == kind)
                    return placement.Type.Shape;
            }
            return null;
        }

        private void ClearGhosts()
        {
            foreach (var ghost in _ghosts)
            {
                if (ghost != null)
                    UnityEngine.Object.Destroy(ghost);
            }
            _ghosts.Clear();
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
            _outcomeSubscription?.Dispose();
            _outcomeSubscription = null;
            ClearGhosts();
        }
    }
}
