using System;
using Gameplay.Tutorial;
using R3;
using UnityEngine;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// The Hint button as an <see cref="IHudTourTarget"/> (TASK-635 plan §3.2) — a passive adapter: the
    /// <see cref="HintBoosterPresenter"/> feeds it (<see cref="Attach"/>, <see cref="SetReady"/>, <see cref="SetCanActivate"/>,
    /// <see cref="NotifyActivated"/>) and reads <see cref="FocusHeld"/> into its <see cref="HudCover"/> binding. A Singleton
    /// in the prod scope, so the registry can be built before any presenter starts.
    /// </summary>
    public sealed class HintButtonTourTarget : IHudTourTarget
    {
        private readonly ReactiveProperty<bool> _ready = new(false);
        private readonly ReactiveProperty<bool> _canActivate = new(false);
        private readonly ReactiveProperty<bool> _focusHeld = new(false);
        private readonly Subject<Unit> _activated = new();
        private readonly Vector3[] _world = new Vector3[4];
        private RectTransform _button;
        private int _holds;

        public string Id => BoosterTourRecipe.HintButtonTargetId;

        public ReadOnlyReactiveProperty<bool> Ready => _ready;

        public ReadOnlyReactiveProperty<bool> CanActivate => _canActivate;

        public Observable<Unit> Activated => _activated;

        /// <summary>True while any tour beat holds the button's focus exemption.</summary>
        public ReadOnlyReactiveProperty<bool> FocusHeld => _focusHeld;

        public void Attach(RectTransform button) => _button = button;

        public void SetReady(bool ready) => _ready.Value = ready && _button != null;

        public void SetCanActivate(bool canActivate) => _canActivate.Value = canActivate;

        public void NotifyActivated() => _activated.OnNext(Unit.Default);

        /// <summary>The button's world corners — for a Screen Space Overlay canvas they ARE screen pixels.</summary>
        public bool TryGetScreenCorners(Vector2[] corners4)
        {
            if (_button == null || corners4 == null || corners4.Length < 4)
                return false;
            _button.GetWorldCorners(_world);
            for (var i = 0; i < 4; i++)
                corners4[i] = _world[i];
            return true;
        }

        public IDisposable HoldFocus()
        {
            _holds++;
            _focusHeld.Value = true;
            return Disposable.Create(() =>
            {
                _holds = Math.Max(0, _holds - 1);
                _focusHeld.Value = _holds > 0;
            });
        }
    }
}
