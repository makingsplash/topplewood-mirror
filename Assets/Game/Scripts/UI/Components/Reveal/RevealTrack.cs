using System.Collections.Generic;
using UnityEngine;

namespace Ui.Components.Reveal
{
    /// <summary>
    /// The reveal steps of ONE node (TASK-425). Played by the <see cref="ScreenReveal"/> on the screen root; inert
    /// without it (the compiler's post-build check reports a track with no root animator as an error).
    ///
    /// A node that is inactive when the reveal starts (a branch the controller's Setup switched off) is skipped, and
    /// <see cref="SkipShiftMs"/> pulls every later track earlier — so a hidden reward row leaves no dead pause.
    /// On the screen ROOT the alpha channel fades only the root's own Graphic (the background scrim), never the
    /// subtree; on any other node alpha goes through a CanvasGroup and fades the whole subtree.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RevealTrack : MonoBehaviour
    {
        [SerializeField] private List<RevealStep> _steps = new();
        [SerializeField] private int _skipShiftMs;
        [SerializeField] private bool _unlocksInput;

        public IReadOnlyList<RevealStep> Steps => _steps;
        public int SkipShiftMs => _skipShiftMs;
        public bool UnlocksInput => _unlocksInput;

        /// <summary>Compiler entry point: replaces the whole track (idempotent rebuilds of an existing root).</summary>
        public void Configure(List<RevealStep> steps, int skipShiftMs, bool unlocksInput)
        {
            _steps = steps ?? new List<RevealStep>();
            _skipShiftMs = skipShiftMs;
            _unlocksInput = unlocksInput;
        }

        /// <summary>Earliest start of the track — the anchor its skip-shift applies from.</summary>
        public int FirstAtMs
        {
            get
            {
                var first = int.MaxValue;
                foreach (var step in _steps)
                    first = Mathf.Min(first, step.AtMs);
                return first == int.MaxValue ? 0 : first;
            }
        }

        /// <summary>End of the last non-loop step — when this node has arrived in its prefab state.</summary>
        public int IntroEndMs
        {
            get
            {
                var end = 0;
                foreach (var step in _steps)
                    if (step.Kind != RevealKind.Loop)
                        end = Mathf.Max(end, step.EndMs);
                return end;
            }
        }
    }
}
