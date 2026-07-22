using System;
using R3;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Synthetic input source (RFC §3): a <see cref="Subject{T}"/> of <see cref="InputIntent"/> the bot pushes
    /// world taps into. Mirrors the production seam so <see cref="GameController"/> routes intents identically.
    /// </summary>
    public sealed class SimBotInputSource : IGameplayInputSource, IDisposable
    {
        private readonly Subject<InputIntent> _subject = new();

        public Observable<InputIntent> Intents => _subject;

        public void Push(Vec2 worldPoint) => _subject.OnNext(new InputIntent(worldPoint.X, worldPoint.Y));

        public void Dispose() => _subject.Dispose();
    }
}
