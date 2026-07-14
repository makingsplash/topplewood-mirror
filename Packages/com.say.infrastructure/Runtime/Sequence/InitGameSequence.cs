using System;

using SGS.Infrastructure.Core;

namespace SGS.Infrastructure.Sequence
{
    public abstract class InitGameSequence : GameSequence
    {
        public event Action<IGameplayController> GameplayReady;

        protected override void Finish()
        {
            GameplayReady?.Invoke((IGameplayController)Context[GameSequenceTags.GameplayController]);
            
            base.Finish();
        }
    }
}