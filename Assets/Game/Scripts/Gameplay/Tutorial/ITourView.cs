using System.Collections.Generic;
using Gameplay.Core;
using R3;

namespace Gameplay.Tutorial
{
    /// <summary>One tap beat of the tour: the piece to point at and how long the view waits before showing it.</summary>
    public readonly struct TapCue
    {
        /// <summary>The piece's pose and geometry, so the view can trace a contour matching its silhouette.</summary>
        public readonly Vec2 Origin;
        public readonly float Rotation;
        public readonly Shape Shape;

        /// <summary>The piece's kind — the view lights up the target card's slot(s) of the same kind.</summary>
        public readonly string Kind;

        /// <summary>Seconds the view waits before showing the beat (0 on the first, <see cref="TourConfig.BetweenTapsDelay"/>
        /// after). Held by the view so the step stays timer-free and deterministic under test.</summary>
        public readonly float Delay;

        public TapCue(Vec2 origin, float rotation, Shape shape, string kind, float delay)
        {
            Origin = origin;
            Rotation = rotation;
            Shape = shape;
            Kind = kind;
            Delay = delay;
        }
    }

    /// <summary>The "Совпало" beat: the level's target (union outline + slots) and the shape of every kind in it.</summary>
    public readonly struct MatchCue
    {
        public readonly TargetFigure Target;
        public readonly IReadOnlyDictionary<string, Shape> ShapesByKind;

        public MatchCue(TargetFigure target, IReadOnlyDictionary<string, Shape> shapesByKind)
        {
            Target = target;
            ShapesByKind = shapesByKind;
        }
    }

    /// <summary>
    /// The tutorial tour's view seam (TASK-613 plan §3.4) — the TOUR's alone. The Hint booster has its own
    /// <c>Gameplay.View.IPieceHighlightView</c>, so each view has exactly one writer: no keys, no shared
    /// <c>Clear()</c> that tears down the other consumer (TASK-383/244/550 closed by construction). Domain data flows
    /// INTO the view (<see cref="Vec2"/>, <see cref="Shape"/>, <see cref="TargetFigure"/>), which is the permitted
    /// direction; the view converts on its side. Steps are tested in EditMode against a fake of this interface.
    /// </summary>
    public interface ITourView
    {
        /// <summary>Show one tap beat: dim with cut-outs (piece, card), the piece contour, the card slot(s) of its kind
        /// and the finger's tap cycle — after <see cref="TapCue.Delay"/>.</summary>
        void ShowTap(TapCue cue);

        /// <summary>A tap went elsewhere: an out-of-turn cycle from the "press" phase plus a flash of the cut-out.
        /// Idempotent — a second call just restarts the cycle.</summary>
        void PulseMiss();

        /// <summary>The player tapped the piece: finger/contours out fast, the dim slower so the fall stays visible.</summary>
        void HideTap();

        /// <summary>The "Совпало" beat. COLD: nothing plays until subscribed; emits once and completes when the beat is
        /// over (≈<see cref="TourConfig.MatchSeconds"/>). Disposing it early stops the beat.</summary>
        Observable<Unit> PlayMatch(MatchCue cue);

        /// <summary>Hide everything the tour shows, immediately.</summary>
        void Clear();
    }
}
