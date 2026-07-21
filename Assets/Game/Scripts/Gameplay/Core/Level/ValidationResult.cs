using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>Structural validation outcome for a level schema.</summary>
    public sealed class ValidationResult
    {
        public bool Ok => Errors.Count == 0;
        public IReadOnlyList<string> Errors { get; }

        public ValidationResult(IReadOnlyList<string> errors)
        {
            Errors = errors ?? new List<string>();
        }
    }
}
