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

        /// <summary>TASK-031c: optional render descriptors, parallel by index to <see cref="StaticGeometry"/> (a
        /// static is invisible unless its descriptor sets <c>Visible</c>). Never drives the sim — the physics still
        /// reads only <see cref="StaticGeometry"/>. May be null for old data; consumers guard on length.</summary>
        public IReadOnlyList<StaticVisual> StaticVisuals { get; set; }

        /// <summary>Optional distractor pieces (mid/late tiers; MVP = empty). GAME_RULES §5.</summary>
        public IReadOnlyList<PiecePlacement> Decoys { get; set; }

        public Rect Bounds { get; set; }
        public Vec2 Gravity { get; set; } = new Vec2(0f, -9.81f);
        public int Seed { get; set; }
        public int DifficultyTier { get; set; }
        public LevelSourceKind Source { get; set; } = LevelSourceKind.Authored;

        /// <summary>Physics behaviour of placed pieces (TASK-021). Default <see cref="PhysicsMode.Assembly"/>
        /// keeps the canonical freeze-on-rest stack; <see cref="PhysicsMode.Balance"/> runs the pivot beam as a
        /// FREE Dynamic body resting on its fulcrum — no world hinge/pin (TASK-023/027 de-pinned it; the "damped
        /// world hinge" this doc once claimed has not existed since) — with the verdict deferred to global rest.
        /// See <see cref="PhysicsMode"/>.</summary>
        public PhysicsMode PhysicsMode { get; set; } = PhysicsMode.Assembly;

        /// <summary>TASK-031d (pure-physics, opt-in on Balance/Catapult). When true, at global rest each placed
        /// non-pivot piece is frozen EXACTLY at its physical rest pose (claiming its nearest same-Kind slot for the
        /// win check) instead of gliding onto the authored slot via <c>BalanceSnapLoads</c> — the piece must reach
        /// the silhouette by real physics, and posTol/angTol only JUDGE the result. Default false keeps the existing
        /// catapult/seesaw levels on the glide path byte-for-byte.</summary>
        public bool FreezeLoadsAtRest { get; set; }

        /// <summary>TASK-097: optional directed air-flow zones (propeller/fan — environment force). Null/absent ⇒
        /// no flow, so every existing level is byte-for-byte unchanged. The physics adapter applies each zone's
        /// mass-independent acceleration to every RELEASED Dynamic body whose centre is inside it (see
        /// <see cref="AirZone"/>). Consumers guard on null (mirrors <see cref="Decoys"/> / <see cref="StaticVisuals"/>).</summary>
        public AirZone[] AirZones { get; set; }

        /// <summary>TASK-237: certified winning release order (placement indices) — the source of the Hint booster's
        /// "next piece" (<see cref="Gameplay.Core.Mechanic.NextHintResolver"/>), and, later, of the tutorial (closes
        /// the dormant TASK-078/079 hardcode). Absent → null → the hint is a no-op (it never GUESSES an order). The
        /// order is the sim's deterministic winning policy order (slot-ascending for assembly, pair-release for
        /// balance/catapult), so "the hint never lies" is true by construction (balancer §7). The sim's bot policies
        /// compute their own orders; since TASK-427 the sweep ALSO replays this order as a separate bucket
        /// (<see cref="Gameplay.Core.Sim.SolveSweepPlan.PinnedOrder"/>) on levels too large to enumerate, so a narrow
        /// winning set stays provable — it never alters any other run's poseHash; consumers guard on null
        /// (mirrors <see cref="AirZones"/>/<see cref="Decoys"/>/<see cref="StaticVisuals"/>).</summary>
        public IReadOnlyList<int> WinningOrder { get; set; }

        /// <summary>TASK-374: groups of placement indices (into <see cref="Pieces"/>) that must be tapped TOGETHER to
        /// fall — the "co-release" rule. A tap on a member alone does NOTHING; the whole group releases in one step
        /// only once every member has been tapped inside <c>ShapeFillTuning.CoReleaseWindowSteps</c> (else the group
        /// silently resets). A piece not listed in any group releases immediately on tap, in every mode. Any number of
        /// groups, each of size ≥2; a piece belongs to at most one group. Absent → null → no groups → every level up to
        /// TASK-374 is byte-for-byte unchanged (the same additive-back-compat contract as
        /// <see cref="AirZones"/>/<see cref="WinningOrder"/>/<see cref="Decoys"/>). The sim does NOT read this field to
        /// build its bot orders — the DOMAIN enforces the rule in <c>OnIntent</c>, so poseHash of the winning
        /// (synchronous-pair) path is unchanged; consumers guard on null.</summary>
        public IReadOnlyList<IReadOnlyList<int>> CoReleaseGroups { get; set; }
    }
}
