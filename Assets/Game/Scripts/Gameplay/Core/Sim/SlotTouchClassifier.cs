using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>How two authored target slots sit relative to each other in the STATIC slot pose (TASK-075
    /// reference-rest baseline). Drives which baseline the honesty detector measures overshoot from.</summary>
    public enum SlotTouchClass
    {
        /// <summary>Authored clear gap (<c>sep &gt; proximity_eps</c>): the pieces were separated on purpose, any
        /// contact between them is dishonest → baseline 0 (strictness untouched).</summary>
        Disjoint,

        /// <summary>Authored contact (<c>|sep| ≤ proximity_eps</c>, ≥1 circle): the pieces touch by design, so their
        /// physical resting-slop is honest → baseline = the cross-run reference-rest floor (harness-computed).</summary>
        Touching,

        /// <summary>Authored nested overlap (<c>sep &lt; −proximity_eps</c>): a designed interpenetration (owl's ear
        /// in the head) → baseline = the static slot-pose overlap, as before.</summary>
        Overlapping,
    }

    /// <summary>One classified slot pair — its indices, the SIGNED surface gap, its class, and whether it is
    /// circle-containing (only circle-containing pairs are reference-rest-eligible; poly-poly is deferred). Emitted
    /// per level for the corpus-validation print the owner requires (every pair's class + gap, eyes-on).</summary>
    public readonly struct SlotPairTouch
    {
        public readonly int SlotA;
        public readonly int SlotB;
        public readonly float Separation;      // float.MaxValue ⇒ poly-poly apart (exact distance deferred)
        public readonly SlotTouchClass Class;
        public readonly bool CircleContaining;

        public SlotPairTouch(int slotA, int slotB, float separation, SlotTouchClass cls, bool circleContaining)
        {
            SlotA = slotA;
            SlotB = slotB;
            Separation = separation;
            Class = cls;
            CircleContaining = circleContaining;
        }
    }

    /// <summary>
    /// Classifies each authored slot pair as DISJOINT / TOUCHING / OVERLAPPING by the SIGNED surface gap in the
    /// static slot pose (TASK-075 §3). The single source of the touching predicate — the detector (SimRun) fills
    /// its per-run touching matrix from it, and the harness (LevelSimRunner) prints the full classification from it,
    /// so the gate path and the eyes-on diagnostic can never diverge. Unity-free.
    /// </summary>
    public static class SlotTouchClassifier
    {
        /// <summary>"Touching" band half-width. = Box2D <c>linearSlop</c> = the sim-gate <c>PEN_EPS</c>: two
        /// surfaces coincide in the slot pose to within one physical resting-slop. NOT a free fudge — the same
        /// physical constant that the floor it selects is measured against. Mandate: may move DOWN (stricter) or into
        /// a proven empty band only; widening for green is forbidden (owner directive, corpus-validated bimodal gap).</summary>
        public const float ProximityEps = 0.005f;

        /// <summary>Classify one pair by its signed <see cref="Geometry.Separation"/>. TOUCHING is reference-rest-
        /// eligible, so it is gated on ≥1 circle — poly-poly is deferred (TASK-075 §12), and a poly-poly pair whose
        /// sep would land in the band is left DISJOINT (it keeps the static baseline path, never the floor).</summary>
        public static SlotTouchClass Classify(in ConvexShape a, in ConvexShape b, out float separation)
        {
            separation = Geometry.Separation(a, b);
            if (separation < -ProximityEps)
                return SlotTouchClass.Overlapping;
            if ((a.IsCircle || b.IsCircle) && separation <= ProximityEps)
                return SlotTouchClass.Touching;
            return SlotTouchClass.Disjoint;
        }

        /// <summary>Classify every slot pair of a level. <paramref name="shapeByKind"/> maps each slot Kind to its
        /// authored <see cref="Shape"/>; each slot's collider is <see cref="ConvexShape.Of"/> (the SAME dispatch the
        /// detector uses). Pairs whose Kind has no shape are skipped. Returns i&lt;j pairs in slot-index order.</summary>
        public static IReadOnlyList<SlotPairTouch> ClassifyLevel(
            IReadOnlyList<TargetSlot> slots, IReadOnlyDictionary<string, Shape> shapeByKind)
        {
            var result = new List<SlotPairTouch>();
            if (slots == null || shapeByKind == null)
                return result;

            var n = slots.Count;
            var shape = new ConvexShape[n];
            var has = new bool[n];
            for (var i = 0; i < n; i++)
                if (shapeByKind.TryGetValue(slots[i].Kind, out var s))
                {
                    shape[i] = ConvexShape.Of(s, slots[i].Position, slots[i].Rotation);
                    has[i] = true;
                }

            for (var i = 0; i < n; i++)
            {
                if (!has[i])
                    continue;
                for (var j = i + 1; j < n; j++)
                {
                    if (!has[j])
                        continue;
                    var cls = Classify(shape[i], shape[j], out var sep);
                    result.Add(new SlotPairTouch(i, j, sep, cls, shape[i].IsCircle || shape[j].IsCircle));
                }
            }
            return result;
        }
    }
}
