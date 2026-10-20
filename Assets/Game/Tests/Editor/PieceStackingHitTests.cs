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
            private readonly ShapeFillMechanicController _controller;

            public ControllerRig()
            {
                _controller = new ShapeFillMechanicController(Model, new ScriptedPoseSimulation(), new PoseMatchEvaluator(),
                    new LevelDataValidator(new TapZoneConfig()), new NullPieceViewFactory(), new ShapeFillTuning(),
                    new NullTapTolerance());
                Model.LoadLevel(OverlapLevel(new[] { 1, 0 }));
                Assert.That(Model.CurrentPhase, Is.EqualTo(GamePhase.Running), "the fixture level must pass the validator");
            }

            public void Tap(Vec2 p) => _controller.OnIntent(new InputIntent(p.X, p.Y));

            public void Dispose() => _controller.Dispose();
        }
    }
}
