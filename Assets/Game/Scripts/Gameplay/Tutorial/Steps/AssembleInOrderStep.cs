using System.Collections.Generic;
using Gameplay.Core;
using R3;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// The step-by-step tutorial MECHANISM (TASK-078): one composite step that walks a level's pieces in a given
    /// order and, for each, highlights it, admits ONLY a tap on it, and waits for its REAL release before moving
    /// on — repeating until every listed piece is assembled. It is level-agnostic: it knows nothing about
    /// giraffe, it just reads the loaded <see cref="GameModel"/> and follows the tap order it is handed. The order
    /// is the temporary hardcoded INPUT (giraffe placement-order, wired in the installer); the mechanism is not.
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
                var current = new SerialDisposable(); // active (release-sub + gate handle) for the piece being tapped
                var index = 0;

                void Arm()
                {
                    if (index >= targets.Count)
                    {
                        observer.OnNext(Unit.Default); // assembly complete → runner advances the tour
                        observer.OnCompleted();
                        return;
                    }

                    var target = targets[index];
                    // Hand the view the piece's pose AND shape so it can trace a contour matching the silhouette
                    // (TASK-083). Read-only domain access — TypeFor is the same seam HitTest below uses.
                    context.View.Highlight(target.Position, target.Rotation, context.Model.TypeFor(target).Shape);

                    // Admit only a tap inside THIS piece's polygon; build the new gate handle BEFORE swapping the
                    // SerialDisposable so the old handle's dispose (ClearAllow) is a no-op (last-wins) — no gap.
                    var gate = context.InputGate.AllowOnly(HitTest(context.Model, target));
                    var release = context.Model.PieceReleased
                        .Where(id => id == target.InstanceId)
                        .Take(1)
                        .Subscribe(_ =>
                        {
                            index++;
                            Arm();
                        });

                    current.Disposable = Disposable.Combine(release, gate);
                }

                Arm();
                return current;
            });
        }

        public void Exit(TutorialContext context) => context.View.Clear();

        /// <summary>Resolve placement indices to the loaded level's currently-Idle instances, in tap order.
        /// Pre-placed / already-released / unknown indices are skipped so the step never waits on a piece that
        /// cannot be tapped.</summary>
        private static List<PieceInstance> ResolveTargets(GameModel model, IReadOnlyList<int> tapOrder)
        {
            var targets = new List<PieceInstance>(tapOrder.Count);
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

        /// <summary>Predicate "tap lands inside the highlighted piece" — built from the SAME public geometry the
        /// domain hit-tests with (<see cref="Geometry.PointInPolygon"/> over the piece's world outline at its
        /// current pose). Idle pieces do not move, so the pose captured here stays valid until release.
        /// KNOWN LIMIT (TASK-078 tech-debt): hit-tests independently of the domain's PickIdlePieceAt — overlapping
        /// Idle pieces could disagree; giraffe's slots do not overlap, so it does not fire here.</summary>
        private static System.Func<InputIntent, bool> HitTest(GameModel model, PieceInstance target)
        {
            var shape = model.TypeFor(target).Shape;
            var world = Geometry.WorldVertices(shape, target.Position, target.Rotation);
            return intent => Geometry.PointInPolygon(world, new Vec2(intent.X, intent.Y));
        }
    }
}
