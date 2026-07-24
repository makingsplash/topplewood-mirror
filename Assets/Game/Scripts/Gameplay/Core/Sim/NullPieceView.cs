using Gameplay.Core.Adapters;

namespace Gameplay.Core.Sim
{
    /// <summary>Headless no-op <see cref="IPieceView"/> (RFC §3): the sim graph carries no visuals.</summary>
    public sealed class NullPieceView : IPieceView
    {
        public void Sync(Vec2 position, float rotation) { }

        public void SetState(PieceLifecycle state) { }

        public void SetArmed(bool armed) { }

        public void Dispose() { }
    }
}
