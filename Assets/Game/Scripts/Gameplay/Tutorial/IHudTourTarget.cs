using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// A HUD element a tour can point at and wait on (TASK-635 plan §3.2) — the class "tap this HUD element", not the
    /// Hint button by name: the Hint button is the first target, a future booster button or the settings gear is one
    /// more adapter plus one catalog line. The element's presenter owns the adapter and feeds it; the tour only reads.
    /// </summary>
    public interface IHudTourTarget
    {
        /// <summary>Stable id the recipe names (e.g. <c>"hint_button"</c>).</summary>
        string Id { get; }

        /// <summary>Built, visible (for the Hint button: unlocked) and laid out — the step waits for true.</summary>
        ReadOnlyReactiveProperty<bool> Ready { get; }

        /// <summary>The element's OWN interactivity, ignoring the tour's focus (charges, eligibility, busy).</summary>
        ReadOnlyReactiveProperty<bool> CanActivate { get; }

        /// <summary>The element's current screen quad in pixels (4 corners, any winding); false if it has none now.</summary>
        bool TryGetScreenCorners(Vector2[] corners4);

        /// <summary>An ACCEPTED activation (for the Hint button: a charge spent and a piece revealed) — not a raw click.</summary>
        Observable<Unit> Activated { get; }

        /// <summary>While the handle is held, the element is exempt from the tour's HUD dim and stays tappable.</summary>
        IDisposable HoldFocus();
    }

    /// <summary>The HUD targets the hosting scope offers to tours, by id.</summary>
    public interface IHudTourTargets
    {
        bool TryGet(string id, out IHudTourTarget target);
    }

    /// <summary>
    /// Fixed registry of <see cref="IHudTourTarget"/>s, bound by the HOSTING scope (prod: the Hint button; bringup:
    /// <see cref="Empty"/>) — like <c>ICampaignStore</c>. A fixed list, not one filled from presenters' Start: entry-point
    /// order would then decide whether the runner sees a target, while the target's own readiness is already reactive.
    /// </summary>
    public sealed class HudTourTargets : IHudTourTargets
    {
        private readonly Dictionary<string, IHudTourTarget> _byId = new();

        public HudTourTargets(params IHudTourTarget[] targets)
        {
            foreach (var t in targets)
                if (t != null)
                    _byId[t.Id] = t;
        }

        public static HudTourTargets Empty => new();

        public bool TryGet(string id, out IHudTourTarget target)
        {
            target = null;
            return id != null && _byId.TryGetValue(id, out target);
        }
    }
}
