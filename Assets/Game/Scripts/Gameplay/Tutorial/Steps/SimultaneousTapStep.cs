using System;
using System.Collections.Generic;
using Gameplay.Core;
using R3;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Teaches the CO-RELEASE gesture (TASK-377): highlight EVERY member of a declared co-release group at once —
    /// each with its own finger — and wait for the group's REAL simultaneous release before advancing. It reads the
    /// group members from the loaded level's <see cref="LevelData.CoReleaseGroups"/> (a data-driven list of placement
    /// indices), NOT from a hardcoded pair, so the same step teaches whatever group the level declares — the "class,
    /// not case" contract (owner 2026-09-14): grand_seesaw's pan pair [2,3] is just the first case; a weight/cap pair
    /// or a 3-piece group is the same code. <paramref name="groupOrdinal"/> selects WHICH group of the level to teach
    /// (default: the first).
    ///
    /// Under the co-release rule the domain releases these pieces ONLY when all members are tapped inside
    /// <c>ShapeFillTuning.CoReleaseWindowSteps</c> (a single tap does nothing), so this step cannot be satisfied by a
    /// one-finger tap — it teaches exactly the required two-finger gesture. The input gate admits a tap on ANY member
    /// (both fingers pass); the domain decides whether the two taps land in the same group in time.
    ///
    /// Completion contract with <see cref="TutorialRunner"/>: the returned observable emits exactly ONCE — after the
    /// LAST member is released. Steps only OBSERVE the domain (<see cref="GameModel.PieceReleased"/>) — they never
    /// mutate it, so the headless-simulatable invariant holds (same as <see cref="AssembleInOrderStep"/>).
    /// </summary>
    public sealed class SimultaneousTapStep : ITutorialStep
    {
        private readonly int _groupOrdinal;

        /// <param name="groupOrdinal">Which co-release group of the loaded level to teach (index into
        /// <see cref="LevelData.CoReleaseGroups"/>). Defaults to the first group.</param>
        public SimultaneousTapStep(int groupOrdinal = 0) => _groupOrdinal = groupOrdinal;

        public string Id => "simultaneous_tap";

        public Observable<Unit> Enter(TutorialContext context)
        {
            return Observable.Create<Unit>(observer =>
            {
                var targets = ResolveTargets(context.Model, _groupOrdinal);
                if (targets.Count == 0)
                {
                    // No tappable group members (no group declared, ordinal out of range, or all already released) →
                    // complete immediately so a mis-wired tour never stalls the runner instead of silently hanging.
                    observer.OnNext(Unit.Default);
                    observer.OnCompleted();
                    return Disposable.Empty;
                }

                // Highlight EVERY member with its own overlay key so N fingers show at once (multi-highlight, TASK-377).
                // The keyed Highlight overload keeps a distinct overlay per member; Clear() (in Exit) hides them all.
                var targetIds = new HashSet<int>();
                for (var i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    targetIds.Add(target.InstanceId);
                    context.View.Highlight(i, target.Position, target.Rotation,
                        context.Model.TypeFor(target).Shape, HighlightStyle.FingerPoint);
                }

                // Admit a tap on ANY member (both fingers pass); the domain's co-release window then requires the
                // whole group within CoReleaseWindowSteps. Build the gate over the SAME shared resolver the domain
                // uses so the tutorial gate and the domain can never disagree on which piece a tap hits (TASK-085).
                var remaining = new HashSet<int>(targetIds);
                var gate = context.InputGate.AllowOnly(HitTest(context, targetIds));
                var release = context.Model.PieceReleased
                    .Where(id => remaining.Contains(id))
                    .Subscribe(id =>
                    {
                        remaining.Remove(id);
                        if (remaining.Count != 0)
                            return;
                        observer.OnNext(Unit.Default); // whole group released → runner advances the tour
                        observer.OnCompleted();
                    });

                return Disposable.Combine(release, gate);
            });
        }

        public void Exit(TutorialContext context) => context.View.Clear();

        /// <summary>Resolve the group's placement indices to the loaded level's currently-Idle instances. Reads the
        /// group from <see cref="LevelData.CoReleaseGroups"/>; pre-placed / already-released / unknown indices are
        /// skipped so the step never waits on a piece that cannot be tapped. Returns empty when the level has no such
        /// group (null groups or ordinal out of range).</summary>
        private static List<PieceInstance> ResolveTargets(GameModel model, int groupOrdinal)
        {
            var groups = model.CurrentLevel?.CoReleaseGroups;
            if (groups == null || groupOrdinal < 0 || groupOrdinal >= groups.Count)
                return new List<PieceInstance>();

            var group = groups[groupOrdinal];
            var targets = new List<PieceInstance>(group.Count);
            foreach (var placementIndex in group)
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

        /// <summary>Predicate "the tap resolves to one of the highlighted members" — built from the SAME shared
        /// <see cref="PieceHitResolver"/> the domain uses (TASK-085), over the SAME screen-space tap halo, so the gate
        /// admits a near-miss on a small member exactly when the domain would hit it. Idle pieces do not move, so the
        /// candidate poses snapshotted here stay valid until release.</summary>
        private static Func<InputIntent, bool> HitTest(TutorialContext context, HashSet<int> targetIds)
        {
            var candidates = BuildCandidates(context.Model);
            return intent => targetIds.Contains(PieceHitResolver.Resolve(
                candidates, new Vec2(intent.X, intent.Y), context.TapTolerance.WorldRadius));
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
