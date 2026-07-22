using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Domain-side determinism hash (RFC §5.3): FNV-1a over the quantised poses of all pieces, ordered by
    /// <see cref="PieceInstance.InstanceId"/>. Independent of the physics adapter's dictionary iteration order
    /// (unlike <c>IGameplaySimulation.Hash</c>), so identical settled layouts hash identically across runs.
    /// </summary>
    public static class SimPoseHash
    {
        private const long FnvOffset = 1469598103934665603L;
        private const long FnvPrime = 1099511628211L;

        public static long Of(GameModel model)
        {
            var pieces = new List<PieceInstance>(model.Pieces);
            pieces.Sort(static (a, b) => a.InstanceId.CompareTo(b.InstanceId));

            var hash = FnvOffset;
            foreach (var piece in pieces)
            {
                hash = Mix(hash, piece.InstanceId);
                hash = Mix(hash, Quantise(piece.Position.X));
                hash = Mix(hash, Quantise(piece.Position.Y));
                hash = Mix(hash, Quantise(piece.Rotation));
                hash = Mix(hash, (int)piece.State);
            }
            return hash;
        }

        private static long Mix(long hash, long value)
        {
            unchecked
            {
                return (hash ^ value) * FnvPrime;
            }
        }

        private static long Quantise(float v) => (long)(v * 1000f);
    }
}
