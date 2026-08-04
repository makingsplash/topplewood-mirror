using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>Structural validation outcome for a level schema.</summary>
    public sealed class ValidationResult
    {
        public bool Ok => Errors.Count == 0;
        public IReadOnlyList<string> Errors { get; }

        /// <summary>TASK-085: non-blocking advisories (e.g. the tap-spacing guard in phase 1). Warnings do NOT
        /// affect <see cref="Ok"/> — a level with warnings still loads and simulates — so callers that only gate on
        /// <see cref="Ok"/> are unchanged; a tool/audit can surface these to the author.</summary>
        public IReadOnlyList<string> Warnings { get; }

        public ValidationResult(IReadOnlyList<string> errors, IReadOnlyList<string> warnings = null)
        {
            Errors = errors ?? new List<string>();
            Warnings = warnings ?? new List<string>();
        }
    }
}
