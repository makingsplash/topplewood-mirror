using System;
using Infrastructure.ApplicationTriggers.Interfaces;

namespace Infrastructure.ApplicationTriggers
{
    public sealed class ApplicationTriggers : IApplicationTriggers
    {
        private readonly IApplicationPauseTrigger _pauseTrigger;
        private readonly IApplicationFocusTrigger _focusTrigger;
        private readonly IApplicationQuitTrigger _quitTrigger;

        public ApplicationTriggers(
            IApplicationPauseTrigger pauseTrigger, 
            IApplicationFocusTrigger focusTrigger,
            IApplicationQuitTrigger quitTrigger)
        {
            _pauseTrigger = pauseTrigger ?? throw new ArgumentNullException(nameof(pauseTrigger));
            _focusTrigger = focusTrigger ?? throw new ArgumentNullException(nameof(focusTrigger));
            _quitTrigger = quitTrigger ?? throw new ArgumentNullException(nameof(quitTrigger));
        }
        
        public event Action<bool> PauseChanged
        {
            add => _pauseTrigger.PauseChanged += value;
            remove => _pauseTrigger.PauseChanged -= value;
        }
        
        public bool HasPause => _pauseTrigger.HasPause;
        
        public event Action<bool> FocusChanged
        {
            add => _focusTrigger.FocusChanged += value;
            remove => _focusTrigger.FocusChanged -= value;
        }
        
        public bool HasFocus => _focusTrigger.HasFocus;
        
        public event Action IsQuitting
        {
            add => _quitTrigger.IsQuitting += value;
            remove => _quitTrigger.IsQuitting -= value;
        }

        public event Func<bool> WantsToQuit
        {
            add => _quitTrigger.WantsToQuit += value;
            remove => _quitTrigger.WantsToQuit -= value;
        }
    }
}