using System;
using System.Collections.Generic;
using Gameplay.Core;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.View
{
    /// <summary>
    /// Unity presenter that renders the target as a ghost overlay when a level loads (RFC §6, GAME_RULES §Q5):
    /// the union outline plus one paler per-slot ghost so the player sees where each piece belongs. Observes
    /// <see cref="GameModel"/> only (domain stays view-agnostic); piece views are handled by the factory.
    /// </summary>
    public sealed class ShapeFillPresenter : IStartable, IDisposable
    {
        private static readonly Color GhostColor = new Color(0.6f, 0.85f, 1f, 0.35f);

        private readonly GameModel _model;
        private readonly List<GameObject> _ghosts = new();
        private IDisposable _subscription;
        private IDisposable _outcomeSubscription;

        public ShapeFillPresenter(GameModel model)
        {
            _model = model;
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

                var world = Geometry.WorldVertices(shape, slot.Position, slot.Rotation);
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
