using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// Typed level schema — the TASK-007 pivot delta over the GAME_RULES §5 "vocabulary". Newtonsoft-friendly,
    /// stable field names. The target figure + pre-placed pieces + pose tolerance replace the removed
    /// coverage/pool/dropSpawn model. Numeric tolerances are <c>⟨balancer⟩</c> (starting values, tuned after
    /// playtests).
    /// </summary>
    public sealed class LevelData
    {
        public string LevelId { get; set; }

        /// <summary>The picture-to-build: union outline + per-piece slots (replaces Silhouette).</summary>
        public TargetFigure Target { get; set; }

        /// <summary>Pieces pre-placed on the field at start, all Idle (replaces BlockPool).</summary>
        public IReadOnlyList<PiecePlacement> Pieces { get; set; }

        /// <summary>Pose-match tolerance pair (replaces the scalar CoverageThreshold).</summary>
        public MatchTolerance MatchTolerance { get; set; }

        /// <summary>Floor / cradle geometry helping pieces settle (no longer a coverage container mould).</summary>
        public Polygon[] StaticGeometry { get; set; }

        /// <summary>Optional distractor pieces (mid/late tiers; MVP = empty). GAME_RULES §5.</summary>
        public IReadOnlyList<PiecePlacement> Decoys { get; set; }

        public Rect Bounds { get; set; }
        public Vec2 Gravity { get; set; } = new Vec2(0f, -9.81f);
        public int Seed { get; set; }
        public int DifficultyTier { get; set; }
        public LevelSourceKind Source { get; set; } = LevelSourceKind.Authored;

        /// <summary>Physics behaviour of placed pieces (TASK-021). Default <see cref="PhysicsMode.Assembly"/>
        /// keeps the canonical freeze-on-rest stack; <see cref="PhysicsMode.Balance"/> switches the pivot beam
        /// to a damped world hinge (snap/freeze off, verdict on global rest). See <see cref="PhysicsMode"/>.</summary>
        public PhysicsMode PhysicsMode { get; set; } = PhysicsMode.Assembly;
    }
}
