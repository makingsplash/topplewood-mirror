using System.Collections.Generic;
using Gameplay.Core;
using R3;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// One tap beat of the tour (TASK-613 plan §3.7): wait until the field is at rest, point at ONE piece, admit only a
    /// tap on it, complete on its REAL release. A tour is a chain of these built from the level's
    /// <c>WinningOrder</c> (<see cref="AssembleTourRecipe"/>), closed by <see cref="AwaitWinStep"/>. This is the
    /// per-piece body of the former composite <c>AssembleInOrderStep</c>, behaviour unchanged:
    /// <list type="bullet">
    /// <item>TASK-524 rest gate: until <see cref="GameModel.FieldAtRest"/>, input is BLOCKED (not merely un-whitelisted:
    /// a disposed AllowOnly opens the gate to every tap) and nothing is shown, so the finger never points at a piece
    /// that cannot be tapped yet and a player following it cannot drop the sail onto a falling hull.</item>
    /// <item>The active (rest-wait + block) or (release + allow-gate) pair lives in a <see cref="SerialDisposable"/>
    /// RETURNED from <see cref="Observable.Create{T}"/>, so disposing the subscription — normal Take(1) completion OR
    /// a mid-tour StopSequence on a LOSS reload, which disposes WITHOUT Exit — tears the gate handle down.</item>
    /// <item>The piece is no longer Idle (pre-placed, already released, unknown index) → the step completes at once:
    /// it never waits on a piece that cannot be tapped.</item>
    /// </list>
    /// New in TASK-613: the cue carries the piece's kind (the view lights the card slot) and a delay (0 on the first
    /// beat, <see cref="TourConfig.BetweenTapsDelay"/> after) that the VIEW holds — input is open from rest, so a tap on
    /// the right piece during that pause is legal, and the step stays timer-free. A whitelist miss
    /// (<see cref="ITutorialInputGate.Rejected"/>) pulses the view while this beat's gate is active.
    /// TASK-635 <c>silent</c>: the beat gates exactly the same but shows NOTHING on arm — someone else points at the piece
    /// (the Hint booster's own outline, <see cref="BoosterTourRecipe"/>). Only the first miss brings the tour's usual beat
    /// in at once (cut-out + finger, delay 0), then the pulse: the same "not there — here" the player learnt on level 1.
    /// </summary>
    public sealed class TapPieceStep : ITutorialStep
    {
        private readonly int _placementIndex;
        private readonly int _ordinal;
        private readonly bool _silent;

        /// <param name="placementIndex">Index into the level's pieces to tap.</param>
        /// <param name="ordinal">1-based position in the tour — names the analytics step and decides the delay.</param>
        /// <param name="silent">No visual on arm; the tour's beat appears on the first miss (TASK-635).</param>
        public TapPieceStep(int placementIndex, int ordinal, bool silent = false)
        {
            _placementIndex = placementIndex;
            _ordinal = ordinal;
            _silent = silent;
        }

        public int PlacementIndex => _placementIndex;

        public bool Silent => _silent;

        public string Id => $"tap_{_ordinal}";

        public Observable<Unit> Enter(TutorialContext context)
        {
            return Observable.Create<Unit>(observer =>
            {
                var target = ResolveTarget(context.Model, _placementIndex);
                if (target == null)
                {
                    observer.OnNext(Unit.Default);
                    observer.OnCompleted();
                    return Disposable.Empty;
                }

                var current = new SerialDisposable();

                void Arm()
                {
                    var delay = _ordinal <= 1 || _silent ? 0f : context.Config.BetweenTapsDelay;
                    var candidates = BuildCandidates(context.Model);
                    var cue = new TapCue(target.Position, target.Rotation,
                        context.Model.TypeFor(target).Shape, target.Kind, delay, OthersOf(candidates, target));
                    var shown = !_silent;
                    if (shown)
                        context.View.ShowTap(cue);

                    // Build the new gate handle BEFORE swapping the SerialDisposable so the block's dispose never
                    // leaves the gate open in between (last-wins on AllowOnly).
                    var gate = context.InputGate.AllowOnly(HitTest(context, target, candidates));
                    var miss = context.InputGate.Rejected.Subscribe(_ =>
                    {
                        if (!shown)
                        {
                            shown = true;
                            context.View.ShowTap(cue);
                        }
                        context.View.PulseMiss();
                    });
                    var release = context.Model.PieceReleased
                        .Where(id => id == target.InstanceId)
                        .Take(1)
                        .Subscribe(_ =>
                        {
                            observer.OnNext(Unit.Default);
                            observer.OnCompleted();
                        });

                    current.Disposable = Disposable.Combine(release, miss, gate);
                }

                if (context.Model.FieldAtRest.CurrentValue)
                {
                    Arm();
                    return current;
                }

                var block = context.InputGate.Block();
                // CurrentValue is false here, so the replayed value is filtered out — Arm never runs inside Subscribe
                // and cannot be overwritten by the assignment below.
                var rest = context.Model.FieldAtRest
                    .Where(atRest => atRest)
                    .Take(1)
                    .Subscribe(_ => Arm());
                current.Disposable = Disposable.Combine(rest, block);
                return current;
            });
        }

        public void Exit(TutorialContext context) => context.View.HideTap();

        /// <summary>The loaded level's still-Idle instance at <paramref name="placementIndex"/>, or null.</summary>
        private static PieceInstance ResolveTarget(GameModel model, int placementIndex)
        {
            foreach (var piece in model.Pieces)
                if (piece.PlacementIndex == placementIndex && piece.State == PieceLifecycle.Idle)
                    return piece;
            return null;
        }

        /// <summary>Predicate "the tap resolves to the highlighted piece" — built from the SAME shared
        /// <see cref="PieceHitResolver"/> the domain uses (TASK-085), so the tutorial gate and the domain can never
        /// disagree on which piece a tap hits, including the screen-space tap halo
        /// (<see cref="TutorialContext.TapTolerance"/>) that makes small pieces tappable IN the tour. Idle pieces do not
        /// move, so the candidate poses snapshotted at Arm stay valid until this beat's release.</summary>
        private static System.Func<InputIntent, bool> HitTest(TutorialContext context, PieceInstance target,
            List<PieceHitResolver.HitCandidate> candidates)
        {
            return intent => PieceHitResolver.Resolve(
                candidates, new Vec2(intent.X, intent.Y), context.TapTolerance.WorldRadius) == target.InstanceId;
        }

        /// <summary>World outlines of every Idle candidate except the target (the cut-out's gap rule, spec 02 §3).</summary>
        private static List<Vec2[]> OthersOf(List<PieceHitResolver.HitCandidate> candidates, PieceInstance target)
        {
            var others = new List<Vec2[]>(candidates.Count);
            foreach (var c in candidates)
                if (c.InstanceId != target.InstanceId)
                    others.Add(c.World);
            return others;
        }

        /// <summary>Snapshot every currently-Idle piece as a hit candidate (its world polygon + centre), so the
        /// shared resolver picks over the SAME set the domain would.</summary>
        private static List<PieceHitResolver.HitCandidate> BuildCandidates(GameModel model)
        {
            var candidates = new List<PieceHitResolver.HitCandidate>();
            foreach (var piece in model.Pieces)
            {
                if (piece.State != PieceLifecycle.Idle)
                    continue;
                var shape = model.TypeFor(piece).Shape;
                var world = Geometry.WorldVertices(shape, piece.Position, piece.Rotation);
                candidates.Add(new PieceHitResolver.HitCandidate(piece.InstanceId, world, piece.Position));
            }
            return candidates;
        }
    }
}
