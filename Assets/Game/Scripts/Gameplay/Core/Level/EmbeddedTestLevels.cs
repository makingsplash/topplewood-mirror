using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// Hand-authored test levels embedded in code — deliberately minimal input for the pivot slice (RFC §7.2),
    /// so no content pipeline is fixed yet. The layout invariant: a piece's start pose is ABOVE its target
    /// slot (there is no aiming — a straight drop under gravity lands it on place; the puzzle is the release
    /// ORDER). All numbers (posTol/angTol, start heights, floor/cradle) are the balancer's starting values
    /// (<c>⟨balancer⟩</c>), UNVERIFIED — solvability of circle_halves/house is level-sim / QA's call.
    /// </summary>
    public sealed class EmbeddedTestLevels : IEmbeddedLevels
    {
        private static readonly MaterialProfile Wood = new MaterialProfile(1.0f, 0.6f, 0.05f);

        private const float PosTol = 0.25f;                 // world units ⟨balancer⟩
        private const float AngTol = 15f * (System.MathF.PI / 180f); // 15° in rad ⟨balancer⟩
        private const float FloorThickness = 0.3f;

        private static readonly Rect DefaultBounds = new Rect(new Vec2(-6f, -6f), new Vec2(6f, 10f));
        private static readonly Vec2 DefaultGravity = new Vec2(0f, -9.81f);

        private readonly List<LevelData> _levels;

        public EmbeddedTestLevels()
        {
            _levels = new List<LevelData> { BuildTwoCubes(), BuildCircleHalves(), BuildHouse() };
        }

        public IReadOnlyList<LevelData> Levels => _levels;
        public LevelData Default => _levels[0];

        // (M1) two_cubes — two identical cubes stacked (the human's requested first playable figure).
        private static LevelData BuildTwoCubes()
        {
            var wood = new PieceType("wood_unit", Shape.Box(1f, 1f), Wood, symmetryOrder: 4);

            var outline = new Polygon(new[]
            {
                new Vec2(-0.5f, -0.5f),
                new Vec2(0.5f, -0.5f),
                new Vec2(0.5f, 1.5f),
                new Vec2(-0.5f, 1.5f),
            });
            var target = new TargetFigure(outline, new[]
            {
                new TargetSlot("wood_unit", new Vec2(0f, 0f), 0f), // bottom
                new TargetSlot("wood_unit", new Vec2(0f, 1f), 0f), // top
            });

            return new LevelData
            {
                LevelId = "two_cubes",
                Target = target,
                Pieces = new[]
                {
                    new PiecePlacement(wood, new Vec2(0f, 3.0f)),
                    new PiecePlacement(wood, new Vec2(0f, 4.5f)),
                },
                MatchTolerance = new MatchTolerance(PosTol, AngTol),
                StaticGeometry = new[] { BuildFloor(-1.5f, 1.5f, -0.5f, FloorThickness) },
                Decoys = null,
                Bounds = DefaultBounds,
                Gravity = DefaultGravity,
                Seed = 1,
                DifficultyTier = 1,
                Source = LevelSourceKind.Authored,
            };
        }

        // (M2) circle_halves — a disc split into two halves. Bottom half sits dome-down in a cradle (rot π),
        // top half domes up on it (rot 0). Cradle + numbers UNVERIFIED (needs level-sim; RFC §7.2).
        private static LevelData BuildCircleHalves()
        {
            // segments:7 ⇒ 8 collider vertices ⇒ one convex Box2D shape (no coordinate-dependent split). >8 would
            // trip the LevelDataValidator ≤8-vertex collider guard (TASK-074) — same rule the shipped levels obey.
            var half = new PieceType("half_disc", Shape.HalfDisc(0.5f, 7), Wood, symmetryOrder: 1);

            var outline = new Polygon(new[]
            {
                new Vec2(-0.5f, -0.5f),
                new Vec2(0.5f, -0.5f),
                new Vec2(0.5f, 0.5f),
                new Vec2(-0.5f, 0.5f),
            });
            var target = new TargetFigure(outline, new[]
            {
                new TargetSlot("half_disc", new Vec2(0f, 0f), System.MathF.PI), // bottom (dome down)
                new TargetSlot("half_disc", new Vec2(0f, 0f), 0f),              // top (dome up)
            });

            return new LevelData
            {
                LevelId = "circle_halves",
                Target = target,
                Pieces = new[]
                {
                    new PiecePlacement(half, new Vec2(0f, 3.0f), System.MathF.PI),
                    new PiecePlacement(half, new Vec2(0f, 4.5f), 0f),
                },
                MatchTolerance = new MatchTolerance(PosTol, AngTol),
                StaticGeometry = BuildCradle(-0.6f, 0.6f, -0.5f, FloorThickness),
                Decoys = null,
                Bounds = DefaultBounds,
                Gravity = DefaultGravity,
                Seed = 3,
                DifficultyTier = 2,
                Source = LevelSourceKind.Authored,
            };
        }

        // (M2) house — box base (■) + triangle roof (▲). Base must be released before the roof (order puzzle).
        private static LevelData BuildHouse()
        {
            var wood = new PieceType("wood_unit", Shape.Box(1f, 1f), Wood, symmetryOrder: 4);
            var roof = new PieceType("roof_tri", Shape.Triangle(1f, 1f), Wood, symmetryOrder: 1);

            // House pentagon: box x[-0.5,0.5] y[-0.5,0.5] + triangle base y=0.5 apex (0,1.5).
            var outline = new Polygon(new[]
            {
                new Vec2(-0.5f, -0.5f),
                new Vec2(0.5f, -0.5f),
                new Vec2(0.5f, 0.5f),
                new Vec2(0f, 1.5f),
                new Vec2(-0.5f, 0.5f),
            });
            var target = new TargetFigure(outline, new[]
            {
                new TargetSlot("wood_unit", new Vec2(0f, 0f), 0f), // base
                new TargetSlot("roof_tri", new Vec2(0f, 1f), 0f),  // roof
            });

            return new LevelData
            {
                LevelId = "house",
                Target = target,
                Pieces = new[]
                {
                    new PiecePlacement(wood, new Vec2(0f, 3.0f)),
                    new PiecePlacement(roof, new Vec2(0f, 4.5f)),
                },
                MatchTolerance = new MatchTolerance(PosTol, AngTol),
                StaticGeometry = new[] { BuildFloor(-1.5f, 1.5f, -0.5f, FloorThickness) },
                Decoys = null,
                Bounds = DefaultBounds,
                Gravity = DefaultGravity,
                Seed = 2,
                DifficultyTier = 2,
                Source = LevelSourceKind.Authored,
            };
        }

        /// <summary>A static floor plate: top edge at <paramref name="topY"/>, extending down by thickness (CCW).</summary>
        private static Polygon BuildFloor(float minX, float maxX, float topY, float th)
            => new Polygon(new[]
            {
                new Vec2(minX, topY - th),
                new Vec2(maxX, topY - th),
                new Vec2(maxX, topY),
                new Vec2(minX, topY),
            });

        /// <summary>Floor + two low side walls forming a cradle so a dome-down half-disc does not roll away.</summary>
        private static Polygon[] BuildCradle(float minX, float maxX, float topY, float th)
        {
            var floor = BuildFloor(minX, maxX, topY, th);
            const float wallH = 0.6f;
            var leftWall = new Polygon(new[]
            {
                new Vec2(minX - th, topY),
                new Vec2(minX, topY),
                new Vec2(minX, topY + wallH),
                new Vec2(minX - th, topY + wallH),
            });
            var rightWall = new Polygon(new[]
            {
                new Vec2(maxX, topY),
                new Vec2(maxX + th, topY),
                new Vec2(maxX + th, topY + wallH),
                new Vec2(maxX, topY + wallH),
            });
            return new[] { floor, leftWall, rightWall };
        }
    }
}
