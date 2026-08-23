// TASK-185 — Unity-FREE core of the level-family generator. Deliberately references NO UnityEngine/UnityEditor: it is
// the single implementation of skeleton expansion + envelope validation + manifest emission, called BOTH by the Unity
// editor batch entry (Gameplay.Sim.Editor.LevelFamilyGenerator.Gen, which adds env/file-I/O/Exit) AND by a standalone
// dotnet golden-run harness (tools that reuse these exact domain sources — zero schema/geometry drift, the CLAUDE.md
// "самодельная переагрегация даёт ложную тревогу" hazard the architect flagged). It reuses the REAL domain types:
// builds pieces via ShapeDto→Shape factories, converts with LevelDataDto.ToDomain(), and validates with the REAL
// LevelDataValidator (structural + machine-readable tap-spacing audit, §D12). No I/O here — inputs/outputs are strings.
using System;
using System.Collections.Generic;
using System.Linq;
using Gameplay.Core;
using Newtonsoft.Json;

namespace Gameplay.Sim.Editor
{
    /// <summary>Skeleton input format — PROJECT DATA (passed as an argument), deliberately NOT part of the runtime
    /// <see cref="LevelData"/> schema (TASK-180/02 §4.3). The generator expands one of these into ordinary level
    /// records; the runtime never sees the skeleton file.</summary>
    public sealed class SkeletonFileDto
    {
        [JsonProperty("family")] public string Family { get; set; }
        [JsonProperty("skeleton")] public string Skeleton { get; set; }
        [JsonProperty("physicsMode")] public string PhysicsMode { get; set; }
        [JsonProperty("difficultyTier")] public int DifficultyTier { get; set; }
        [JsonProperty("seedBase")] public int SeedBase { get; set; }
        [JsonProperty("bounds")] public RectDto Bounds { get; set; }
        [JsonProperty("gravity")] public Vec2Dto Gravity { get; set; }
        [JsonProperty("matchTolerance")] public MatchToleranceDto MatchTolerance { get; set; }
        [JsonProperty("material")] public MaterialProfileDto Material { get; set; }
        [JsonProperty("plinthColor")] public string PlinthColor { get; set; }
        [JsonProperty("plinthMargin")] public float? PlinthMargin { get; set; }
        [JsonProperty("plinthDepth")] public float? PlinthDepth { get; set; }
        [JsonProperty("variations")] public List<SkeletonVariationDto> Variations { get; set; }
    }

