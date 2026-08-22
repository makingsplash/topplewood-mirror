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
    /// Expands an S1 central-flat-stack skeleton into campaign level records, rejecting any variation outside the
    /// proven-safe assembly envelope (§L1/§L2/§L12) with a NAMED reason (owner "Жёсткие требования": a variation that
    /// fails the envelope is not emitted). Deterministic: slots stack from the layer heights, hang heights follow the
    /// §L12 anti-spoiler table, colours follow the §L8 palette formula, and the target outline is the union silhouette
    /// of the stack. Only S1 is implemented here (owner decision #3 scopes this task to the cheap assembly families;
    /// S1b/S2/S3 are follow-ups). Pure — no I/O, no engine references.
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
            if (!string.Equals(skeleton.Skeleton, "S1", StringComparison.OrdinalIgnoreCase))
            {
                r.Error = $"skeleton kind '{skeleton.Skeleton}' is not implemented — only 'S1' (central flat-stack).";
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
                var dto = TryBuildVariation(skeleton, variation, levelId, position, out var reason);
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
            int position, out string reason)
        {
            reason = null;
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
