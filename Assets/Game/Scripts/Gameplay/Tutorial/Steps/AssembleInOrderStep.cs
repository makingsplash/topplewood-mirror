using System.Collections.Generic;
using Gameplay.Core;
using R3;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// The step-by-step tutorial MECHANISM (TASK-078): one composite step that walks a level's pieces in a given
    /// order and, for each, waits until the field is at rest (<see cref="GameModel.FieldAtRest"/>, TASK-524 — input
    /// blocked, no highlight meanwhile), then highlights it, admits ONLY a tap on it, and waits for its REAL release
    /// before moving on — repeating until every listed piece is assembled. It is level-agnostic: it knows nothing about the
    /// concrete figure, it just reads the loaded <see cref="GameModel"/> and follows the tap order it is handed. The
    /// order is the INPUT the installer hands it (sailboat's FTUE, see <c>GameplayCoreInstaller</c>); the mechanism is
    /// not tied to any one level. An empty order completes at once.
    ///
    /// Completion contract with <see cref="TutorialRunner"/>: the returned observable emits exactly ONCE — after
    /// the LAST piece is released — so the runner's Take(1) advances the tour only when assembly is done, not on
    /// the first release. It is driven imperatively via <see cref="Observable.Create{T}"/>: the active
    /// (release-subscription + input-gate) pair lives in a <see cref="SerialDisposable"/> that is RETURNED from
    /// Create, so disposing the subscription (normal Take(1) completion OR a mid-tour StopSequence on LOSS-reload,
    /// which disposes WITHOUT calling Exit) tears down the current gate handle. Steps only observe the domain
    /// (<see cref="GameModel.PieceReleased"/>) — they never mutate it, so the headless-simulatable invariant holds.
    /// </summary>
    public sealed class AssembleInOrderStep : ITutorialStep
    {
        private readonly IReadOnlyList<int> _tapOrder;

        /// <param name="tapOrder">Placement indices to tap, in order. Non-Idle / missing entries are skipped.</param>
        public AssembleInOrderStep(IReadOnlyList<int> tapOrder) => _tapOrder = tapOrder;

        // Single funnel key for the whole tour (per-piece granularity is a follow-up, TASK-078 tech-debt).
        public string Id => "assemble";

        public Observable<Unit> Enter(TutorialContext context)
        {
            return Observable.Create<Unit>(observer =>
            {
                var targets = ResolveTargets(context.Model, _tapOrder);
                // The active phase: either (rest-wait + input block) or (release-sub + allow-gate) for the current piece.
                var current = new SerialDisposable();
                var index = 0;

                // TASK-524: the next piece is shown and admitted only once the field is at rest — the previous piece
                // has landed and nothing moves. Arming on the release itself let the finger jump to the sail while the
                // hull was still falling, and a player following the finger without a pause lost level 1. While
                // waiting, input is BLOCKED (not merely un-whitelisted: a disposed AllowOnly opens the gate to every
                // tap) and the highlight is cleared, so the finger never points at a piece that cannot be tapped yet.
                void AwaitRest()
                {
                    if (index >= targets.Count)
                    {
                        observer.OnNext(Unit.Default); // assembly complete → runner advances the tour
                        observer.OnCompleted();
                        return;
                    }

                    if (context.Model.FieldAtRest.CurrentValue)
                    {
                        Arm();
                        return;
                    }

                    context.View.Clear();
                    // Block BEFORE swapping, so the previous allow-gate's dispose never leaves the gate open.
                    var block = context.InputGate.Block();
                    // CurrentValue is false here, so the replayed value is filtered out — Arm never runs inside
                    // Subscribe and cannot be overwritten by the assignment below.
                    var rest = context.Model.FieldAtRest
                        .Where(atRest => atRest)
                        .Take(1)
                        .Subscribe(_ => Arm());
                    current.Disposable = Disposable.Combine(rest, block);
                }

                void Arm()
                {
                    var target = targets[index];
                    // Hand the view the piece's pose AND shape so it can trace a contour matching the silhouette
                    // (TASK-083). Read-only domain access — TypeFor is the same seam HitTest below uses. The FTUE tour
                    // keeps the pointing finger (TASK-263 §2): the tutorial teaches the tap, so it still points.
                    context.View.Highlight(target.Position, target.Rotation, context.Model.TypeFor(target).Shape,
                        HighlightStyle.FingerPoint);

                    // Admit only a tap that resolves to THIS piece; build the new gate handle BEFORE swapping the
                    // SerialDisposable so the old handle's dispose (ClearAllow) is a no-op (last-wins) — no gap.
                    var gate = context.InputGate.AllowOnly(HitTest(context, target));
                    var release = context.Model.PieceReleased
                        .Where(id => id == target.InstanceId)
                        .Take(1)
                        .Subscribe(_ =>
                        {
                            index++;
                            AwaitRest();
                        });

                    current.Disposable = Disposable.Combine(release, gate);
                }

                AwaitRest();
                return current;
            });
        }

        public void Exit(TutorialContext context) => context.View.Clear();

        /// <summary>Resolve placement indices to the loaded level's currently-Idle instances, in tap order.
        /// Pre-placed / already-released / unknown indices are skipped so the step never waits on a piece that
        /// cannot be tapped.</summary>
        private static List<PieceInstance> ResolveTargets(GameModel model, IReadOnlyList<int> tapOrder)
        {
            var targets = new List<PieceInstance>(tapOrder?.Count ?? 0);
            if (tapOrder == null)
                return targets;
            foreach (var placementIndex in tapOrder)
            {
                foreach (var piece in model.Pieces)
                {
                    if (piece.PlacementIndex != placementIndex || piece.State != PieceLifecycle.Idle)
                        continue;
                    targets.Add(piece);
                    break;
                }
            }
            return targets;
        }

        /// <summary>Predicate "the tap resolves to the highlighted piece" — built from the SAME shared
        /// <see cref="PieceHitResolver"/> the domain uses (TASK-085), so the tutorial gate and the domain can never
        /// disagree on which piece a tap hits. This closes the former TASK-078 tech-debt (the gate used to hit-test
        /// independently, containment-only): a near-miss on a small tapable piece now passes the gate exactly when it
        /// would drop that piece in the domain, and the shared two-tier resolver honours the SAME screen-space tap
        /// halo (<see cref="TutorialContext.TapTolerance"/>) — which is what makes the giraffe horns tappable IN the
        /// tutorial, not only outside it. Idle pieces do not move, so the candidate poses snapshotted at Arm stay
        /// valid until release; the tour only shrinks the Idle set (each Arm follows a release), so the snapshot is
        /// the current Idle set for the whole time this gate is active.</summary>
        private static System.Func<InputIntent, bool> HitTest(TutorialContext context, PieceInstance target)
        {
            var candidates = BuildCandidates(context.Model);
            return intent => PieceHitResolver.Resolve(
                candidates, new Vec2(intent.X, intent.Y), context.TapTolerance.WorldRadius) == target.InstanceId;
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
