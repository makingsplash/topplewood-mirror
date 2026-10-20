using System.Collections.Generic;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using Gameplay.Core.Mechanic;
using Gameplay.Core.Sim;
using Gameplay.Tutorial;
using Gameplay.View;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-645: the tap takes the piece the player SEES at that point — among the Idle pieces containing it, the one
    /// drawn on top (lower placement index, <see cref="PieceStacking"/>, the same rank <see cref="PieceDepth"/> draws by).
    /// Pinned on the pure resolver, on the real controller (the domain tap), on the tour gate, and on the sim bot's tap
    /// point. Fixture: two unit squares, index 0 at x=0 (top) and index 1 at x=0.6 (under it) — they overlap on
    /// x∈[0.1, 0.5]. The probe x=0.4 is in the overlap but NEARER the lower piece's centre, so the pre-TASK-645
    /// nearest-centre rule took the lower one: dropping the rank from the comparison turns these tests red.
    /// </summary>
    [TestFixture]
    public sealed class PieceStackingHitTests
    {
        private const int TopId = 10;
        private const int BottomId = 11;
        private static readonly Vec2 InOverlap = new(0.4f, 0f);
        private static readonly Vec2 OnBottomOnly = new(0.9f, 0f);
        private static readonly Vec2 OnTopOnly = new(-0.3f, 0f);

        // ── one source with the picture ──────────────────────────────────────────────────────────────────────────
        [Test]
        public void Rank_OrdersExactlyLikePieceDepth()
        {
            for (var i = 0; i < PieceStacking.Slots + 10; i++)
            for (var j = 0; j < PieceStacking.Slots + 10; j++)
            {
                var rankAbove = PieceStacking.Rank(i) < PieceStacking.Rank(j);
                var drawnAbove = PieceDepth.For(PieceLifecycle.Idle, i) < PieceDepth.For(PieceLifecycle.Idle, j);
                Assert.That(rankAbove, Is.EqualTo(drawnAbove), $"index {i} vs {j}: the tap and the picture disagree");
            }
        }

        // ── the pure resolver ────────────────────────────────────────────────────────────────────────────────────
        [Test]
        public void PartialOverlap_OverlapTakesTheTop_UncoveredPartTakesTheBottom([Values] bool reversedList)
        {
            var c = Pair(reversedList);
            Assert.That(PieceHitResolver.Resolve(c, InOverlap, 0f), Is.EqualTo(TopId), "overlap → the piece drawn on top");
            Assert.That(PieceHitResolver.Resolve(c, OnBottomOnly, 0f), Is.EqualTo(BottomId),
                "the visible part of the lower piece taps the lower piece");
            Assert.That(PieceHitResolver.Resolve(c, OnTopOnly, 0f), Is.EqualTo(TopId));
            Assert.That(PieceHitResolver.Resolve(c, new Vec2(5f, 5f), 0f), Is.EqualTo(-1));
        }

        [Test]
        public void PartialOverlap_BottomAim_IsOnItsVisiblePart_AndTapsIt()
        {
            var c = Pair(false);
            Assert.That(PieceHitResolver.FindAim(c, BottomId, out var aim), Is.EqualTo(PieceHitResolver.Visibility.Partial));
            Assert.That(PieceHitResolver.Resolve(c, aim, 0f), Is.EqualTo(BottomId));
            Assert.That(aim.X, Is.GreaterThan(0.5f), "outside the cover");
            Assert.That(PieceHitResolver.FindAim(c, TopId, out var topAim), Is.EqualTo(PieceHitResolver.Visibility.Full));
            Assert.That(PieceHitResolver.Resolve(c, topAim, 0f), Is.EqualTo(TopId));
        }

        [Test]
        public void FullCover_HiddenPieceCannotBeTapped_UntilTheCoverIsGone()
        {
            var cover = new PieceHitResolver.HitCandidate(TopId, 0, Box(new Vec2(0f, 0f), 2f), new Vec2(0f, 0f));
            var hidden = new PieceHitResolver.HitCandidate(BottomId, 1, Box(new Vec2(0.2f, 0.2f), 0.5f), new Vec2(0.2f, 0.2f));
            var both = new List<PieceHitResolver.HitCandidate> { hidden, cover };

            Assert.That(PieceHitResolver.Resolve(both, new Vec2(0.2f, 0.2f), 0f), Is.EqualTo(TopId),
                "the hidden piece's own centre goes to its cover");
            Assert.That(PieceHitResolver.FindAim(both, BottomId, out _), Is.EqualTo(PieceHitResolver.Visibility.Hidden));

            var coverGone = new List<PieceHitResolver.HitCandidate> { hidden };
            Assert.That(PieceHitResolver.Resolve(coverGone, new Vec2(0.2f, 0.2f), 0f), Is.EqualTo(BottomId));
        }

        [Test]
        public void HaloTie_OnASharedEdge_GoesToTheCover()
        {
            // The hidden square's right edge lies on the cover's (x = 0.5): a halo tap just outside is equidistant.
            var cover = new PieceHitResolver.HitCandidate(TopId, 0, Box(new Vec2(0f, 0f), 1f), new Vec2(0f, 0f));
            var hidden = new PieceHitResolver.HitCandidate(BottomId, 1, Box(new Vec2(0.25f, 0f), 0.5f), new Vec2(0.25f, 0f));
            var both = new List<PieceHitResolver.HitCandidate> { hidden, cover };

            Assert.That(PieceHitResolver.Resolve(both, new Vec2(0.6f, 0f), 0.2f), Is.EqualTo(TopId));
        }

        [Test]
        public void EqualRank_PastTheSlots_FallsBackToNearestCentre()
        {
            var a = new PieceHitResolver.HitCandidate(TopId, PieceStacking.Slots + 5, Box(new Vec2(0f, 0f), 1f), new Vec2(0f, 0f));
            var b = new PieceHitResolver.HitCandidate(BottomId, PieceStacking.Slots + 9, Box(new Vec2(0.6f, 0f), 1f), new Vec2(0.6f, 0f));
            var c = new List<PieceHitResolver.HitCandidate> { a, b };

            Assert.That(PieceHitResolver.Resolve(c, InOverlap, 0f), Is.EqualTo(BottomId),
                "the picture does not order them either — the pre-TASK-645 nearest centre decides");
        }

        // ── the domain tap (real controller) ─────────────────────────────────────────────────────────────────────
        [Test]
        public void Controller_TapInOverlap_ReleasesTheTop_TapOnUncoveredPart_ReleasesTheBottom()
        {
            using (var rig = new ControllerRig())
            {
                rig.Tap(InOverlap);
                Assert.That(rig.Model.Pieces[0].State, Is.Not.EqualTo(PieceLifecycle.Idle), "the top piece fell");
                Assert.That(rig.Model.Pieces[1].State, Is.EqualTo(PieceLifecycle.Idle));
            }

            using (var rig = new ControllerRig())
            {
                rig.Tap(OnBottomOnly);
                Assert.That(rig.Model.Pieces[1].State, Is.Not.EqualTo(PieceLifecycle.Idle),
                    "the owner's case: the lower piece is tapped first, by its visible part");
                Assert.That(rig.Model.Pieces[0].State, Is.EqualTo(PieceLifecycle.Idle));
            }
        }

        // ── the tour gate: the same rule ─────────────────────────────────────────────────────────────────────────
        [Test]
        public void Tour_BeatOnTheLowerPiece_AdmitsItsVisiblePart_RefusesTheOverlap_AimsAtWhatItAdmits()
        {
            using var rig = new TourRig(OverlapLevel(new[] { 1, 0 }));
            rig.Runner.Start();
            rig.Model.SetFieldAtRest(true);

            Assert.That(rig.View.Taps.Count, Is.EqualTo(1));
            var cue = rig.View.Taps[0];
            Assert.That(rig.Gate.Allows(TourRig.Tap(OnBottomOnly)), Is.True);
            Assert.That(rig.Gate.Allows(TourRig.Tap(InOverlap)), Is.False, "the overlap is the top piece's");
            Assert.That(cue.Aim.HasValue, Is.True, "a partly covered piece carries an aim");
            Assert.That(rig.Gate.Allows(TourRig.Tap(cue.Aim.Value)), Is.True, "the finger points where a tap is admitted");
        }

        [Test]
        public void Tour_UncoveredPiece_HasNoAim()
        {
            using var rig = new TourRig(); // two separate boxes
            rig.Runner.Start();
            rig.Model.SetFieldAtRest(true);
            Assert.That(rig.View.Taps[0].Aim.HasValue, Is.False, "nothing covers it — the view keeps its own tip");
        }

        // ── the sim bot ──────────────────────────────────────────────────────────────────────────────────────────
        [Test]
        public void SimTapPoint_PartlyCoveredPiece_TapsItsVisiblePart_UncoveredKeepsTheCentroid()
        {
            var model = new GameModel();
            // Lower piece's centroid (0.6, 0) lies under the top piece only when they overlap past it: widen the cover.
            var level = OverlapLevel(new[] { 0, 1 }, coverSize: 1.4f);
            model.LoadLevel(level);
            var top = model.Pieces[0];
            var bottom = model.Pieces[1];

            var candidates = new List<PieceHitResolver.HitCandidate>();
            foreach (var p in model.Pieces)
                candidates.Add(new PieceHitResolver.HitCandidate(p.InstanceId, p.PlacementIndex,
                    Geometry.WorldVertices(model.TypeFor(p).Shape, p.Position, p.Rotation), p.Position));

            var centroid = SimGeometry.ShapeCentroidWorld(model.TypeFor(bottom), bottom.Position, bottom.Rotation);
            Assert.That(PieceHitResolver.Resolve(candidates, centroid, 0f), Is.EqualTo(top.InstanceId),
                "fixture: the lower piece's centroid is under the cover");
            Assert.That(PieceHitResolver.Resolve(candidates, SimGeometry.TapPoint(model, bottom), 0f),
                Is.EqualTo(bottom.InstanceId), "the bot still addresses the piece it meant");
            Assert.That(SimGeometry.TapPoint(model, top),
                Is.EqualTo(SimGeometry.ShapeCentroidWorld(model.TypeFor(top), top.Position, top.Rotation)),
                "an uncovered piece keeps the centroid — the sweep of every other level is bit-for-bit");
        }

        // ── TASK-649: an active piece on top catches the tap ─────────────────────────────────────────────────────
        private static readonly PieceLifecycle[] AllStates =
            { PieceLifecycle.Idle, PieceLifecycle.Falling, PieceLifecycle.Placed, PieceLifecycle.Lost };

        [Test]
        public void RankAcrossLayers_OrdersExactlyLikePieceDepth()
        {
            foreach (var s in AllStates)
            foreach (var t in AllStates)
                for (var i = 0; i < PieceStacking.Slots + 5; i += 3)
                for (var j = 0; j < PieceStacking.Slots + 5; j += 3)
                {
                    var rankAbove = PieceStacking.Rank(s, i) < PieceStacking.Rank(t, j);
                    var drawnAbove = PieceDepth.For(s, i) < PieceDepth.For(t, j);
                    Assert.That(rankAbove, Is.EqualTo(drawnAbove), $"{s}#{i} vs {t}#{j}: the tap and the picture disagree");
                }
        }

        [Test]
        public void ActiveCover_CatchesTheTap_InItsArea_EvenWithAHalo_AndIsNeverReturned([Values] bool reversedList)
        {
            // The ACTIVE piece has the HIGHER index: only the layer puts it on top (an index-only rank would not).
            var idle = new PieceHitResolver.HitCandidate(TopId, PieceLifecycle.Idle, 0, Box(new Vec2(0f, 0f), 1f),
                new Vec2(0f, 0f));
            var falling = new PieceHitResolver.HitCandidate(BottomId, PieceLifecycle.Falling, 1,
                Box(new Vec2(0.6f, 0f), 1f), new Vec2(0.6f, 0f));
            var c = reversedList
                ? new List<PieceHitResolver.HitCandidate> { falling, idle }
                : new List<PieceHitResolver.HitCandidate> { idle, falling };

            Assert.That(PieceHitResolver.Resolve(c, InOverlap, 0f), Is.EqualTo(-1), "the falling piece is seen there");
            Assert.That(PieceHitResolver.Resolve(c, InOverlap, 0.3f), Is.EqualTo(-1), "a caught tap is not a near miss");
            Assert.That(PieceHitResolver.Resolve(c, OnBottomOnly, 0f), Is.EqualTo(-1), "an active piece is never released");
            Assert.That(PieceHitResolver.Resolve(c, OnTopOnly, 0f), Is.EqualTo(TopId), "the Idle piece's visible part");
            Assert.That(PieceHitResolver.Resolve(c, new Vec2(1.2f, 0f), 0.2f), Is.EqualTo(-1),
                "the halo never reaches an active piece");

            Assert.That(PieceHitResolver.FindAim(c, TopId, out var aim), Is.EqualTo(PieceHitResolver.Visibility.Partial));
            Assert.That(PieceHitResolver.Resolve(c, aim, 0f), Is.EqualTo(TopId));
        }

        [Test]
        public void Controller_FallingCoverOverAnIdlePiece_CatchesTheTap_UntilItHasFallenAway()
        {
            using var rig = new ControllerRig();
            rig.Tap(OnTopOnly);
            Assert.That(rig.Model.Pieces[0].State, Is.EqualTo(PieceLifecycle.Falling), "fixture: the cover falls");
            var attempts = rig.Model.AttemptsUsed;

            rig.Tap(InOverlap);
            Assert.That(rig.Model.Pieces[1].State, Is.EqualTo(PieceLifecycle.Idle),
                "the matryoshka case: the tap lands on the falling cover, not on the piece hanging under it");
            Assert.That(rig.Model.AttemptsUsed, Is.EqualTo(attempts), "a caught tap releases nothing");

            rig.Sim.SetPose(ControllerRig.BodyOf(0), new Vec2(0f, -4f), 0f); // the cover has fallen away
            rig.Tick(0.01f);
            Assert.That(rig.Model.Pieces[0].State, Is.EqualTo(PieceLifecycle.Falling));
            rig.Tap(InOverlap);
            Assert.That(rig.Model.Pieces[1].State, Is.EqualTo(PieceLifecycle.Falling), "nothing above it any more");
        }

        [Test]
        public void Controller_LyingPieceOverAnIdlePiece_CatchesTheTap()
        {
            using var rig = new ControllerRig();
            rig.Tap(OnTopOnly);
            rig.Tick(1f); // rests where it was released (the pose canvas does not move it)
            Assert.That(rig.Model.Pieces[0].State, Is.EqualTo(PieceLifecycle.Placed), "fixture: the cover lies there");
            Assert.That(rig.Model.CurrentPhase, Is.EqualTo(GamePhase.Running), "fixture: play goes on");

            rig.Tap(InOverlap);
            Assert.That(rig.Model.Pieces[1].State, Is.EqualTo(PieceLifecycle.Idle));
            rig.Tap(OnBottomOnly);
            Assert.That(rig.Model.Pieces[1].State, Is.EqualTo(PieceLifecycle.Falling), "its visible part still taps it");
        }

        [Test]
        public void Tour_LyingPieceOverTheTarget_GateRefusesTheCoveredPart_AimsAtTheVisibleOne()
        {
            var level = OverlapLevel(new[] { 0, 1 });
            using var rig = new TourRig(level);
            rig.Model.Pieces[0].State = PieceLifecycle.Placed; // lies where it hung: covers the target's left part
            rig.Runner.Start();
            rig.Model.SetFieldAtRest(true);

            var cue = rig.View.Taps[rig.View.Taps.Count - 1];
            Assert.That(rig.Gate.Allows(TourRig.Tap(InOverlap)), Is.False, "that part is under the lying piece");
            Assert.That(rig.Gate.Allows(TourRig.Tap(OnBottomOnly)), Is.True);
            Assert.That(cue.Aim.HasValue, Is.True);
            Assert.That(rig.Gate.Allows(TourRig.Tap(cue.Aim.Value)), Is.True);
        }

        [Test]
        public void SimTapPoint_PieceUnderALyingOne_TapsItsVisiblePart_FullyCoveredTapIsCaught()
        {
            var model = new GameModel();
            model.LoadLevel(OverlapLevel(new[] { 0, 1 }, coverSize: 1.4f));
            var bottom = model.Pieces[1];
            model.Pieces[0].State = PieceLifecycle.Placed;
            var candidates = PieceHitResolver.CandidatesOf(model);
            Assert.That(PieceHitResolver.Resolve(candidates, SimGeometry.TapPoint(model, bottom), 0f),
                Is.EqualTo(bottom.InstanceId), "the bot taps the part the player sees");

            var covered = new GameModel();
            covered.LoadLevel(OverlapLevel(new[] { 0, 1 }, coverSize: 3f));
            covered.Pieces[0].State = PieceLifecycle.Falling;
            Assert.That(PieceHitResolver.Resolve(PieceHitResolver.CandidatesOf(covered),
                    SimGeometry.TapPoint(covered, covered.Pieces[1]), 0f), Is.EqualTo(-1),
                "covered entirely by an active piece: the tap is caught, as a player's would be");
        }

        // ── fixtures ─────────────────────────────────────────────────────────────────────────────────────────────
        private static List<PieceHitResolver.HitCandidate> Pair(bool reversed)
        {
            var top = new PieceHitResolver.HitCandidate(TopId, 0, Box(new Vec2(0f, 0f), 1f), new Vec2(0f, 0f));
            var bottom = new PieceHitResolver.HitCandidate(BottomId, 1, Box(new Vec2(0.6f, 0f), 1f), new Vec2(0.6f, 0f));
            return reversed
                ? new List<PieceHitResolver.HitCandidate> { bottom, top }
                : new List<PieceHitResolver.HitCandidate> { top, bottom };
        }

        private static Vec2[] Box(Vec2 c, float size)
        {
            var h = size * 0.5f;
            return new[]
            {
                new Vec2(c.X - h, c.Y - h), new Vec2(c.X + h, c.Y - h), new Vec2(c.X + h, c.Y + h), new Vec2(c.X - h, c.Y + h),
            };
        }

        /// <summary>The two-square overlap at the origin; target = two squares side by side further down.</summary>
        private static LevelData OverlapLevel(IReadOnlyList<int> winningOrder, float coverSize = 1f)
        {
            var material = new MaterialProfile(1f, 0.6f, 0.05f);
            var cover = new PieceType("cover", Shape.Box(coverSize, coverSize), material);
            var sq = new PieceType("sq", Shape.Box(1f, 1f), material);
            var outline = new[] { new Vec2(-0.5f, -6.5f), new Vec2(1.5f, -6.5f), new Vec2(1.5f, -5.5f), new Vec2(-0.5f, -5.5f) };
            return new LevelData
            {
                LevelId = "overlap_pair",
                Pieces = new List<PiecePlacement> { new(cover, new Vec2(0f, 0f)), new(sq, new Vec2(0.6f, 0f)) },
                WinningOrder = winningOrder,
                Target = new TargetFigure(new Polygon(outline), new List<TargetSlot>
                {
                    new("cover", new Vec2(0f, -6f), 0f),
                    new("sq", new Vec2(1f, -6f), 0f),
                }),
                MatchTolerance = new MatchTolerance(0.3f, 0.1f),
                Bounds = new Rect(new Vec2(-20f, -20f), new Vec2(20f, 20f)),
            };
        }

        /// <summary>The real controller on the scripted pose canvas (as <c>WinFigureTests</c>), on the overlap level.</summary>
        private sealed class ControllerRig : System.IDisposable
        {
            public readonly GameModel Model = new();
            public readonly ScriptedPoseSimulation Sim = new();
            private readonly ShapeFillMechanicController _controller;

            public ControllerRig()
            {
                _controller = new ShapeFillMechanicController(Model, Sim, new PoseMatchEvaluator(),
                    new LevelDataValidator(new TapZoneConfig()), new NullPieceViewFactory(), new ShapeFillTuning(),
                    new NullTapTolerance());
                Model.LoadLevel(OverlapLevel(new[] { 1, 0 }));
                Assert.That(Model.CurrentPhase, Is.EqualTo(GamePhase.Running), "the fixture level must pass the validator");
            }

            public void Tap(Vec2 p) => _controller.OnIntent(new InputIntent(p.X, p.Y));

            public void Tick(float dt) => _controller.Tick(dt);

            /// <summary>The pose canvas body of a piece: spawned in level order, ids from 1.</summary>
            public static int BodyOf(int placementIndex) => placementIndex + 1;

            public void Dispose() => _controller.Dispose();
        }
    }
}
