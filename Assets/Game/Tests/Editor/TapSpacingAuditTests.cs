using System;
using System.Collections.Generic;
using System.Linq;
using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-087 scene-free tests for the tap-spacing audit, metric v2 (<see cref="LevelDataValidator.AuditTapSpacing"/>):
    /// rule 1 — EDGE-TO-EDGE gap of any two tapable start polygons ≥ the tap-zone diameter
    /// (<see cref="TapZoneConfig.MinTapDp"/>); rule 2 — centres of co-release pair members ≥ the two-finger spread
    /// (<see cref="TapZoneConfig.MinCoReleaseSpreadDp"/>). The control case is <c>card_house</c>, verbatim from the
    /// TASK-457 branch (commit 258d5096): the owner pulled it for pieces too close for fingers, while metric v1
    /// (centre-to-centre) reported 0 violations — the fixture must be flagged by BOTH rules. Pure Unity-free domain
    /// code; the validator uses the shipped default <see cref="TapZoneConfig"/> (the same one the sweep predicts with).
    /// </summary>
    [TestFixture]
    public sealed class TapSpacingAuditTests
    {
        // ---------------------------------------------------------------- Geometry.PolygonGap

        [Test]
        public void PolygonGap_ApartSquares_IsTheGap()
            => Assert.That(Geometry.PolygonGap(Square(0f, 0f, 1f), Square(1.5f, 0f, 1f)), Is.EqualTo(0.5f).Within(1e-4f));

        [Test]
        public void PolygonGap_DiagonalSquares_IsCornerToCorner()
            // Corner (0.5,0.5) to corner (1.5,1.5): √2 — the vertex–vertex case of the vertex–edge minimum.
            => Assert.That(Geometry.PolygonGap(Square(0f, 0f, 1f), Square(2f, 2f, 1f)),
                Is.EqualTo(MathF.Sqrt(2f)).Within(1e-4f));

        [Test]
        public void PolygonGap_Touching_IsZero()
            => Assert.That(Geometry.PolygonGap(Square(0f, 0f, 1f), Square(1f, 0f, 1f)), Is.EqualTo(0f));

        [Test]
        public void PolygonGap_Overlapping_IsZero()
            => Assert.That(Geometry.PolygonGap(Square(0f, 0f, 1f), Square(0.5f, 0.2f, 1f)), Is.EqualTo(0f));

        [Test]
        public void PolygonGap_Nested_IsZero()
            => Assert.That(Geometry.PolygonGap(Square(0f, 0f, 2f), Square(0.1f, 0f, 0.5f)), Is.EqualTo(0f));

        [Test]
        public void PolygonGap_CrossWithNoVertexInside_IsZero()
        {
            // Two planks laid over each other as a "+": no vertex of either lies inside the other — only the edge test
            // sees the overlap. A vertex-only implementation would report a positive gap here.
            var horizontal = RectAt(0f, 0f, 3f, 0.2f);
            var vertical = RectAt(0f, 0f, 0.2f, 3f);
            Assert.That(Geometry.PolygonGap(horizontal, vertical), Is.EqualTo(0f));
        }

        [Test]
        public void PolygonGap_RotatedPlankNearSquareCorner_MatchesBruteForce()
        {
            var plank = Geometry.WorldVertices(Shape.Plank(0.3f, 1.6f), new Vec2(1.2f, 1.1f), 0.6f);
            var square = Square(0f, 0f, 1f);
            Assert.That(Geometry.PolygonGap(plank, square), Is.EqualTo(BruteForceGap(plank, square)).Within(1e-3f));
        }

        [Test]
        public void PolygonGap_ConcaveL_NotchDoesNotCountAsInside()
        {
            // A box sitting in the notch of an L, 0.2 off both inner walls: the L's bounding box contains it, but the gap
            // is the real one — a convex-hull metric would call it 0.
            var l = new[]
            {
                new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(2f, 1f), new Vec2(1f, 1f), new Vec2(1f, 2f), new Vec2(0f, 2f),
            };
            var box = RectAt(1.6f, 1.6f, 0.8f, 0.8f); // spans [1.2,2.0]×[1.2,2.0]; inner walls at x=1, y=1
            Assert.That(Geometry.PolygonGap(l, box), Is.EqualTo(0.2f).Within(1e-4f));
        }

        // ---------------------------------------------------------------- control case: card_house

        [Test]
        public void CardHouse_ControlCase_IsFlaggedByBothRules()
        {
            var audit = new LevelDataValidator().AuditTapSpacing(CardHouse());

            // Different taps, rule 1 (edge-to-edge). Hand-computed from the manifest at the predicted orthoSize ≈ 5.47
            // (minTapWorld ≈ 0.574): the ends of neighbouring pairs and the pair-to-slab contacts.
            Assert.That(Pairs(audit, TapSpacingRule.Gap, sameGroup: false), Is.EquivalentTo(new[]
            {
                (1, 2), (1, 5), (1, 6), (2, 5), (2, 6), (4, 5), (4, 6),
            }));
            // Same group, rule 1: the members' top corners meet (gap 0).
            Assert.That(Pairs(audit, TapSpacingRule.Gap, sameGroup: true), Is.EquivalentTo(new[] { (0, 1), (2, 3), (5, 6) }));
            foreach (var v in audit.Violations.Where(v => v.Rule == TapSpacingRule.Gap && v.SameGroup))
                Assert.That(v.Gap, Is.LessThan(1e-3f), $"pair {v.IndexA}-{v.IndexB} meets at a corner");
            // Rule 2: pair centres ≈0.829 apart against a two-finger spread ≈1.197.
            Assert.That(Pairs(audit, TapSpacingRule.CoReleaseSpread, sameGroup: true),
                Is.EquivalentTo(new[] { (0, 1), (2, 3), (5, 6) }));
            Assert.That(audit.CoReleaseSpreadWorld, Is.GreaterThan(audit.MinTapWorld));
        }

        [Test]
        public void CardHouse_CentreToCentre_WasBlind()
        {
            // The v1 metric's failure, pinned: every flagged different-tap pair has centres FARTHER apart than the tap
            // zone — centre-to-centre would have passed every one of them (the sweep reported 0 violations).
            var audit = new LevelDataValidator().AuditTapSpacing(CardHouse());
            var differentTaps = audit.Violations.Where(v => v.Rule == TapSpacingRule.Gap && !v.SameGroup).ToArray();
            Assert.That(differentTaps, Is.Not.Empty);
            foreach (var v in differentTaps)
                Assert.That(v.CentreDistance, Is.GreaterThanOrEqualTo(audit.MinTapWorld),
                    $"pair {v.IndexA}-{v.IndexB}: v1 would have flagged it too");
        }

        [Test]
        public void CentreFar_EdgeClose_IsFlagged()
        {
            // Two long planks end to end, 0.05 apart: centres 3.05 apart (far beyond the zone), edges nearly touching.
            var level = Level(Placement("bar", Shape.Plank(3f, 0.2f), -1.525f, 0f),
                Placement("bar", Shape.Plank(3f, 0.2f), 1.525f, 0f));
            var audit = new LevelDataValidator().AuditTapSpacing(level);
            var v = audit.Violations.Single();
            Assert.That(v.Rule, Is.EqualTo(TapSpacingRule.Gap));
            Assert.That(v.Gap, Is.EqualTo(0.05f).Within(1e-4f));
            Assert.That(v.CentreDistance, Is.GreaterThan(audit.MinTapWorld));
        }

        [Test]
        public void GapNeverExceedsCentreDistance_OnConvexPieces()
        {
            foreach (var v in new LevelDataValidator().AuditTapSpacing(CardHouse()).Violations)
                Assert.That(v.Gap, Is.LessThanOrEqualTo(v.CentreDistance + 1e-5f));
        }

        // ---------------------------------------------------------------- co-release pair rules

        // Two 0.2 boxes on the min-orthoSize frame (3.5): minTapWorld ≈ 0.368, spread ≈ 0.766.
        [Test]
        public void CoReleasePair_GapOk_CentresClose_IsOneSpreadViolation()
        {
            var level = Level(Placement("a", Shape.Box(0.2f, 0.2f), -0.3f, 0f), Placement("b", Shape.Box(0.2f, 0.2f), 0.3f, 0f));
            level.CoReleaseGroups = Group(0, 1);
            var v = new LevelDataValidator().AuditTapSpacing(level).Violations.Single();
            Assert.That(v.Rule, Is.EqualTo(TapSpacingRule.CoReleaseSpread));
            Assert.That(v.SameGroup, Is.True);
            Assert.That(v.CentreDistance, Is.EqualTo(0.6f).Within(1e-4f));
        }

        [Test]
        public void SamePieces_DifferentTaps_NoSpreadRule()
        {
            var level = Level(Placement("a", Shape.Box(0.2f, 0.2f), -0.3f, 0f), Placement("b", Shape.Box(0.2f, 0.2f), 0.3f, 0f));
            Assert.That(new LevelDataValidator().AuditTapSpacing(level).Violations, Is.Empty,
                "the spread rule is for two simultaneous fingers only — separate taps are one finger in turn");
        }

        [Test]
        public void CoReleasePair_FarApart_IsClean()
        {
            var level = Level(Placement("a", Shape.Box(0.2f, 0.2f), -0.5f, 0f), Placement("b", Shape.Box(0.2f, 0.2f), 0.5f, 0f));
            level.CoReleaseGroups = Group(0, 1);
            Assert.That(new LevelDataValidator().AuditTapSpacing(level).Violations, Is.Empty);
        }

        [Test]
        public void JudgeCoReleasePairsOff_DropsPairViolations_KeepsDifferentTaps()
        {
            var config = new TapZoneConfig { JudgeCoReleasePairs = false };
            var audit = new LevelDataValidator(config).AuditTapSpacing(CardHouse());
            Assert.That(audit.Violations.Where(v => v.SameGroup), Is.Empty);
            Assert.That(Pairs(audit, TapSpacingRule.Gap, sameGroup: false), Is.EquivalentTo(new[]
            {
                (1, 2), (1, 5), (1, 6), (2, 5), (2, 6), (4, 5), (4, 6),
            }));
        }

        // ---------------------------------------------------------------- skips

        [Test]
        public void Preplaced_TouchingATapablePiece_IsNotJudged()
        {
            var level = Level(Placement("lever", Shape.Box(1f, 0.2f), 0f, 0f, preplaced: true),
                Placement("ball", Shape.Box(0.2f, 0.2f), 0f, 0.2f));
            Assert.That(new LevelDataValidator().AuditTapSpacing(level).Violations, Is.Empty);
        }

        [Test]
        public void ShapelessPiece_IsSkipped_NoException()
        {
            var level = Level(new PiecePlacement(new PieceType("ghost", null, Material), new Vec2(0f, 0f)),
                Placement("a", Shape.Box(0.2f, 0.2f), 0.1f, 0f), Placement("b", Shape.Box(0.2f, 0.2f), 2f, 0f));
            Assert.DoesNotThrow(() => new LevelDataValidator().AuditTapSpacing(level));
        }

        // ---------------------------------------------------------------- messages

        [Test]
        public void Messages_OfBothRules_KeepTheTapSpacingPrefix()
        {
            // LevelSimRunner counts tap violations by the "Tap spacing:" prefix; both rules must carry it.
            var result = new LevelDataValidator().Validate(CardHouse());
            var all = new List<string>(result.Errors);
            all.AddRange(result.Warnings);
            var tap = all.Where(m => m.StartsWith("Tap spacing:", StringComparison.Ordinal)).ToArray();
            Assert.That(tap, Has.Some.Contains("edge-to-edge"));
            Assert.That(tap, Has.Some.Contains("two-finger spread"));
            Assert.That(tap, Has.Length.EqualTo(new LevelDataValidator().AuditTapSpacing(CardHouse()).Violations.Count));
        }

        // ---------------------------------------------------------------- fixtures / helpers

        private static readonly MaterialProfile Material = new MaterialProfile(1f, 0.6f, 0.05f);

        /// <summary><c>card_house</c> as authored on the TASK-457 branch (258d5096): pieces, co-release groups, target
        /// slots and the floor/legs/stoppers that feed the framing (the predicted orthoSize sets the threshold).</summary>
        private static LevelData CardHouse()
        {
            const float tilt = 0.3491f;
            var card = Shape.Plank(0.3f, 1.6f);
            var slab = Shape.Plank(3.0f, 0.3f);
            var cardA = new PieceType("card_a", card, Material, symmetryOrder: 2);
            var cardB = new PieceType("card_b", card, Material, symmetryOrder: 2);
            var slabT = new PieceType("slab", slab, Material, symmetryOrder: 2);
            var cardC = new PieceType("card_c", card, Material, symmetryOrder: 2);

            var pieces = new[]
            {
                new PiecePlacement(cardA, new Vec2(-1.5146f, 6.45f), -tilt),
                new PiecePlacement(cardA, new Vec2(-0.6854f, 6.45f), tilt),
                new PiecePlacement(cardB, new Vec2(0.6854f, 6.45f), -tilt),
                new PiecePlacement(cardB, new Vec2(1.5146f, 6.45f), tilt),
                new PiecePlacement(slabT, new Vec2(0f, 3.9f), 0f),
                new PiecePlacement(cardC, new Vec2(-0.4146f, 4.9f), -tilt),
                new PiecePlacement(cardC, new Vec2(0.4146f, 4.9f), tilt),
            };
            var slots = new[]
            {
                new TargetSlot("card_a", new Vec2(-1.5146f, 0.818f), -tilt),
                new TargetSlot("card_a", new Vec2(-0.6854f, 0.818f), tilt),
                new TargetSlot("card_b", new Vec2(0.6854f, 0.818f), -tilt),
                new TargetSlot("card_b", new Vec2(1.5146f, 0.818f), tilt),
                new TargetSlot("slab", new Vec2(0f, 1.79f), 0f),
                new TargetSlot("card_c", new Vec2(-0.4146f, 2.76f), -tilt),
                new TargetSlot("card_c", new Vec2(0.4146f, 2.76f), tilt),
            };
            var outline = new Polygon(new[]
            {
                new Vec2(-1.9f, 0f), new Vec2(1.9f, 0f), new Vec2(1.9f, 1.94f), new Vec2(0.85f, 1.94f),
                new Vec2(0f, 3.6f), new Vec2(-0.85f, 1.94f), new Vec2(-1.9f, 1.94f),
            });
            var statics = new[]
            {
                new Polygon(Corners(-2.4f, -0.3f, 2.4f, 0f)),
                new Polygon(Corners(-2.2f, -2.2f, -1.95f, -0.3f)),
                new Polygon(Corners(1.95f, -2.2f, 2.2f, -0.3f)),
                new Polygon(Corners(-2.094f, 0f, -1.934f, 0.2f)),
                new Polygon(Corners(-0.266f, 0f, -0.106f, 0.2f)),
                new Polygon(Corners(0.106f, 0f, 0.266f, 0.2f)),
                new Polygon(Corners(1.934f, 0f, 2.094f, 0.2f)),
            };
            return new LevelData
            {
                LevelId = "card_house",
                PhysicsMode = PhysicsMode.Balance,
                FreezeLoadsAtRest = true,
                Pieces = pieces,
                Target = new TargetFigure(outline, slots),
                StaticGeometry = statics,
                Bounds = new Rect(new Vec2(-3.5f, -3f), new Vec2(3.5f, 9f)),
                CoReleaseGroups = new IReadOnlyList<int>[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 5, 6 } },
            };
        }

        private static LevelData Level(params PiecePlacement[] pieces) => new LevelData { LevelId = "t", Pieces = pieces };

        private static PiecePlacement Placement(string kind, Shape shape, float x, float y, bool preplaced = false)
            => new PiecePlacement(new PieceType(kind, shape, Material), new Vec2(x, y), 0f, preplaced);

        private static IReadOnlyList<IReadOnlyList<int>> Group(params int[] members)
            => new IReadOnlyList<int>[] { members };

        private static (int, int)[] Pairs(TapSpacingAudit audit, TapSpacingRule rule, bool sameGroup)
            => audit.Violations.Where(v => v.Rule == rule && v.SameGroup == sameGroup)
                .Select(v => (v.IndexA, v.IndexB)).ToArray();

        private static Vec2[] Square(float cx, float cy, float side) => RectAt(cx, cy, side, side);

        private static Vec2[] RectAt(float cx, float cy, float w, float h)
            => Corners(cx - w / 2f, cy - h / 2f, cx + w / 2f, cy + h / 2f);

        private static Vec2[] Corners(float x0, float y0, float x1, float y1)
            => new[] { new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1) };

        /// <summary>Independent reference: dense samples along every edge of both polygons, min distance to the other
        /// boundary. Valid only for disjoint polygons (the test's case); error ≤ half the sample step.</summary>
        private static float BruteForceGap(Vec2[] a, Vec2[] b)
        {
            const int steps = 2000;
            var best = float.MaxValue;
            foreach (var (from, to) in new[] { (a, b), (b, a) })
            {
                for (var i = 0; i < from.Length; i++)
                {
                    var p = from[i];
                    var q = from[(i + 1) % from.Length];
                    for (var s = 0; s <= steps; s++)
                    {
                        var t = s / (float)steps;
                        best = MathF.Min(best, Geometry.DistanceToPolygonBoundary(to, p + (q - p) * t));
                    }
                }
            }
            return best;
        }
    }
}
