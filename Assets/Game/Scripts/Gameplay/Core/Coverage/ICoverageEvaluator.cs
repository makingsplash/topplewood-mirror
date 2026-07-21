using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>Computes coverage = area(silhouette ∩ settled blocks) / area(silhouette), in [0,1].</summary>
    public interface ICoverageEvaluator
    {
        float Evaluate(ISilhouetteRegion silhouette, IReadOnlyList<PlacedShape> settled, int sampleN);
    }
}
