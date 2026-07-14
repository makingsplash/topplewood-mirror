using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public abstract class TimerAnimationProfile : MonoBehaviour
    {
        [SerializeField] private float _maxSecondsToBegan = 10;

        public bool IsAvailable(double currentSeconds)
        {
            return currentSeconds <= _maxSecondsToBegan;
        }

        public abstract UniTask AnimateAsync(TimeSpan currentTime, Action updateAction);
        public abstract UniTask AnimateAsync(Action updateAction);

        private void OnDestroy()
        {
            Clear();
        }

        protected abstract void Clear();
    }
}
