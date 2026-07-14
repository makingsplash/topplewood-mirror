using System;
using System.Collections.Generic;
using SGS.Infrastructure.Sequence;

namespace Gameplay.Sequence.Base
{
    public abstract class LevelSequenceElementBase : ILevelSequenceElement
    {
        public event Action Finished;
        
        public void Start(Dictionary<string, object> context)
        {
            StartInternal(context);
            Finished?.Invoke();
        }

        protected abstract void StartInternal(Dictionary<string, object> context);
    }
}
