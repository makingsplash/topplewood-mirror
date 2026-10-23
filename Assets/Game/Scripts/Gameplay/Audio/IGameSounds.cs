namespace Gameplay.Audio
{
    /// <summary>
    /// The one door from game code to sound (TASK-667 plan §3.3). Bound by the HOSTING scope, like
    /// <c>IGameplayInputSource</c>: prod → <see cref="AudioServiceGameSounds"/>, bringup → <see cref="NullGameSounds"/>
    /// (no LoadingGame there, so no AudioService and no AudioConfig). Ids are the generated <c>Scripts.Audio.Sounds</c>
    /// constants — the names of the SoundEffect assets.
    /// </summary>
    public interface IGameSounds
    {
        /// <param name="volumeScale">Multiplies the asset's own volume (impact strength).</param>
        /// <param name="semitones">Pitch shift for this play only (the win-star ladder).</param>
        void Play(string id, float volumeScale = 1f, float semitones = 0f);

        /// <summary>Load the game's sounds ahead, so the first tap does not wait on an Addressables load.</summary>
        void Preload();
    }
}
