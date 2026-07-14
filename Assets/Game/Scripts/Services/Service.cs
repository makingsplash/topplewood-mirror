using Services.Gameplay;
using SGS.Services.Containers;
using SGS.Services.Date_Time;
using SGS.Services.Gameplay;
using SGS.Services.Pools;
using SGS.Services.Ui;

namespace Services
{
    public static class Service
    {
        // shortcuts
        public static Pool Pool => LocalServiceContainer.Get<Pool>();
        public static PauseService Pause => LocalServiceContainer.Get<PauseService>();
        public static UiService UI => LocalServiceContainer.Get<UiService>();
        public static TimeScale TimeScale => LocalServiceContainer.Get<TimeScale>();
        public static MessagesService Messages => LocalServiceContainer.Get<MessagesService>();
        public static RewardedStateService RewardedState => LocalServiceContainer.Get<RewardedStateService>();
    }
}
