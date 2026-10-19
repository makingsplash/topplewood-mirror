using System.Collections.Generic;
using Gameplay.Core.Adapters;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// TASK-629: how far each PREPLACED piece moves on its own before the player's first tap — the authored start
    /// pose against the pose physics brings it to rest in. Preplaced furniture is released into physics at t=0
    /// (<c>ShapeFillMechanicController.SpawnPreplaced</c>) and the patient player's first tap waits for it to rest
    /// (<c>ReadyForNextRelease</c>), so whatever it does in between — a drop, a tilt onto a stop, a slide — is
    /// motion the player watches at level start. <c>LevelDataValidator</c>'s <c>Preplaced rest:</c> (TASK-628)
    /// catches only the straight-down gap at t=0; a piece seated flush but off balance (lever_lid's lever, centre of
    /// mass off the pivot) tilts anyway, and only the physics can say by how much. This probe runs the level with
    /// NO taps until every preplaced piece leaves Falling (the tick it turns Placed is its rest pose — the same
    /// capture tick SimRun's pre-glide metric uses) and measures the largest shift of any shape vertex, so a
    /// rotation counts at the piece's extremity, not at its centre.
    /// Sim-only and additive: the host runs it in its own isolated world after the gated report is written.
    /// </summary>
    public static class PreplacedSettleProbe
    {
        /// <summary>How far the solver lifts a polygon resting FLUSH on a polygon: their collision skins
        /// (<see cref="ContactRestAllowance.PolygonSkin"/> each) meet, then the corrector lets them sink back by
        /// <see cref="ContactRestAllowance.LinearSlop"/> — core gap 2·0.01 − 0.005 = 0.015. Measured, not assumed: every
        /// flush preplaced piece of the TASK-629 sweep (plank, box, beam, leg) rested exactly +0.015 above its start.
        /// A circle has no skin, so its lift is smaller; this is the polygon–polygon upper bound for ONE contact.</summary>
        public const float SolverRestLift = 2f * ContactRestAllowance.PolygonSkin - ContactRestAllowance.LinearSlop;

        /// <summary>Largest vertex shift (world units) still read as "starts at rest": the t=0 gap rule's allowance
        /// (<see cref="LevelDataValidator.PreplacedRestTolerance"/>) plus the solver's own rest lift. The lift has to be
        /// in: a piece authored flush (what the gap rule demands) always rises by it, and authoring it 0.015 higher to
        /// pre-pay the lift would trip the gap rule instead — the two checks cannot both sit at 0.01. Limitation: the
        /// lift accumulates up a stack (two polygon layers ≈ 0.03), so a preplaced polygon on a preplaced polygon on a
        /// block would read over this while resting honestly.</summary>
        public const float Tolerance = LevelDataValidator.PreplacedRestTolerance + SolverRestLift;

        /// <summary>One preplaced piece: authored start, the pose it settles in, and the step it settled at.
        /// <see cref="Outcome"/> is the piece state when the probe stopped watching it — <c>Placed</c> (rested),
        /// <c>Lost</c> (left the level) or <c>Timeout</c> (still Falling at the step cap).</summary>
        public readonly struct Entry
        {
            public readonly int PieceIndex;
            public readonly string Kind;
            public readonly Vec2 Start;
            public readonly float StartRotation;
            public readonly Vec2 Rest;
            public readonly float RestRotation;
            public readonly int Step;
            public readonly string Outcome;
            public readonly float MaxShift;

            public Entry(int pieceIndex, string kind, Vec2 start, float startRotation, Vec2 rest, float restRotation,
                int step, string outcome, float maxShift)
            {
                PieceIndex = pieceIndex;
                Kind = kind;
                Start = start;
                StartRotation = startRotation;
                Rest = rest;
                RestRotation = restRotation;
                Step = step;
                Outcome = outcome;
                MaxShift = maxShift;
            }

            public bool OverTolerance => Outcome != PieceLifecycle.Placed.ToString() || MaxShift > Tolerance;
        }

        /// <summary>Never taps: the probe watches the field the player sees before touching it.</summary>
        private sealed class NoTapPolicy : ISimBotPolicy
        {
            public bool TryNextRelease(GameModel model, List<Vec2> taps) => false;
            public bool GatedByRest => false;
        }

        /// <summary>Runs <paramref name="level"/> with no taps for up to <paramref name="maxSteps"/> fixed steps and
        /// returns one entry per preplaced piece (empty when the level has none). Resets the simulation afterwards,
        /// like <see cref="SimRun.Run"/>; the caller owns its lifetime.</summary>
        public static List<Entry> Run(LevelData level, IShapeFillSimulation simulation, int maxSteps)
        {
            var entries = new List<Entry>();
            if (level.Pieces == null || !HasPreplaced(level))
                return entries;

            using (var run = new SimRun(level, simulation, new NoTapPolicy()))
            {
                var settled = new HashSet<int>();
                var pending = CountPreplaced(level);
                var step = 0;
                for (; step < maxSteps && pending > 0 && !run.Resolved; step++)
                {
                    run.Step();
                    foreach (var piece in run.Pieces)
                    {
                        if (!level.Pieces[piece.PlacementIndex].Preplaced || piece.State == PieceLifecycle.Falling)
                            continue;
                        if (!settled.Add(piece.PlacementIndex))
                            continue;
                        entries.Add(EntryOf(level, piece, step + 1, piece.State.ToString()));
                        pending--;
                    }
                }

                // Whatever is still Falling when the probe stops (step cap, or the level resolved on its own) is
                // reported where it is, flagged Timeout — never silently dropped.
                foreach (var piece in run.Pieces)
                    if (level.Pieces[piece.PlacementIndex].Preplaced && !settled.Contains(piece.PlacementIndex))
                        entries.Add(EntryOf(level, piece, step, "Timeout"));
            }

            simulation.Reset();
            entries.Sort((a, b) => a.PieceIndex.CompareTo(b.PieceIndex));
            return entries;
        }

        private static Entry EntryOf(LevelData level, PieceInstance piece, int step, string outcome)
        {
            var placement = level.Pieces[piece.PlacementIndex];
            return new Entry(piece.PlacementIndex, piece.Kind, placement.StartPosition, placement.StartRotation,
                piece.Position, piece.Rotation, step, outcome,
                MaxVertexShift(placement.Type.Shape, placement.StartPosition, placement.StartRotation,
                    piece.Position, piece.Rotation));
        }

        /// <summary>Largest distance any vertex of <paramref name="shape"/> travels between two poses — translation
        /// and rotation in one world-unit number (a 4-unit plank tilting 0.38 rad moves its tip ~0.75).</summary>
        public static float MaxVertexShift(Shape shape, Vec2 fromPos, float fromRot, Vec2 toPos, float toRot)
        {
            var from = Geometry.WorldVertices(shape, fromPos, fromRot);
            var to = Geometry.WorldVertices(shape, toPos, toRot);
            var max = (toPos - fromPos).Length;
            for (var i = 0; i < from.Length; i++)
            {
                var d = (to[i] - from[i]).Length;
                if (d > max)
                    max = d;
            }
            return max;
        }

        private static bool HasPreplaced(LevelData level) => CountPreplaced(level) > 0;

        private static int CountPreplaced(LevelData level)
        {
            var count = 0;
            foreach (var p in level.Pieces)
                if (p.Preplaced)
                    count++;
            return count;
        }
    }
}