    public sealed class SkeletonVariationDto
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("difficultyTier")] public int? DifficultyTier { get; set; }
        [JsonProperty("layers")] public List<SkeletonLayerDto> Layers { get; set; }

        // TASK-198: off-axis base modules (S1b/S2/S3). Absent → the skeleton's default module (S1 → slab), so every
        // pre-TASK-198 skeleton (s1_stack.json) parses and emits byte-for-byte unchanged. See FamilyExpander.BaseModule.
        [JsonProperty("baseModule")] public string BaseModule { get; set; }
        // twin-leg / post-span: the pair of same-kind supports (legs/posts) and their centre-to-centre span. The
        // central Layers[] then stack on the deck at y = support height (Layers[0] is the bridge that bears on both).
        [JsonProperty("baseSupport")] public SkeletonLayerDto BaseSupport { get; set; }
        [JsonProperty("supportSpan")] public float SupportSpan { get; set; }
        // grid: uniform tile primitive + per-row tile counts (bottom-up); §L1 lateral clearance between neighbours.
        [JsonProperty("tile")] public SkeletonLayerDto Tile { get; set; }
        [JsonProperty("rows")] public List<SkeletonGridRowDto> Rows { get; set; }
        [JsonProperty("clearance")] public float? Clearance { get; set; }
    }

    /// <summary>One grid row (S3): a count of uniform tiles laid out bottom-up. A row of N-1 tiles above a row of N
    /// saddles the seams (pyramid); an equal count stacks aligned columns (rectangular). TASK-198.</summary>
    public sealed class SkeletonGridRowDto
    {
        [JsonProperty("count")] public int Count { get; set; }
    }

    public sealed class SkeletonLayerDto
    {
        [JsonProperty("primitive")] public string Primitive { get; set; }
        [JsonProperty("width")] public float Width { get; set; }
        [JsonProperty("topWidth")] public float TopWidth { get; set; }
        [JsonProperty("height")] public float Height { get; set; }
        [JsonProperty("apexHeight")] public float ApexHeight { get; set; }
        [JsonProperty("symmetryOrder")] public int? SymmetryOrder { get; set; }

        public ShapeDto ToShapeDto() => new()
        {
            Primitive = Primitive,
            Width = Width,
            TopWidth = TopWidth,
            Height = Height,
            ApexHeight = ApexHeight,
        };
    }

    /// <summary>
    /// Expands an assembly flat-stack skeleton into campaign level records, rejecting any variation outside the
    /// proven-safe assembly envelope (§L1/§L2/§L12) with a NAMED reason (owner "Жёсткие требования": a variation that
    /// fails the envelope is not emitted). Deterministic: slots stack from the layer heights, hang heights follow the
    /// §L12 anti-spoiler table, colours follow the §L8 palette formula, and the target outline is the union silhouette.
    /// TASK-198: the single master skeleton carries a <c>base_module</c> — <b>slab</b> (S1, one central column, the
    /// original path, emitted byte-for-byte as before), <b>twin-leg</b> (S1b, a symmetric same-kind support pair + a
    /// central stack bridging them), <b>post-span</b> (S2, two same-kind posts + a plank span that physically requires
    /// the posts first — a span dropped alone falls, the built-in falsifiable), and <b>grid</b> (S3, rows of same-kind
    /// tiles saddling with §L1 clearance). Off-axis columns inherit ALL S1 invariants: flat-on-flat, §L1 clearance
    /// between same-kind neighbours, §L12 hang scramble, §D3 direct drop (hang.x == slot.x). Pure — no I/O, no engine
    /// references (reused by the batch-Unity entry and the dotnet golden run alike).
    /// </summary>
    public sealed class FamilyExpander
    {
        // §L8 canonical palette. color = PALETTE[(pieceTypeIndex + levelPosition) % 10]; levelPosition = the level's
        // 0-based index in levels[] AT INSERTION (§L8 "Гоча — позиция ≠ seed").
        private static readonly string[] Palette =
        {
            "#E23B3B", "#F2801D", "#F4C20D", "#4CAF50", "#12A5A5",
            "#2E7CD6", "#5B4FE0", "#B24BD1", "#E84B8A", "#8D6E4A",
        };

        // §L12 anti-spoiler hang table: HangOrder[k][p] = the 1-based TIER at hang position p (p=0 lowest, bottom-up).
        // Tier t maps to layer index t-1 (tier 1 = lowest slot = base layer). Verbatim from level-authoring §L12; k=2
        // is the documented monotone exception (sailboat).
        private static readonly Dictionary<int, int[]> HangOrder = new()
        {
            { 2, new[] { 2, 1 } },
            { 3, new[] { 2, 3, 1 } },
            { 4, new[] { 3, 1, 4, 2 } },
            { 5, new[] { 3, 5, 1, 4, 2 } },
            { 6, new[] { 4, 1, 5, 2, 6, 3 } },
            { 7, new[] { 4, 7, 2, 5, 1, 6, 3 } },
            { 8, new[] { 5, 1, 6, 3, 8, 2, 7, 4 } },
        };

        private const float HangCeiling = 7.0f;
        private const float MinGap = 0.10f;
        private const float DefaultGap = 0.50f;
        private const float TaperEps = 1e-3f;

        // TASK-198 (off-axis modules).
        private const float MinClearance = 0.05f;    // §L1 minimum lateral clearance between same-kind neighbours.
        private const float OverhangBudget = 0.5f;   // §L2 max cantilever of a bridge/span beyond a support flat top.

        /// <summary>The base module of the assembly master skeleton (TASK-198).</summary>
        private enum BaseModule { Slab, TwinLeg, PostSpan, Grid }

        public sealed class Result
        {
            public bool Ok;                                     // false only for a structural/config failure (bad skeleton)
            public string Error;                                // set when Ok == false
            public string AppendedManifest;                     // full manifest text with new levels, or null if none
            public readonly List<string> EmittedIds = new();
            public readonly List<(string Id, string Reason)> Rejected = new();
            public readonly List<string> Log = new();
        }

        public Result Expand(SkeletonFileDto skeleton, string existingManifestText)
        {
            var r = new Result();
            if (skeleton?.Variations == null || skeleton.Variations.Count == 0)
            {
                r.Error = "skeleton has no variations."; return r;
            }
            if (!TryResolveSkeletonModule(skeleton.Skeleton, out var defaultModule, out var skErr))
            {
                r.Error = skErr;
                return r;
            }

            var existing = JsonConvert.DeserializeObject<CampaignFileDto>(existingManifestText);
            var existingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var lv in existing?.Levels ?? new List<LevelDataDto>())
                if (!string.IsNullOrEmpty(lv.LevelId))
                    existingIds.Add(lv.LevelId);
            var basePosition = existing?.Levels?.Count ?? 0;

            var validator = new LevelDataValidator();
            var emitted = new List<LevelDataDto>();

            foreach (var variation in skeleton.Variations)
            {
                var levelId = $"{skeleton.Family}_{variation.Id}";
                if (existingIds.Contains(levelId))
                {
                    Reject(r, levelId, "duplicate levelId across the campaign (uniqueness invariant, TASK-112)");
                    continue;
                }

                var position = basePosition + emitted.Count;
                var dto = TryBuildVariation(skeleton, variation, levelId, position, defaultModule, out var reason);
                if (dto == null) { Reject(r, levelId, reason); continue; }

                LevelData domain;
                try { domain = dto.ToDomain(); }
                catch (Exception e) { Reject(r, levelId, $"domain conversion failed: {e.Message}"); continue; }

                var vr = validator.Validate(domain);
                if (!vr.Ok) { Reject(r, levelId, "LevelDataValidator: " + string.Join("; ", vr.Errors)); continue; }

                var audit = validator.AuditTapSpacing(domain);
                if (audit.Violations.Count > 0)
                {
                    var pairs = string.Join(", ", audit.Violations.Select(v =>
                        $"{v.KindA}↔{v.KindB} {v.Distance:F3}<{audit.MinTapWorld:F3}"));
                    Reject(r, levelId, $"tap-spacing (§D12): {audit.Violations.Count} pair(s) merge [{pairs}]");
                    continue;
                }

                emitted.Add(dto);
                existingIds.Add(levelId);
                r.EmittedIds.Add(levelId);
                r.Log.Add($"OK {levelId}: {domain.Pieces.Count} pieces, L={position}, " +
                          $"tapZone={audit.MinTapWorld:F3} @ orthoSize {audit.OrthoSize:F2}");
            }

            if (emitted.Count > 0)
                r.AppendedManifest = AppendLevels(existingManifestText, emitted);

            r.Ok = true;
            return r;
        }

        private static void Reject(Result r, string id, string reason)
        {
            r.Rejected.Add((id, reason));
            r.Log.Add($"REJECT {id}: {reason}");
        }

        private static LevelDataDto TryBuildVariation(SkeletonFileDto sk, SkeletonVariationDto v, string levelId,
            int position, BaseModule defaultModule, out string reason)
        {
            reason = null;
            if (!TryResolveVariationModule(v.BaseModule, defaultModule, out var module, out reason))
                return null;
            switch (module)
            {
                case BaseModule.TwinLeg: return TryBuildTwinBase(sk, v, levelId, position, isPostSpan: false, out reason);
                case BaseModule.PostSpan: return TryBuildTwinBase(sk, v, levelId, position, isPostSpan: true, out reason);
                case BaseModule.Grid: return TryBuildGrid(sk, v, levelId, position, out reason);
            }

            // ---- base_module: slab (S1, the original central-flat-stack path — emitted byte-for-byte as before) ----
            var layers = v.Layers;
            if (layers == null || layers.Count < 2)
            {
                reason = "a flat-stack needs at least 2 layers (§D14: a single slot is not a readable silhouette)";
                return null;
            }

            var n = layers.Count;
            var footHW = new float[n];
            var topHW = new float[n];
            var flatTop = new bool[n];
            var height = new float[n];

            for (var i = 0; i < n; i++)
            {
                if (!Geometry(layers[i], out footHW[i], out topHW[i], out flatTop[i], out height[i], out var gErr))
                {
                    reason = $"layer {i} ('{layers[i].Primitive}'): {gErr}";
                    return null;
                }
            }

            for (var i = 0; i < n - 1; i++)
            {
                if (!flatTop[i])
                {
                    reason = $"layer {i} ('{layers[i].Primitive}') has no flat top to support the layer above " +
                             "(§L2/§L3: only the TOP layer may be pointed/curved)";
                    return null;
                }
            }

            for (var i = 1; i < n; i++)
            {
                if (footHW[i] > topHW[i - 1] + TaperEps)
                {
                    reason = $"layer {i} footprint {footHW[i] * 2f:F2} is wider than the flat top {topHW[i - 1] * 2f:F2} " +
                             $"of layer {i - 1} below — overhang / CG past base (§L2)";
                    return null;
                }
            }

            var slotY = new float[n];
            var cum = 0f;
            var maxSlotY = 0f;
            for (var i = 0; i < n; i++)
            {
                slotY[i] = cum + height[i] * 0.5f;
                cum += height[i];
                if (slotY[i] > maxSlotY) maxSlotY = slotY[i];
            }

            var k = n;
            if (!HangOrder.TryGetValue(k, out var order))
            {
                reason = $"{k} tiers exceeds the §L12 hang table (≤8) — extend the canon table before generating " +
                         "deeper stacks (do not invent a scramble order)";
                return null;
            }

            var baseHang = MathF.Max(3.0f, maxSlotY + 0.5f);
            var posLayer = new int[k];
            for (var p = 0; p < k; p++) posLayer[p] = order[p] - 1;

            var halfSum = 0f;
            for (var p = 1; p < k; p++)
                halfSum += (height[posLayer[p - 1]] + height[posLayer[p]]) * 0.5f;
            var gap = DefaultGap;
            var topAtDefault = baseHang + halfSum + (k - 1) * DefaultGap;
            if (topAtDefault > HangCeiling)
            {
                gap = (HangCeiling - baseHang - halfSum) / (k - 1);
                if (gap < MinGap)
                {
                    reason = $"stack too tall to hang under the {HangCeiling:F1} ceiling even at the {MinGap:F2} min gap " +
                             $"(needs top ≈ {topAtDefault:F2}); shorten layer heights (§L12/§D4)";
                    return null;
                }
            }

            var hangYByLayer = new float[n];
            var prev = baseHang;
            for (var p = 0; p < k; p++)
            {
                if (p == 0) hangYByLayer[posLayer[p]] = baseHang;
                else
                {
                    prev += (height[posLayer[p - 1]] + height[posLayer[p]]) * 0.5f + gap;
                    hangYByLayer[posLayer[p]] = prev;
                }
            }

            if (HasMonotoneRunOver2(order))
            {
                reason = $"§L12 table row for k={k} has a monotone run >2 (table copy error)";
                return null;
            }
            var posTol = sk.MatchTolerance?.PosTol ?? 0.30f;
            for (var i = 0; i < n; i++)
            {
                if (MathF.Abs(hangYByLayer[i] - slotY[i]) <= posTol)
                {
                    reason = $"layer {i} hangs {hangYByLayer[i]:F2} within posTol of its slot {slotY[i]:F2} " +
                             "(§L12: pseudo-assembled at spawn)";
                    return null;
                }
            }

            var dto = new LevelDataDto
            {
                LevelId = levelId,
                Seed = sk.SeedBase + position,
                DifficultyTier = v.DifficultyTier ?? sk.DifficultyTier,
                Source = "generated",
                PhysicsMode = string.IsNullOrEmpty(sk.PhysicsMode) ? "assembly" : sk.PhysicsMode,
                MatchTolerance = new MatchToleranceDto
                {
                    PosTol = sk.MatchTolerance?.PosTol ?? 0.30f,
                    AngTol = sk.MatchTolerance?.AngTol ?? 0.43633f,
                    RigidityTol = sk.MatchTolerance?.RigidityTol ?? 0f,
                },
                Bounds = sk.Bounds ?? DefaultBounds(),
                Gravity = sk.Gravity ?? new Vec2Dto { X = 0f, Y = -9.81f },
                PieceTypes = new List<PieceTypeDto>(n),
                Target = new TargetFigureDto(),
                Pieces = new List<PiecePlacementDto>(n),
                StaticGeometry = new List<PolygonDto>(1),
            };

            var mat = sk.Material ?? new MaterialProfileDto { Density = 1f, Friction = 0.6f, Restitution = 0.05f };
            for (var i = 0; i < n; i++)
            {
                var kind = $"{v.Id}_l{i}";
                dto.PieceTypes.Add(new PieceTypeDto
                {
                    Kind = kind,
                    Color = Palette[(i + position) % Palette.Length],
                    SymmetryOrder = layers[i].SymmetryOrder ?? DefaultSymmetry(layers[i].Primitive),
                    Material = mat,
                    Shape = layers[i].ToShapeDto(),
                });
                dto.Target.Slots.Add(new TargetSlotDto
                {
                    Kind = kind, Position = new Vec2Dto { X = 0f, Y = slotY[i] }, Rotation = 0f,
                });
                dto.Pieces.Add(new PiecePlacementDto
                {
                    Kind = kind, StartPosition = new Vec2Dto { X = 0f, Y = hangYByLayer[i] }, StartRotation = 0f,
                });
            }

            dto.Target.Outline = BuildOutline(footHW, topHW, height, flatTop, layers);
            dto.StaticGeometry.Add(BuildPlinth(footHW[0], sk));
            return dto;
        }

        // ---- TASK-198: skeleton/base-module resolution -----------------------------------------------------------

        /// <summary>Map the skeleton kind to its default base module. S1 = slab (byte-for-byte legacy path). The three
        /// off-axis skeletons default to their module, but a variation may still override via <c>baseModule</c>.</summary>
        private static bool TryResolveSkeletonModule(string skeleton, out BaseModule module, out string err)
        {
            err = null; module = BaseModule.Slab;
            switch ((skeleton ?? "").Trim().ToUpperInvariant())
            {
                case "S1": module = BaseModule.Slab; return true;
                case "S1B": module = BaseModule.TwinLeg; return true;
                case "S2": module = BaseModule.PostSpan; return true;
                case "S3": module = BaseModule.Grid; return true;
                default:
                    err = $"skeleton kind '{skeleton}' is not implemented — expected S1 (slab), S1b (twin-leg), " +
                          "S2 (post-span) or S3 (grid).";
                    return false;
            }
        }

        private static bool TryResolveVariationModule(string raw, BaseModule fallback, out BaseModule module, out string err)
        {
            err = null; module = fallback;
            if (string.IsNullOrWhiteSpace(raw)) return true;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "slab": module = BaseModule.Slab; return true;
                case "twin-leg": case "twinleg": module = BaseModule.TwinLeg; return true;
                case "post-span": case "postspan": module = BaseModule.PostSpan; return true;
                case "grid": module = BaseModule.Grid; return true;
                default:
                    err = $"unknown baseModule '{raw}' (expected slab, twin-leg, post-span or grid)";
                    return false;
            }
        }

        // ---- TASK-198: off-axis placement model (shared assembly for every non-slab module) -----------------------

        /// <summary>One emitted piece: a kind (same-kind pieces share it), the resting slot centre, its shape/symmetry,
        /// and its height (for the §L12 hang ladder). hang.x is always slot.x (§D3 direct drop).</summary>
        private sealed class Placement
        {
            public string Kind;
            public ShapeDto Shape;
            public int Symmetry;
            public float SlotX, SlotY;
            public float Height;
            public float RightHalf;   // max right-edge half-width in world-x (for the union outline)
        }

        /// <summary>twin-leg (S1b) and post-span (S2): a symmetric same-kind support pair at ±span/2 plus a central
        /// stack whose bottom layer bridges both supports. Physically identical classes — post-span's semantics only
        /// change the support/deck naming and the messages; the falsifiable ("span dropped before its posts falls") is
        /// emergent from the geometry, not a flag: the span slot sits on the post tops, so a solver that releases it
        /// first has nothing to land on. Envelope (§L1/§L2): supports keep §L1 clearance, the bridge bears on BOTH
        /// support tops without a cantilever past the OverhangBudget, and the stack above tapers monotonically.</summary>
        private static LevelDataDto TryBuildTwinBase(SkeletonFileDto sk, SkeletonVariationDto v, string levelId,
            int position, bool isPostSpan, out string reason)
        {
            reason = null;
            var noun = isPostSpan ? "post" : "leg";
            var deckNoun = isPostSpan ? "span" : "deck";

            var support = v.BaseSupport;
            if (support == null)
            {
                reason = $"{(isPostSpan ? "post-span" : "twin-leg")} needs a baseSupport primitive (the paired {noun}s)";
                return null;
            }
            if (!Geometry(support, out var supFootHW, out var supTopHW, out var supFlatTop, out var supH, out var sErr))
            {
                reason = $"baseSupport ('{support.Primitive}'): {sErr}";
                return null;
            }
            if (!supFlatTop)
            {
                reason = $"baseSupport ('{support.Primitive}') has no flat top to bear the {deckNoun} " +
                         "(§L2/§L3: a support must be box/plank/trapezoid)";
                return null;
            }

            var span = v.SupportSpan;
            if (span <= 0f)
            {
                reason = "supportSpan (centre-to-centre distance of the two supports) must be > 0";
                return null;
            }
            var footGap = span - 2f * supFootHW;
            if (footGap < MinClearance)
            {
                reason = $"the two same-kind {noun}s are {footGap:F2} apart (< {MinClearance:F2} §L1 clearance) — " +
                         "widen supportSpan or narrow the support (same-kind neighbours must not share a face)";
                return null;
            }

            var layers = v.Layers;
            if (layers == null || layers.Count < 1)
            {
                reason = $"{(isPostSpan ? "post-span" : "twin-leg")} needs at least 1 central layer (the {deckNoun} that " +
                         "bridges the supports)";
                return null;
            }

            var n = layers.Count;
            var footHW = new float[n];
            var topHW = new float[n];
            var flatTop = new bool[n];
            var height = new float[n];
            for (var i = 0; i < n; i++)
            {
                if (!Geometry(layers[i], out footHW[i], out topHW[i], out flatTop[i], out height[i], out var gErr))
                {
                    reason = $"central layer {i} ('{layers[i].Primitive}'): {gErr}";
                    return null;
                }
            }
            for (var i = 0; i < n - 1; i++)
            {
                if (!flatTop[i])
                {
                    reason = $"central layer {i} ('{layers[i].Primitive}') has no flat top to support the layer above " +
                             "(§L2/§L3: only the TOP central layer may be pointed/curved)";
                    return null;
                }
            }

            // Bridge (Layers[0]) must bear on BOTH support flat tops (§L2): reach at least each support's inner edge,
            // and not cantilever past the OverhangBudget beyond each support's outer edge.
            var bridgeHW = footHW[0];
            if (bridgeHW < span * 0.5f - supTopHW)
            {
                reason = $"the {deckNoun} half-width {bridgeHW:F2} does not reach both {noun} tops " +
                         $"(need ≥ {span * 0.5f - supTopHW:F2} to bear on each, §L2)";
                return null;
            }
            if (bridgeHW > span * 0.5f + supTopHW + OverhangBudget)
            {
                reason = $"the {deckNoun} overhangs the supports by more than {OverhangBudget:F2} " +
                         $"(half-width {bridgeHW:F2} > {span * 0.5f + supTopHW + OverhangBudget:F2}) — cantilever / CG past base (§L2)";
                return null;
            }
            for (var i = 1; i < n; i++)
            {
                if (footHW[i] > topHW[i - 1] + TaperEps)
                {
                    reason = $"central layer {i} footprint {footHW[i] * 2f:F2} is wider than the flat top " +
                             $"{topHW[i - 1] * 2f:F2} of layer {i - 1} below — overhang / CG past base (§L2)";
                    return null;
                }
            }

            var placements = new List<Placement>(n + 2);
            var baseKind = $"{v.Id}_{noun}";
            var supSym = support.SymmetryOrder ?? DefaultSymmetry(support.Primitive);
            var supRight = span * 0.5f + supFootHW; // outer edge of the support pair (fills the gap in the skyline)
            foreach (var sx in new[] { -span * 0.5f, span * 0.5f })
            {
                placements.Add(new Placement
                {
                    Kind = baseKind, Shape = support.ToShapeDto(), Symmetry = supSym,
                    SlotX = sx, SlotY = supH * 0.5f, Height = supH, RightHalf = supRight,
                });
            }

            var deckY = supH;
            var cum = 0f;
            for (var i = 0; i < n; i++)
            {
                var sy = deckY + cum + height[i] * 0.5f;
                cum += height[i];
                placements.Add(new Placement
                {
                    Kind = $"{v.Id}_l{i}", Shape = layers[i].ToShapeDto(),
                    Symmetry = layers[i].SymmetryOrder ?? DefaultSymmetry(layers[i].Primitive),
                    SlotX = 0f, SlotY = sy, Height = height[i], RightHalf = MathF.Max(footHW[i], topHW[i]),
                });
            }

            var statics = new List<PolygonDto> { BuildPlinth(supRight, sk) };
            return BuildLevelDto(sk, v, levelId, position, placements, statics, out reason);
        }

        /// <summary>grid (S3): rows of uniform same-kind tiles laid out bottom-up with §L1 lateral clearance. A row of
        /// N-1 tiles above a row of N saddles the seams (pyramid); an equal count stacks aligned columns. §D12 tap-zone
        /// (many same-kind pieces) is the real width ceiling — enforced downstream by the runtime tap audit, not here.
        /// Corner cradle walls (as authored toy_pyramid) hold the base row laterally; they sit OUTSIDE every drop
        /// column so they never intersect a piece's fall corridor or rest bbox (§L3).</summary>
        private static LevelDataDto TryBuildGrid(SkeletonFileDto sk, SkeletonVariationDto v, string levelId,
            int position, out string reason)
        {
            reason = null;
            var tile = v.Tile;
            if (tile == null) { reason = "grid needs a tile primitive"; return null; }
            if (!Geometry(tile, out var tileHW, out var tileTopHW, out var tileFlat, out var tileH, out var tErr))
            {
                reason = $"tile ('{tile.Primitive}'): {tErr}"; return null;
            }
            if (!tileFlat || MathF.Abs(tileTopHW - tileHW) > TaperEps)
            {
                reason = $"grid tile ('{tile.Primitive}') must be a flat-top, flat-bottom box/plank of uniform width " +
                         "(a grid stacks squared tiles, §L2)";
                return null;
            }
            var rows = v.Rows;
            if (rows == null || rows.Count < 1) { reason = "grid needs at least one row"; return null; }

            var clearance = v.Clearance ?? MinClearance;
            if (clearance < MinClearance)
            {
                reason = $"grid clearance {clearance:F2} is below the §L1 minimum {MinClearance:F2} (same-kind tiles " +
                         "would share a face)";
                return null;
            }
            var pitch = 2f * tileHW + clearance;

            for (var r = 1; r < rows.Count; r++)
            {
                var below = rows[r - 1].Count;
                var here = rows[r].Count;
                if (here < 1 || (here != below && here != below - 1))
                {
                    reason = $"grid row {r} has {here} tile(s); a row must keep the count below ({below}, aligned) or " +
                             $"drop by one ({below - 1}, saddled pyramid) so every tile rests flat on the row under it (§L2)";
                    return null;
                }
            }

            var placements = new List<Placement>();
            var tileKind = $"{v.Id}_tile";
            var tileSym = tile.SymmetryOrder ?? DefaultSymmetry(tile.Primitive);
            var baseHalf = 0f;
            for (var r = 0; r < rows.Count; r++)
            {
                var count = rows[r].Count;
                var sy = tileH * r + tileH * 0.5f;
                for (var j = 0; j < count; j++)
                {
                    var sx = (j - (count - 1) * 0.5f) * pitch;
                    placements.Add(new Placement
                    {
                        Kind = tileKind, Shape = tile.ToShapeDto(), Symmetry = tileSym,
                        SlotX = sx, SlotY = sy, Height = tileH, RightHalf = MathF.Abs(sx) + tileHW,
                    });
                }
                if (r == 0)
                    baseHalf = (count - 1) * 0.5f * pitch + tileHW;
            }

            var statics = new List<PolygonDto> { BuildPlinth(baseHalf, sk) };
            AddCradleWalls(statics, baseHalf + clearance, tileH * 0.8f, sk);
            return BuildLevelDto(sk, v, levelId, position, placements, statics, out reason);
        }

        /// <summary>Shared tail for every off-axis module: assign the §L8 palette, run the §L12 hang scramble over ALL
        /// pieces (a single virtual ladder ordered bottom-up by slot; hang.x == slot.x so each piece drops straight
        /// into its column), reject a pseudo-assembled or over-tall stack with a named reason, build the union outline,
        /// and return a DTO that the caller runs through the REAL LevelDataValidator + tap audit before emitting.</summary>
        private static LevelDataDto BuildLevelDto(SkeletonFileDto sk, SkeletonVariationDto v, string levelId,
            int position, List<Placement> placements, List<PolygonDto> statics, out string reason)
        {
            reason = null;
            var k = placements.Count;
            if (k < 2)
            {
                reason = "a target figure needs at least 2 pieces (§D14: a single slot is not a readable silhouette)";
                return null;
            }
            if (!HangOrder.TryGetValue(k, out var order))
            {
                reason = $"{k} pieces exceeds the §L12 hang table (2..8) — reduce the piece count before generating " +
                         "(do not invent a scramble order)";
                return null;
            }

            // Canonical bottom-up tier order (by slotY, then slotX) → the §L12 permutation assigns hang heights.
            var tier = new List<int>(k);
            for (var i = 0; i < k; i++) tier.Add(i);
            tier.Sort((a, b) =>
            {
                var c = placements[a].SlotY.CompareTo(placements[b].SlotY);
                return c != 0 ? c : placements[a].SlotX.CompareTo(placements[b].SlotX);
            });

            var maxSlotY = 0f;
            foreach (var p in placements) if (p.SlotY > maxSlotY) maxSlotY = p.SlotY;

            // Hang ladder (identical math to the slab path): a scrambled vertical column above the figure, ceiling 7.0.
            var posPiece = new int[k];               // piece index hanging at hang-position p (bottom-up)
            for (var p = 0; p < k; p++) posPiece[p] = tier[order[p] - 1];

            var halfSum = 0f;
            for (var p = 1; p < k; p++)
                halfSum += (placements[posPiece[p - 1]].Height + placements[posPiece[p]].Height) * 0.5f;
            var baseHang = MathF.Max(3.0f, maxSlotY + 0.5f);
            var gap = DefaultGap;
            var topAtDefault = baseHang + halfSum + (k - 1) * DefaultGap;
            if (topAtDefault > HangCeiling)
            {
                gap = (HangCeiling - baseHang - halfSum) / (k - 1);
                if (gap < MinGap)
                {
                    reason = $"stack too tall to hang under the {HangCeiling:F1} ceiling even at the {MinGap:F2} min gap " +
                             $"(needs top ≈ {topAtDefault:F2}); shorten the pieces (§L12/§D4)";
                    return null;
                }
            }

            if (HasMonotoneRunOver2(order))
            {
                reason = $"§L12 table row for k={k} has a monotone run >2 (table copy error)";
                return null;
            }

            var hangY = new float[k];
            var prev = baseHang;
            for (var p = 0; p < k; p++)
            {
                if (p == 0) hangY[posPiece[p]] = baseHang;
                else
                {
                    prev += (placements[posPiece[p - 1]].Height + placements[posPiece[p]].Height) * 0.5f + gap;
                    hangY[posPiece[p]] = prev;
                }
            }

            var posTol = sk.MatchTolerance?.PosTol ?? 0.30f;
            for (var i = 0; i < k; i++)
            {
                if (MathF.Abs(hangY[i] - placements[i].SlotY) <= posTol)
                {
                    reason = $"piece {i} ('{placements[i].Kind}') hangs {hangY[i]:F2} within posTol of its slot " +
                             $"{placements[i].SlotY:F2} (§L12: pseudo-assembled at spawn)";
                    return null;
                }
            }

            var dto = new LevelDataDto
            {
                LevelId = levelId,
                Seed = sk.SeedBase + position,
                DifficultyTier = v.DifficultyTier ?? sk.DifficultyTier,
                Source = "generated",
                PhysicsMode = string.IsNullOrEmpty(sk.PhysicsMode) ? "assembly" : sk.PhysicsMode,
                MatchTolerance = new MatchToleranceDto
                {
                    PosTol = sk.MatchTolerance?.PosTol ?? 0.30f,
                    AngTol = sk.MatchTolerance?.AngTol ?? 0.43633f,
                    RigidityTol = sk.MatchTolerance?.RigidityTol ?? 0f,
                },
                Bounds = sk.Bounds ?? DefaultBounds(),
                Gravity = sk.Gravity ?? new Vec2Dto { X = 0f, Y = -9.81f },
                PieceTypes = new List<PieceTypeDto>(),
                Target = new TargetFigureDto(),
                Pieces = new List<PiecePlacementDto>(k),
                StaticGeometry = statics ?? new List<PolygonDto>(),
            };

            var mat = sk.Material ?? new MaterialProfileDto { Density = 1f, Friction = 0.6f, Restitution = 0.05f };
            // One PieceType per distinct kind, coloured by first-appearance index (§L8: PALETTE[(typeIndex+L)%10]).
            var typeIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var p in placements)
            {
                if (!typeIndex.ContainsKey(p.Kind))
                {
                    var idx = typeIndex.Count;
                    typeIndex[p.Kind] = idx;
                    dto.PieceTypes.Add(new PieceTypeDto
                    {
                        Kind = p.Kind,
                        Color = Palette[(idx + position) % Palette.Length],
                        SymmetryOrder = p.Symmetry,
                        Material = mat,
                        Shape = p.Shape,
                    });
                }
                dto.Target.Slots.Add(new TargetSlotDto
                {
                    Kind = p.Kind, Position = new Vec2Dto { X = p.SlotX, Y = p.SlotY }, Rotation = 0f,
                });
            }
            for (var i = 0; i < k; i++)
            {
                dto.Pieces.Add(new PiecePlacementDto
                {
                    Kind = placements[i].Kind,
                    StartPosition = new Vec2Dto { X = placements[i].SlotX, Y = hangY[i] },
                    StartRotation = 0f,
                });
            }

            dto.Target.Outline = BuildStepOutline(placements);
            return dto;
        }

        /// <summary>Union silhouette for the off-axis modules: a symmetric orthogonal step profile that ENCLOSES every
        /// piece (max right extent per y-band, mirrored). This fills the gap between legs into a solid silhouette and
        /// steps a pyramid — matching the authored corpus convention (robot's solid body rect, toy_pyramid's stepped
        /// outline). The target outline is cosmetic (the match is pose-vs-slot), so this deliberate step approximation
        /// costs no winnability; the slab path keeps its tapered union.</summary>
        private static List<Vec2Dto> BuildStepOutline(List<Placement> placements)
        {
            var ys = new SortedSet<float>();
            foreach (var p in placements)
            {
                ys.Add(p.SlotY - p.Height * 0.5f);
                ys.Add(p.SlotY + p.Height * 0.5f);
            }
            var yl = new List<float>(ys);

            var bandY0 = new List<float>();
            var bandY1 = new List<float>();
            var bandRx = new List<float>();
            for (var b = 0; b < yl.Count - 1; b++)
            {
                var y0 = yl[b];
                var y1 = yl[b + 1];
                var rx = 0f;
                foreach (var p in placements)
                {
                    var py0 = p.SlotY - p.Height * 0.5f;
                    var py1 = p.SlotY + p.Height * 0.5f;
                    if (py0 <= y0 + TaperEps && py1 >= y1 - TaperEps && p.RightHalf > rx)
                        rx = p.RightHalf;
                }
                if (rx <= 0f) continue;
                // Merge with the previous band if the extent is unchanged (fewer vertices).
                if (bandRx.Count > 0 && MathF.Abs(bandRx[^1] - rx) < 1e-4f && MathF.Abs(bandY1[^1] - y0) < 1e-4f)
                    bandY1[^1] = y1;
                else { bandY0.Add(y0); bandY1.Add(y1); bandRx.Add(rx); }
            }

            var outline = new List<Vec2Dto>();
            for (var b = 0; b < bandRx.Count; b++)   // right side, bottom-up
            {
                AddPoint(outline, bandRx[b], bandY0[b]);
                AddPoint(outline, bandRx[b], bandY1[b]);
            }
            for (var b = bandRx.Count - 1; b >= 0; b--)   // left side, top-down (mirror)
            {
                AddPoint(outline, -bandRx[b], bandY1[b]);
                AddPoint(outline, -bandRx[b], bandY0[b]);
            }
            return outline;
        }

        /// <summary>Two thin corner cradle walls that hold a grid's base row laterally (as authored toy_pyramid). They
        /// stand just OUTSIDE the outer tile edge (+clearance), so no drop column or rest bbox ever intersects them.</summary>
        private static void AddCradleWalls(List<PolygonDto> statics, float innerX, float wallHeight, SkeletonFileDto sk)
        {
            const float thickness = 0.2f;
            var color = string.IsNullOrEmpty(sk.PlinthColor) ? "#B8926B" : sk.PlinthColor;
            foreach (var side in new[] { -1f, 1f })
            {
                var x0 = side > 0 ? innerX : -innerX - thickness;
                var x1 = x0 + thickness;
                statics.Add(new PolygonDto
                {
                    Visible = true,
                    Color = color,
                    Outline = new List<Vec2Dto>
                    {
                        new() { X = x0, Y = 0f },
                        new() { X = x1, Y = 0f },
                        new() { X = x1, Y = wallHeight },
                        new() { X = x0, Y = wallHeight },
                    },
                });
            }
        }

        private static bool Geometry(SkeletonLayerDto L, out float footHalfW, out float topHalfW, out bool flatTop,
            out float h, out string err)
        {
            footHalfW = topHalfW = h = 0f; flatTop = false; err = null;
            switch (L.Primitive)
            {
                case "box":
                case "plank":
                    if (L.Width <= 0f || L.Height <= 0f) { err = "needs positive width/height"; return false; }
                    footHalfW = topHalfW = L.Width * 0.5f; h = L.Height; flatTop = true; return true;
                case "trapezoid":
                    if (L.Width <= 0f || L.TopWidth <= 0f || L.Height <= 0f)
                    { err = "needs positive width/topWidth/height"; return false; }
                    footHalfW = L.Width * 0.5f; topHalfW = L.TopWidth * 0.5f; h = L.Height; flatTop = true; return true;
                case "triangle":
                    if (L.Width <= 0f || L.Height <= 0f) { err = "needs positive width/height"; return false; }
                    footHalfW = L.Width * 0.5f; topHalfW = 0f; h = L.Height; flatTop = false; return true;
                case "gable":
                    if (L.Width <= 0f || L.Height <= 0f || L.ApexHeight <= 0f)
                    { err = "needs positive width/height/apexHeight"; return false; }
                    footHalfW = topHalfW = L.Width * 0.5f; h = L.Height + L.ApexHeight; flatTop = false; return true;
                default:
                    err = $"primitive '{L.Primitive}' cannot be a flat-stack layer " +
                          "(use box/plank/trapezoid; triangle/gable only on top)";
                    return false;
            }
        }

        private static List<Vec2Dto> BuildOutline(float[] footHW, float[] topHW, float[] height, bool[] flatTop,
            List<SkeletonLayerDto> layers)
        {
            var n = footHW.Length;
            var right = new List<Vec2Dto>();
            var yb = 0f;
            for (var i = 0; i < n; i++)
            {
                var yt = yb + height[i];
                AddPoint(right, footHW[i], yb);
                if (flatTop[i]) AddPoint(right, topHW[i], yt);
                else if (layers[i].Primitive == "gable")
                {
                    var eaveY = yb + layers[i].Height;
                    AddPoint(right, footHW[i], eaveY);
                    AddPoint(right, 0f, yt);
                }
                else AddPoint(right, 0f, yt);
                yb = yt;
            }

            var outline = new List<Vec2Dto>(right);
            for (var i = right.Count - 1; i >= 0; i--)
            {
                var p = right[i];
                if (MathF.Abs(p.X) < 1e-4f) continue;
                AddPoint(outline, -p.X, p.Y);
            }
            return outline;
        }

        private static void AddPoint(List<Vec2Dto> list, float x, float y)
        {
            if (list.Count > 0)
            {
                var last = list[^1];
                if (MathF.Abs(last.X - x) < 1e-4f && MathF.Abs(last.Y - y) < 1e-4f) return;
            }
            list.Add(new Vec2Dto { X = x, Y = y });
        }

        private static PolygonDto BuildPlinth(float baseHalfW, SkeletonFileDto sk)
        {
            var margin = sk.PlinthMargin ?? 0.6f;
            var depth = sk.PlinthDepth ?? 0.6f;
            var hw = baseHalfW + margin;
            return new PolygonDto
            {
                Visible = true,
                Color = string.IsNullOrEmpty(sk.PlinthColor) ? "#B8926B" : sk.PlinthColor,
                Outline = new List<Vec2Dto>
                {
                    new() { X = -hw, Y = -depth },
                    new() { X = hw, Y = -depth },
                    new() { X = hw, Y = 0f },
                    new() { X = -hw, Y = 0f },
                },
            };
        }

        private static bool HasMonotoneRunOver2(int[] order)
        {
            var up = 1; var down = 1;
            for (var i = 1; i < order.Length; i++)
            {
                up = order[i] > order[i - 1] ? up + 1 : 1;
                down = order[i] < order[i - 1] ? down + 1 : 1;
                if (up > 2 || down > 2) return true;
            }
            return false;
        }

        /// <summary>Insert emitted level objects before the levels-array close, leaving existing levels BYTE-FOR-BYTE
        /// unchanged (a whole-file re-serialise would reformat all levels and emit default fields — a noisy, risky
        /// diff). The final ']' is the levels-array close: every per-level inner array closes before its level object's
        /// '}', so nothing follows the last level but the array ']' and the root '}'.</summary>
        private static string AppendLevels(string manifestText, List<LevelDataDto> emitted)
        {
            var settings = new JsonSerializerSettings { Formatting = Formatting.Indented };
            var blocks = emitted.Select(dto => Indent(JsonConvert.SerializeObject(dto, settings), "    "));
            var insert = ",\n" + string.Join(",\n", blocks) + "\n  ";

            var close = manifestText.LastIndexOf(']');
            if (close < 0)
                throw new InvalidOperationException("manifest has no levels-array close ']'.");
            var head = manifestText.Substring(0, close).TrimEnd();
            var tail = manifestText.Substring(close);
            return head + insert + tail;
        }

        private static string Indent(string block, string pad)
        {
            var lines = block.Split('\n');
            for (var i = 0; i < lines.Length; i++)
                lines[i] = pad + lines[i];
            return string.Join("\n", lines);
        }

        private static int DefaultSymmetry(string primitive) => primitive switch
        {
            "box" => 4,
            "plank" => 2,
            _ => 1,
        };

        private static RectDto DefaultBounds() => new()
        {
            Min = new Vec2Dto { X = -6f, Y = -6f },
            Max = new Vec2Dto { X = 6f, Y = 10f },
        };
    }
}
