using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Builds the exact release sequence a REST-GATED prover policy (<see cref="SlotAscendingPolicy"/>,
    /// <see cref="PairReleasePolicy"/>) would choose over a level, expressed as a fixed-step
    /// <see cref="ScriptedStep"/> script so the timing pass (TASK-060/TASK-067) can replay that WINNING
    /// sequence at a human tempo — the same order, but a fixed <c>gapSteps</c> between releases instead of
    /// waiting for global rest. This closes the timing-pass gap (TASK-067): a level solved by a POLICY, not
    /// by an enumerated single order, was never measured fast — the enumerated impatient loop only re-runs
    /// <see cref="SolveSweepPlan"/>'s orders, which for a 7-piece figure (seeded sample) or a seesaw are not
    /// the policy's sequence.
    ///
    /// The builders are STATIC MIRRORS of the two policies and MUST stay in sync with them:
    ///  • <see cref="SlotAscending"/> ≡ <see cref="SlotAscendingPolicy"/>: each release picks the Idle piece
    ///    whose lowest same-Kind target slot sits lowest (tie-broken by placement index) and releases it
    ///    alone. A released piece leaves the Idle set and slot-Y / index are static, so the full order is
    ///    just the tappable pieces ordered by (lowestSlotY, placementIndex) — provably identical to calling
    ///    the live policy to exhaustion.
    ///  • <see cref="PairRelease"/> ≡ <see cref="PairReleasePolicy"/>: each release picks the Kind whose
    ///    lowest slot sits lowest (tie-broken by ordinal Kind name) and releases EVERY Idle piece of that
    ///    Kind as ONE synchronous group. Each Kind appears once, so the order is the Kinds ordered by
    ///    (lowestSlotY, ordinal name), each carrying all its tappable pieces — again identical to the live
    ///    policy run to exhaustion. Both mirrors use the SAME exact float equality on slot Y as the policies
    ///    (<c>==</c> / <c>CompareTo</c> over values loaded byte-identically from the manifest), so the tie
    ///    resolution matches the live policy exactly.
    ///
    /// Only NON-preplaced pieces are considered: a Preplaced piece spawns Falling at t=0 (never Idle), so no
    /// policy ever taps it — mirrored here by excluding it, exactly as <c>LevelSimRunner.TappableIndices</c>.
    ///
    /// CO-RELEASE NOTE for <see cref="PairRelease"/> (balance §L9, TASK-374): a declared co-release pair (2 pieces)
    /// is emitted in ONE <see cref="ScriptedStep"/> so both taps enter the same physics step and the mechanic
    /// releases the whole group synchronously — the pair is NEVER split. The old TASK-023 coalescence caveat no
    /// longer applies: a SINGLE-piece group (base beam/fulcrum) is NOT in any co-release group, so it releases
    /// immediately and opens NO window; and an incomplete co-release group goes SILENT when its window expires
    /// (it never flushes a lone piece). So there is no cross-group coalescence to report regardless of the caller's
    /// gap — the mechanic either releases a full declared group in one step or nothing.
    /// </summary>
    public static class PolicyScriptPlan
    {
        /// <summary>Scripted <see cref="SlotAscendingPolicy"/> order (single piece per release), first release
        /// on step 0 and each subsequent one <paramref name="gapSteps"/> fixed steps later.</summary>
        public static ScriptedStep[] SlotAscending(LevelData level, int gapSteps)
        {
            var order = TappableSlotAscending(level);
            var script = new ScriptedStep[order.Count];
            for (var i = 0; i < order.Count; i++)
                script[i] = new ScriptedStep(new[] { order[i] }, i == 0 ? 0 : gapSteps);
            return script;
        }

        /// <summary>Scripted <see cref="PairReleasePolicy"/> order: each per-Kind group tapped synchronously
        /// (multi-index step), the first group on step 0 and each subsequent group <paramref name="gapSteps"/>
        /// fixed steps later. Keeps a pair synchronous; see the coalescence caveat in the type doc.</summary>
        public static ScriptedStep[] PairRelease(LevelData level, int gapSteps)
        {
            var groups = TappableKindGroups(level);
            var script = new ScriptedStep[groups.Count];
            for (var i = 0; i < groups.Count; i++)
                script[i] = new ScriptedStep(groups[i].ToArray(), i == 0 ? 0 : gapSteps);
            return script;
        }

        // Placement indices of the non-preplaced pieces, ordered by (lowest same-Kind slot Y, placement index)
        // — the order SlotAscendingPolicy releases them in.
        private static List<int> TappableSlotAscending(LevelData level)
        {
            var indices = new List<int>();
            for (var i = 0; i < level.Pieces.Count; i++)
                if (!level.Pieces[i].Preplaced)
                    indices.Add(i);

            indices.Sort((a, b) =>
            {
                var ya = LowestSlotY(level.Target, level.Pieces[a].Type.Kind);
                var yb = LowestSlotY(level.Target, level.Pieces[b].Type.Kind);
                return ya != yb ? ya.CompareTo(yb) : a.CompareTo(b);
            });
            return indices;
        }

        // Per-Kind synchronous groups over the non-preplaced pieces (placement order preserved within a Kind,
        // matching PairReleasePolicy's model iteration — irrelevant to the synchronous flush but tidy), the
        // Kinds ordered by (lowest slot Y, ordinal name) — the order PairReleasePolicy releases them in.
        private static List<List<int>> TappableKindGroups(LevelData level)
        {
            var groupByKind = new Dictionary<string, List<int>>();
            var kindOrder = new List<string>();
            for (var i = 0; i < level.Pieces.Count; i++)
            {
                if (level.Pieces[i].Preplaced)
                    continue;
                var kind = level.Pieces[i].Type.Kind;
                if (!groupByKind.TryGetValue(kind, out var list))
                {
                    groupByKind[kind] = list = new List<int>();
                    kindOrder.Add(kind);
                }
                list.Add(i);
            }

            kindOrder.Sort((a, b) =>
            {
                var ya = LowestSlotY(level.Target, a);
                var yb = LowestSlotY(level.Target, b);
                return ya != yb ? ya.CompareTo(yb) : string.CompareOrdinal(a, b);
            });

            var groups = new List<List<int>>(kindOrder.Count);
            foreach (var kind in kindOrder)
                groups.Add(groupByKind[kind]);
            return groups;
        }

        private static float LowestSlotY(TargetFigure target, string kind)
        {
            var min = float.MaxValue;
            foreach (var slot in target.Slots)
                if (slot.Kind == kind && slot.Position.Y < min)
                    min = slot.Position.Y;
            return min;
        }
    }
}
