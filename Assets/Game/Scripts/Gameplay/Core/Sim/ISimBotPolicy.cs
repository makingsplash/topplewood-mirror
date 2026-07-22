namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Seeded bot policy (RFC §4): chooses which pre-placed Idle piece to release next by producing the world
    /// point to tap. The driver calls it ONLY when the field is quiet (no Falling piece), so the protocol is
    /// strictly "release → wait for rest → release next". Returns false when nothing remains to release.
    /// </summary>
    public interface ISimBotPolicy
    {
        bool TryNextRelease(GameModel model, out Vec2 tapPoint);
    }
}
