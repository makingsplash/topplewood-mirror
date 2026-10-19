using System.Collections.Generic;
using System.Linq;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using Gameplay.Core.Mechanic;
using Gameplay.Core.Sim;
using NUnit.Framework;
using R3;
using static System.MathF;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-634 plan §7 T1–T3: the win beat flies EXACTLY the judge's layout. (T1) the pairs the evaluator now hands out
    /// leave the verdict bit for bit as it was and describe the assignment it scored; the signed symmetry fold is the
    /// judge's angle error with a sign. (T2) the controller publishes the won figure from those pairs — only the pieces in
    /// slots, each landing on the slot angle NEAREST its rest pose. (T3) the figure is there when a synchronous Won
    /// subscriber reads it, and gone after a reload.
    /// </summary>
    [TestFixture]
    public sealed class WinFigureTests
    {
        private const float Deg = PI / 180f;

        // ── T1: evaluator pairs + signed symmetry fold ───────────────────────────────────────────────────────────
        [Test]
        public void Evaluate_WithPairs_SameVerdict_PairsAreTheAssignment()
        {
            var eval = new PoseMatchEvaluator();
            var tol = new MatchTolerance(0.3f, 5f * Deg, 0.2f);
            var target = TwoSquareTarget();
            var layouts = new[]
            {
                // exact; swapped identical pieces; one piece at 88° (box order 4); one out of tolerance; rigidity breach
                new[] { Sq(0f, 0f, 0f), Sq(1f, 0f, 0f) },
                new[] { Sq(1f, 0f, 0f), Sq(0f, 0f, 0f) },
                new[] { Sq(1.02f, 0.01f, 88f * Deg), Sq(0f, 0f, 0f) },
                new[] { Sq(0f, 0f, 0f), Sq(1.6f, 0f, 0f) },
                new[] { Sq(-0.25f, 0f, 0f), Sq(1.25f, 0f, 0f) },
            };

            foreach (var placed in layouts)
            {
                var pairs = new List<MatchPair>();
                var plain = eval.Evaluate(target, placed, tol);
                var withPairs = eval.Evaluate(target, placed, tol, pairs);

                Assert.That(withPairs.AllSlotsFilled, Is.EqualTo(plain.AllSlotsFilled));
                Assert.That(withPairs.SlotsFilled, Is.EqualTo(plain.SlotsFilled));
                Assert.That(withPairs.TotalError, Is.EqualTo(plain.TotalError));
                Assert.That(pairs.Count, Is.EqualTo(plain.SlotsFilled), "one pair per filled slot");
                Assert.That(pairs.Select(p => p.SlotIndex).Distinct().Count(), Is.EqualTo(pairs.Count));
                Assert.That(pairs.Select(p => p.PlacedIndex).Distinct().Count(), Is.EqualTo(pairs.Count));
                foreach (var p in pairs)
                    Assert.That((target.Slots[p.SlotIndex].Position - placed[p.PlacedIndex].Position).Length,
                        Is.LessThanOrEqualTo(tol.PosTol), "a pair is an admissible (slot, piece) of the same layout");
            }

            var swapped = new List<MatchPair>();
            eval.Evaluate(target, layouts[1], tol, swapped);
            Assert.That(swapped.Single(p => p.SlotIndex == 0).PlacedIndex, Is.EqualTo(1),
                "identical pieces keep the slot they lie in — no swap in the air");
        }

        [Test]
        public void Evaluate_ReusedPairsList_IsClearedFirst()
        {
            var eval = new PoseMatchEvaluator();
            var pairs = new List<MatchPair> { new(7, 7), new(8, 8), new(9, 9) };

            eval.Evaluate(TwoSquareTarget(), new[] { Sq(0f, 0f, 0f) }, new MatchTolerance(0.3f, 0.1f), pairs);

            Assert.That(pairs.Count, Is.EqualTo(1));
        }

        [Test]
        public void SignedSymmetryDelta_FoldsIntoHalfPeriod_AndIsCongruent()
        {
            foreach (var order in new[] { 1, 2, 4 })
            {
                var period = 2f * PI / order;
                for (var delta = -4f * PI; delta <= 4f * PI; delta += 0.137f)
                {
                    var d = PoseMatchEvaluator.SignedSymmetryDelta(delta, order);
                    Assert.That(Abs(d), Is.LessThanOrEqualTo(period * 0.5f + 1e-5f), $"order {order}, delta {delta}");
                    var turns = (delta - d) / period;
                    Assert.That(Abs(turns - Round(turns)), Is.LessThan(1e-3f), "delta − fold is a whole number of periods");
                }
            }
        }

        // ── T2/T3: the controller's won figure ───────────────────────────────────────────────────────────────────
        [Test]
        public void Controller_WinFigure_IsTheJudgedPieces_LandingOnTheNearestSymmetricAngle()
        {
            using var rig = new ControllerRig();
            rig.Drop(0, new Vec2(1.02f, 0.01f), 88f * Deg); // a square settled a quarter turn off slot 1
            rig.Drop(1, new Vec2(0f, 0f), 0f);
            rig.TickToVerdict();

            Assert.That(rig.Model.Outcome.CurrentValue, Is.EqualTo(GameOutcome.Won), "the early assembly verdict");
            var figure = rig.Model.WinFigure;
            Assert.That(figure.Select(f => f.PlacementIndex), Is.EquivalentTo(new[] { 0, 1 }),
                "the still-Idle third square is not part of the picture");

            var turned = figure.Single(f => f.PlacementIndex == 0);
            Assert.That(turned.SlotIndex, Is.EqualTo(1));
            Assert.That(turned.RestRotation, Is.EqualTo(88f * Deg).Within(1e-6f));
            Assert.That(turned.LandRotation, Is.EqualTo(90f * Deg).Within(1e-5f),
                "lands on slot + 90° (≡ slot for a square), NOT on slot 0° — no quarter-turn spin in flight");

            var straight = figure.Single(f => f.PlacementIndex == 1);
            Assert.That(straight.SlotIndex, Is.EqualTo(0));
            Assert.That(straight.LandRotation, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void Controller_WinFigure_SetBeforeWonIsDelivered_ClearedOnReload()
        {
            using var rig = new ControllerRig();
            var seenOnWon = -1;
            using var _ = rig.Model.Outcome.Where(o => o == GameOutcome.Won)
                .Subscribe(__ => seenOnWon = rig.Model.WinFigure.Count);

            rig.Drop(0, new Vec2(1f, 0f), 0f);
            rig.Drop(1, new Vec2(0f, 0f), 0f);
            rig.TickToVerdict();

            Assert.That(seenOnWon, Is.EqualTo(2), "a synchronous Won subscriber (the win flow) already reads the figure");

            rig.Model.LoadLevel(rig.Level);
            Assert.That(rig.Model.WinFigure, Is.Empty);
        }

        [Test]
        public void Controller_Lost_LeavesNoWinFigure()
        {
            using var rig = new ControllerRig();
            rig.Drop(0, new Vec2(5f, 0f), 0f); // nowhere near a slot
            rig.Drop(1, new Vec2(0f, 0f), 0f);
            rig.Drop(2, new Vec2(9f, 0f), 0f);
            rig.TickToVerdict();

            Assert.That(rig.Model.Outcome.CurrentValue, Is.EqualTo(GameOutcome.Lost));
            Assert.That(rig.Model.WinFigure, Is.Empty);
        }

        private static PlacedPiece Sq(float x, float y, float rot) => new("sq", new Vec2(x, y), rot, 4);

        private static TargetFigure TwoSquareTarget()
        {
            var outline = new[] { new Vec2(-0.5f, -0.5f), new Vec2(1.5f, -0.5f), new Vec2(1.5f, 0.5f), new Vec2(-0.5f, 0.5f) };
            return new TargetFigure(new Polygon(outline), new List<TargetSlot>
            {
                new("sq", new Vec2(0f, 0f), 0f),
                new("sq", new Vec2(1f, 0f), 0f),
            });
        }

        /// <summary>The real controller on the scripted pose canvas: a tap releases a piece, the test sets where it
        /// rests, and ticks run the rest timers into the verdict — the same path the device takes, minus physics.</summary>
        private sealed class ControllerRig : System.IDisposable
        {
            public readonly GameModel Model = new();
            public readonly LevelData Level;
            private readonly ScriptedPoseSimulation _sim = new();
            private readonly ShapeFillMechanicController _controller;

            public ControllerRig()
            {
                var square = new PieceType("sq", Shape.Box(1f, 1f), new MaterialProfile(1f, 0.6f, 0.05f), symmetryOrder: 4);
                Level = new LevelData
                {
                    LevelId = "win_figure",
                    Pieces = new List<PiecePlacement>
                    {
                        new(square, new Vec2(-4f, 6f)), new(square, new Vec2(0f, 6f)), new(square, new Vec2(4f, 6f)),
                    },
                    Target = TwoSquareTarget(),
                    MatchTolerance = new MatchTolerance(0.3f, 5f * Deg),
                    Bounds = new Rect(new Vec2(-20f, -20f), new Vec2(20f, 20f)),
                };
                _controller = new ShapeFillMechanicController(Model, _sim, new PoseMatchEvaluator(),
                    new LevelDataValidator(new TapZoneConfig()), new NullPieceViewFactory(), new ShapeFillTuning(),
                    new NullTapTolerance());
                Model.LoadLevel(Level);
                Assert.That(Model.CurrentPhase, Is.EqualTo(GamePhase.Running), "the fixture level must pass the validator");
            }

            /// <summary>Tap the piece at its start pose and let it come to rest at <paramref name="at"/>.</summary>
            public void Drop(int placementIndex, Vec2 at, float rotation)
            {
                var start = Level.Pieces[placementIndex].StartPosition;
                _controller.OnIntent(new InputIntent(start.X, start.Y));
                var body = placementIndex + 1; // bodies are spawned in placement order, ids from 1
                _sim.SetPose(body, at, rotation);
                _sim.SetSpeed(body, 0f);
            }

            public void TickToVerdict()
            {
                for (var i = 0; i < 100 && Model.CurrentPhase == GamePhase.Running; i++)
                    _controller.Tick(0.05f);
            }

            public void Dispose() => _controller.Dispose();
        }
    }
}
