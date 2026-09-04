using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Gameplay.Core
{
    /// <summary>
    /// Serialization layer for authored levels (TASK-009): the JSON storage shape, kept OUT of the clean
    /// domain <see cref="LevelData"/>. Newtonsoft with stable field names, Unity-free. The human mandate
    /// (comment 15:04) — "levels are hardcoded, design a storage system and assemble levels in it" — is met
    /// by hand-authored JSON in StreamingAssets (see <c>shapefill_levels.json</c>) mapped here to the domain.
    /// One file groups the small related POCOs.
    /// </summary>
    public sealed class CampaignFileDto
    {
        public const int CurrentSchemaVersion = 1;

        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        [JsonProperty("levels")] public List<LevelDataDto> Levels { get; set; } = new();
    }

    /// <summary>One authored level. <c>pieceTypes</c> is a per-level catalogue keyed by <c>kind</c>; both
    /// <c>pieces</c> and target <c>slots</c> reference a kind (mirrors the identity-agnostic domain match).</summary>
    public sealed class LevelDataDto
    {
        [JsonProperty("levelId")] public string LevelId { get; set; }
        [JsonProperty("seed")] public int Seed { get; set; }
        [JsonProperty("difficultyTier")] public int DifficultyTier { get; set; }
        [JsonProperty("matchTolerance")] public MatchToleranceDto MatchTolerance { get; set; }
        [JsonProperty("bounds")] public RectDto Bounds { get; set; }
        [JsonProperty("gravity")] public Vec2Dto Gravity { get; set; }
        [JsonProperty("pieceTypes")] public List<PieceTypeDto> PieceTypes { get; set; } = new();
        [JsonProperty("physicsMode")] public string PhysicsMode { get; set; } // null/absent → Assembly (TASK-021)
        [JsonProperty("source")] public string Source { get; set; } // null/absent → Authored (TASK-185)
        [JsonProperty("freezeLoadsAtRest")] public bool FreezeLoadsAtRest { get; set; } // absent → false (TASK-031d)
        [JsonProperty("target")] public TargetFigureDto Target { get; set; }
        [JsonProperty("pieces")] public List<PiecePlacementDto> Pieces { get; set; } = new();
        [JsonProperty("staticGeometry")] public List<PolygonDto> StaticGeometry { get; set; } = new();
        [JsonProperty("airZones")] public List<AirZoneDto> AirZones { get; set; } = new(); // absent/empty → null → no flow (TASK-097)
        [JsonProperty("winningOrder")] public List<int> WinningOrder { get; set; } // absent/empty → null → no hint (TASK-237)

        public LevelData ToDomain()
        {
            var types = new Dictionary<string, PieceType>(StringComparer.Ordinal);
            foreach (var t in PieceTypes)
            {
                if (t == null || string.IsNullOrEmpty(t.Kind))
                    throw new InvalidOperationException($"Level '{LevelId}': a pieceType has no kind.");
                types[t.Kind] = t.ToDomain();
            }

            var slots = new List<TargetSlot>(Target?.Slots?.Count ?? 0);
            if (Target?.Slots != null)
                foreach (var s in Target.Slots)
                    slots.Add(new TargetSlot(s.Kind, s.Position.ToDomain(), s.Rotation));

            var pieces = new List<PiecePlacement>(Pieces.Count);
            foreach (var p in Pieces)
            {
                if (!types.TryGetValue(p.Kind, out var type))
                    throw new InvalidOperationException(
                        $"Level '{LevelId}': piece references unknown kind '{p.Kind}'.");
                pieces.Add(new PiecePlacement(type, p.StartPosition.ToDomain(), p.StartRotation, p.Preplaced, p.Expendable));
            }

            var statics = new Polygon[StaticGeometry.Count];
            var visuals = new StaticVisual[StaticGeometry.Count];
            for (var i = 0; i < StaticGeometry.Count; i++)
            {
                statics[i] = StaticGeometry[i].ToDomain();
                visuals[i] = new StaticVisual(StaticGeometry[i].Visible, StaticGeometry[i].Color);
            }

            // TASK-097: absent/empty → null so every pre-TASK-097 level maps to no flow, exactly like Decoys=null.
            AirZone[] airZones = null;
            if (AirZones != null && AirZones.Count > 0)
            {
                airZones = new AirZone[AirZones.Count];
                for (var i = 0; i < AirZones.Count; i++)
                    airZones[i] = AirZones[i].ToDomain();
            }

            return new LevelData
            {
                LevelId = LevelId,
                Target = new TargetFigure(new Polygon(Target.ToOutlineVertices()), slots),
                Pieces = pieces,
                MatchTolerance = MatchTolerance.ToDomain(),
                StaticGeometry = statics,
                StaticVisuals = visuals,
                Decoys = null,
                Bounds = Bounds.ToDomain(),
                Gravity = Gravity.ToDomain(),
                Seed = Seed,
                DifficultyTier = DifficultyTier,
                Source = ParseSource(Source),
                PhysicsMode = ParsePhysicsMode(PhysicsMode),
                FreezeLoadsAtRest = FreezeLoadsAtRest,
                AirZones = airZones,
                // TASK-237: absent/empty → null so every pre-TASK-237 level maps to no hint, exactly like AirZones.
                WinningOrder = (WinningOrder != null && WinningOrder.Count > 0) ? WinningOrder : null,
            };
        }

        /// <summary>TASK-185: map the optional <c>source</c> provenance string to the domain enum. Absent/empty →
        /// Authored so every pre-TASK-185 level (and any hand-authored level) parses byte-for-byte unchanged — the
        /// same back-compat contract as <c>physicsMode</c>/<c>airZones</c> (no schema bump, no migration). An unknown
        /// value is a hard authoring error (mirrors <see cref="ParsePhysicsMode"/>). This makes
        /// <see cref="LevelSourceKind.Generated"/> reachable from data for the first time: before this the domain enum
        /// value existed but <c>ToDomain()</c> hardcoded <c>Authored</c>, so no content path could tag a level as
        /// generated (see the family generator, <c>Gameplay.Sim.Editor.LevelFamilyGenerator</c>).</summary>
        private LevelSourceKind ParseSource(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return LevelSourceKind.Authored;
            switch (raw)
            {
                case "authored": return LevelSourceKind.Authored;
                case "generated": return LevelSourceKind.Generated;
                default:
                    throw new InvalidOperationException(
                        $"Level '{LevelId}': unknown source '{raw}' (expected 'authored' or 'generated').");
            }
        }

        /// <summary>Map the optional <c>physicsMode</c> string to the domain enum. Absent/empty → Assembly (the
        /// canonical stack, so every existing level parses unchanged); an unknown value is a hard authoring
        /// error (mirrors <see cref="ShapeDto"/>'s unknown-primitive throw).</summary>
        private PhysicsMode ParsePhysicsMode(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return Gameplay.Core.PhysicsMode.Assembly;
            switch (raw)
            {
                case "assembly": return Gameplay.Core.PhysicsMode.Assembly;
                case "balance": return Gameplay.Core.PhysicsMode.Balance;
                case "catapult": return Gameplay.Core.PhysicsMode.Catapult;
                default:
                    throw new InvalidOperationException(
                        $"Level '{LevelId}': unknown physicsMode '{raw}' (expected 'assembly', 'balance' or 'catapult').");
            }
        }
    }

    public sealed class Vec2Dto
    {
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }

        public Vec2 ToDomain() => new Vec2(X, Y);
    }

    public sealed class RectDto
    {
        [JsonProperty("min")] public Vec2Dto Min { get; set; }
        [JsonProperty("max")] public Vec2Dto Max { get; set; }

        public Rect ToDomain() => new Rect(Min.ToDomain(), Max.ToDomain());
    }

    public sealed class MatchToleranceDto
    {
        [JsonProperty("posTol")] public float PosTol { get; set; }
        [JsonProperty("angTol")] public float AngTol { get; set; }
        // TASK-043: rigidity spread bound. Absent → 0 → the rigidity constraint is OFF (every existing level is
        // judged byte-for-byte until a level explicitly authors it — currently only grand_seesaw §L9).
        [JsonProperty("rigidityTol")] public float RigidityTol { get; set; }

        public MatchTolerance ToDomain() => new MatchTolerance(PosTol, AngTol, RigidityTol);
    }

    public sealed class MaterialProfileDto
    {
        [JsonProperty("density")] public float Density { get; set; }
        [JsonProperty("friction")] public float Friction { get; set; }
        [JsonProperty("restitution")] public float Restitution { get; set; }

        public MaterialProfile ToDomain() => new MaterialProfile(Density, Friction, Restitution);
    }

    /// <summary>A convex primitive, reconstructed via the <see cref="Shape"/> factories (no raw vertex lists
    /// to author). Discriminated by <c>primitive</c>: box | triangle | halfdisc | plank | trapezoid |
    /// parallelogram | righttriangle | quarterdisc | gable | circle.
    /// <c>topWidth</c> feeds trapezoid (top edge), <c>skew</c> feeds parallelogram (top-edge +X shift),
    /// <c>apexHeight</c> feeds gable (roof height above the rectangular base).</summary>
    public sealed class ShapeDto
    {
        [JsonProperty("primitive")] public string Primitive { get; set; }
        [JsonProperty("width")] public float Width { get; set; }
        [JsonProperty("height")] public float Height { get; set; }
        [JsonProperty("radius")] public float Radius { get; set; }
        [JsonProperty("segments")] public int Segments { get; set; } = 12;
        [JsonProperty("topWidth")] public float TopWidth { get; set; }   // trapezoid: top-edge width
        [JsonProperty("skew")] public float Skew { get; set; }           // parallelogram: top-edge +X shift
        [JsonProperty("apexHeight")] public float ApexHeight { get; set; } // gable: roof height above the rectangular base
        [JsonProperty("cell")] public float Cell { get; set; }            // tetrominoes: world size of one grid cell (width/height do not describe a fixed-grid polyomino)

        public Shape ToDomain()
        {
            switch (Primitive)
            {
                case "box": return Shape.Box(Width, Height);
                case "triangle": return Shape.Triangle(Width, Height);
                case "halfdisc": return Shape.HalfDisc(Radius, Segments);
                case "circle":
                {
                    if (Radius <= 0f)
                        throw new InvalidOperationException($"circle needs positive radius; got {Radius}.");
                    return Shape.Circle(Radius, Segments);
                }
                case "plank":
                {
                    var w = Width > 0f ? Width : 1f;
                    var h = Height > 0f ? Height : 2f;
                    return Shape.Plank(w, h);
                }
                case "trapezoid":
                {
                    if (Width <= 0f || TopWidth <= 0f || Height <= 0f)
                        throw new InvalidOperationException(
                            $"trapezoid needs positive width/topWidth/height; got {Width}/{TopWidth}/{Height}.");
                    return Shape.Trapezoid(Width, TopWidth, Height);
                }
                case "parallelogram":
                {
                    if (Width <= 0f || Height <= 0f)
                        throw new InvalidOperationException(
                            $"parallelogram needs positive width/height; got {Width}/{Height}.");
                    return Shape.Parallelogram(Width, Height, Skew);
                }
                case "righttriangle":
                {
                    if (Width <= 0f || Height <= 0f)
                        throw new InvalidOperationException(
                            $"righttriangle needs positive width/height; got {Width}/{Height}.");
                    return Shape.RightTriangle(Width, Height);
                }
                case "quarterdisc":
                {
                    if (Radius <= 0f)
                        throw new InvalidOperationException(
                            $"quarterdisc needs positive radius; got {Radius}.");
                    return Shape.QuarterDisc(Radius, Segments);
                }
                case "gable":
                {
                    if (Width <= 0f || Height <= 0f || ApexHeight <= 0f)
                        throw new InvalidOperationException(
                            $"gable needs positive width/height/apexHeight; got {Width}/{Height}/{ApexHeight}.");
                    return Shape.Gable(Width, Height, ApexHeight);
                }
                // TASK-178: concave tetrominoes — a fixed integer grid scaled by `cell`; width/height/radius do not apply.
                case "l_tetromino": return Shape.LTetromino(RequireCell());
                case "j_tetromino": return Shape.JTetromino(RequireCell());
                case "s_tetromino": return Shape.STetromino(RequireCell());
                case "z_tetromino": return Shape.ZTetromino(RequireCell());
                case "t_tetromino": return Shape.TTetromino(RequireCell());
                // TASK-314: concave pentominoes — same fixed-grid `cell` contract; width/height/radius do not apply.
                case "t_pentomino":    return Shape.TPentomino(RequireCell());
                case "n_pentomino":    return Shape.NPentomino(RequireCell());
                case "z_pentomino":    return Shape.ZPentomino(RequireCell());
                case "v_pentomino":    return Shape.VPentomino(RequireCell());
                case "p_pentomino":    return Shape.PPentomino(RequireCell());
                case "plus_pentomino": return Shape.PlusPentomino(RequireCell());
                // TASK-330: L-pentomino — same fixed-grid `cell` contract; width/height/radius do not apply.
                case "l_pentomino":    return Shape.LPentomino(RequireCell());
                default:
                    throw new InvalidOperationException($"Unknown shape primitive '{Primitive}'.");
            }
        }

        // TASK-178: a tetromino's geometry is fixed by its grid; `cell` (world units per cell) is its only dimension.
        private float RequireCell()
        {
            if (Cell <= 0f)
                throw new InvalidOperationException($"{Primitive} needs positive cell; got {Cell}.");
            return Cell;
        }
    }

    public sealed class PieceTypeDto
    {
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("color")] public string Color { get; set; }
        [JsonProperty("symmetryOrder")] public int SymmetryOrder { get; set; } = 1;
        [JsonProperty("pivot")] public bool Pivot { get; set; } // balance-mode hinge beam (TASK-021); default false
        [JsonProperty("material")] public MaterialProfileDto Material { get; set; }
        [JsonProperty("shape")] public ShapeDto Shape { get; set; }

        public PieceType ToDomain()
            => new PieceType(Kind, Shape.ToDomain(), Material.ToDomain(), Color, SymmetryOrder, Pivot);
    }

    public sealed class PiecePlacementDto
    {
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("startPosition")] public Vec2Dto StartPosition { get; set; }
        [JsonProperty("startRotation")] public float StartRotation { get; set; }
        [JsonProperty("preplaced")] public bool Preplaced { get; set; } // absent → false (TASK-031b)
        [JsonProperty("expendable")] public bool Expendable { get; set; } // absent → false → essential (TASK-054)
    }

    public sealed class TargetSlotDto
    {
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("position")] public Vec2Dto Position { get; set; }
        [JsonProperty("rotation")] public float Rotation { get; set; }
    }

    public sealed class TargetFigureDto
    {
        [JsonProperty("outline")] public List<Vec2Dto> Outline { get; set; } = new();
        [JsonProperty("slots")] public List<TargetSlotDto> Slots { get; set; } = new();

        public Vec2[] ToOutlineVertices()
        {
            var verts = new Vec2[Outline.Count];
            for (var i = 0; i < Outline.Count; i++)
                verts[i] = Outline[i].ToDomain();
            return verts;
        }
    }

    /// <summary>Static-geometry block. <c>outline</c> is a closed solid contour by default; <c>surface:true</c>
    /// (TASK-325) reinterprets it as an OPEN polyline surface (chute/ramp) traced by an edge-chain collider, with an
    /// optional <c>thickness</c> for the rendered ribbon. <c>collider:false</c> (TASK-346) makes the block VISUAL-ONLY
    /// — it is drawn (a concave mass through the ear-clip triangulator) but builds NO collider, so a concave silhouette
    /// can be shown without the forbidden concave FILLED collider (Box2D's platform-dependent split, TASK-307). All
    /// three fields absent → the pre-TASK-325 filled solid WITH a collider, byte for byte (the same additive-back-compat
    /// contract as <c>airZones</c>/<c>winningOrder</c>).</summary>
    public sealed class PolygonDto
    {
        [JsonProperty("outline")] public List<Vec2Dto> Outline { get; set; } = new();
        [JsonProperty("visible")] public bool Visible { get; set; }        // absent → false (TASK-031c)
        [JsonProperty("color")] public string Color { get; set; }          // absent → null → view default
        [JsonProperty("surface")] public bool Surface { get; set; }        // absent → false → filled solid (TASK-325)
        [JsonProperty("thickness")] public float Thickness { get; set; }   // surface only; absent/0 → StaticSurface.DefaultThickness
        [JsonProperty("collider")] public bool Collider { get; set; } = true; // absent → true → builds a collider (TASK-346)

        public Polygon ToDomain()
        {
            var verts = new Vec2[Outline.Count];
            for (var i = 0; i < Outline.Count; i++)
                verts[i] = Outline[i].ToDomain();
            // absent surface flag ⇒ null ⇒ filled solid (unchanged); thickness normalised inside StaticSurface.
            var surface = Surface ? new StaticSurface(Thickness) : null;
            return new Polygon(verts, surface: surface, hasCollider: Collider);
        }
    }

    /// <summary>TASK-097: one authored air-flow zone. <c>region</c> is the finite force area (world space, == the
    /// rendered mesh, §D5); <c>direction</c> is normalised to a unit vector here so authors may write any non-zero
    /// vector; <c>acceleration</c> is mass-independent units/s² (balancer default 3.0, band [2,4]); <c>color</c>
    /// tints the visible zone mesh. A zero-length direction is left as-is → LevelDataValidator flags it (ERROR).
    /// TASK-107: <c>bypassHoverGuard</c> (optional, absent → false) opts THIS zone out of the hover-guard so a real
    /// up-launcher may lift a piece; the burden of proving termination then moves to the sim gate (§L(air) Rule 4).</summary>
    public sealed class AirZoneDto
    {
        [JsonProperty("region")] public RectDto Region { get; set; }
        [JsonProperty("direction")] public Vec2Dto Direction { get; set; }
        [JsonProperty("acceleration")] public float Acceleration { get; set; }
        [JsonProperty("color")] public string Color { get; set; }
        [JsonProperty("bypassHoverGuard")] public bool BypassHoverGuard { get; set; } // absent → false → guard on (TASK-107)

        public AirZone ToDomain()
        {
            var dir = Direction != null ? Direction.ToDomain() : Vec2.Zero;
            var len = dir.Length;
            var unit = len > 0f ? dir * (1f / len) : dir; // normalise; zero stays zero → validator rejects
            var region = Region != null ? Region.ToDomain() : default;
            return new AirZone(region, unit, Acceleration, Color, BypassHoverGuard);
        }
    }
}
