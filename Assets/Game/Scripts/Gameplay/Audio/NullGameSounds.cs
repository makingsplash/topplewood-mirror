namespace Gameplay.Audio
{
    /// <summary>Silent <see cref="IGameSounds"/> for the bringup scene and tests (TASK-667).</summary>
    public sealed class NullGameSounds : IGameSounds
    {
        public void Play(string id, float volumeScale = 1f, float semitones = 0f)
        {
        }

        public void Preload()
        {
        }
    }
}
