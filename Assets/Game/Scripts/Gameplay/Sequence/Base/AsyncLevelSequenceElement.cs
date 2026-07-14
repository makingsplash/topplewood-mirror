using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SGS.Infrastructure.Sequence;

namespace Gameplay.Sequence.Base
{
    public abstract class AsyncLevelSequenceElement : ILevelSequenceElement
    {
        public event Action Finished;
        
        public void Start(Dictionary<string, object> context) => 
            StartInternalAsync(context).Forget();

        protected abstract UniTask StartAsync(Dictionary<string, object> context);

        private async UniTask StartInternalAsync(Dictionary<string, object> context)
        {
            await StartAsync(context);
            Finished?.Invoke();
        }
    }
}
