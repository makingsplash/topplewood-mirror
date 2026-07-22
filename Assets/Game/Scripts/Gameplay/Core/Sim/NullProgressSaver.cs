namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Headless <see cref="IProgressSaver"/> (RFC §3): never persists and never restores, so each sim run
    /// starts from the freshly loaded level, immune to any stray real save on disk.
    /// </summary>
    public sealed class NullProgressSaver : IProgressSaver
    {
        public void Save() { }

        public bool TryRestore() => false;
    }
}
