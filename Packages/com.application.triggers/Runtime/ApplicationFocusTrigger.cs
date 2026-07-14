using System;
using Infrastructure.ApplicationTriggers.Interfaces;
using UnityEngine;
#if VCONTAINER
using VContainer.Unity;
#endif

namespace Infrastructure.ApplicationTriggers
{
    public sealed class ApplicationFocusTrigger :
        MonoBehaviour,
#if VCONTAINER
        IInitializable,
#endif
        IApplicationFocusTrigger
    {
        public event Action<bool> FocusChanged;

        public bool HasFocus { get; private set; }

#if VCONTAINER
        public void Initialize() => HasFocus = Application.isFocused;
#else
        private void Awake() => HasFocus = Application.isFocused;
#endif

        private void OnApplicationFocus(bool hasFocus)
        {
            HasFocus = hasFocus;
            FocusChanged?.Invoke(hasFocus);
        }
    }
}