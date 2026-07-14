using SGS.Data;
using SGS.Services.Analytics;
using SGS.Services.Audio;
using SGS.Services.Containers;
using SGS.Services.Date_Time;
using SGS.Services.DebugLog;
using SGS.Services.DebugTools;
using SGS.Services.Gameplay;
using SGS.Services.Haptic;
using SGS.Services.Localization;
using SGS.Services.Pools;
using SGS.Services.SayKit;
using SGS.Services.Ui;

namespace SGS.Services
{
    public static class Service
    {
        // shortcuts
        public static bool IsReady => LocalServiceContainer.IsReady();
        public static Pool Pool => LocalServiceContainer.Get<Pool>();
        public static PlaneRaycastService PlaneRaycast => LocalServiceContainer.Get<PlaneRaycastService>();
        public static PauseService Pause => LocalServiceContainer.Get<PauseService>();
        public static UiService UI => LocalServiceContainer.Get<UiService>();
        public static TimeScale TimeScale => LocalServiceContainer.Get<TimeScale>();

    }
    
    public static class GlobalService
    {
        public static void Create()
        {
            GlobalServiceContainer.Register(new DebugLogService(new DebugLogLogic(DebugLogType.FromIntMask(SayData.Debug.DebugLogType.Value))));
                ISayKitService sayKitService =
#if SAYKIT
                    new SayKitService();
#else
                new EmptySayKitService();
#endif
            GlobalServiceContainer.Register<ISayKitService>(sayKitService);
            GlobalServiceContainer.Register(new AnalyticsService());
            GlobalServiceContainer.Register(new TimerService());
            GlobalServiceContainer.Register(new GameTimerService());
            GlobalServiceContainer.Register(AudioService.Create());
            GlobalServiceContainer.Register(new HapticService());
            GlobalServiceContainer.Register(new DateService());
            GlobalServiceContainer.Register(new LocalizationService());
            
#if GPIE_DEVELOPMENT            
            GlobalServiceContainer.Register(new DebugService());
#endif
        }
        
        // shortcuts
        public static AnalyticsService Analytics => GlobalServiceContainer.Get<AnalyticsService>();
        public static DebugService Debug => GlobalServiceContainer.Get<DebugService>();
        public static ISayKitService SayKit => GlobalServiceContainer.Get<ISayKitService>();
        public static DebugLogService DebugLog => GlobalServiceContainer.Get<DebugLogService>();
        public static TimerService Timer => GlobalServiceContainer.Get<TimerService>();
        public static GameTimerService GameTimer => GlobalServiceContainer.Get<GameTimerService>();
        public static AudioService AudioService => GlobalServiceContainer.Get<AudioService>();
        public static HapticService HapticService => GlobalServiceContainer.Get<HapticService>();
        public static DateService Date => GlobalServiceContainer.Get<DateService>();
        public static LocalizationService Localization => GlobalServiceContainer.Get<LocalizationService>();

    }
}
