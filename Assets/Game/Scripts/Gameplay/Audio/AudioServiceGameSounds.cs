using Cysharp.Threading.Tasks;
using Scripts.Audio;
using Services;
using UnityEngine;

namespace Gameplay.Audio
{
    /// <summary>
    /// Prod <see cref="IGameSounds"/> (TASK-667): plays through the SayKit-stack AudioService, which resolves the id to
    /// the Addressables asset <c>SoundEffects/&lt;id&gt;.asset</c>. No service (the container is not up) → a silent
    /// no-op, never an exception in the game loop. Dev builds log every play as <c>[sfx] play …</c> — the device check
    /// of the ticket reads exactly these lines (plan §8 T8).
    /// </summary>
    public sealed class AudioServiceGameSounds : IGameSounds
    {
        private static readonly string[] GameplaySounds =
        {
            Sounds.PieceRelease, Sounds.PieceLand, Sounds.WinLift, Sounds.WinFrame,
            Sounds.WinStar, Sounds.Lose, Sounds.Button, Sounds.Hint,
        };

        public void Play(string id, float volumeScale = 1f, float semitones = 0f)
        {
            var audio = GlobalService.AudioService;
            if (audio == null)
                return;
#if GPIE_DEVELOPMENT
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[sfx] play {0} vol={1:0.00} st={2:0.#} t={3:0.000}", id, volumeScale, semitones, Time.time));
#endif
            audio.Play(id, volumeScale, semitones).Forget();
        }

        public void Preload()
        {
            var audio = GlobalService.AudioService;
            if (audio == null)
                return;
            audio.Preload(GameplaySounds);
        }
    }
}
