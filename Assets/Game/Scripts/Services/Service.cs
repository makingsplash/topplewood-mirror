using System;
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
        // TASK-126: громкий контракт. До TASK-126 этот геттер молча отдавал null (LocalServiceContainer
        // никогда не поднимался), из-за чего Service.UI.Show(...) падал NRE где-то позже, а
        // BaseScreen.Close() → Service.UI?.Hide(this) тихо не делал ничего — именно это молчание стоило
        // нам TASK-120. Теперь: после app-бута (LoadingGame.InitializeLocalServices) сервис
        // гарантированно не-null; отсутствие = программерская ошибка (обращение до бута или разрушенный
        // контейнер) и падает ГРОМКО в точке обращения, а не маскируется.
        public static UiService UI => LocalServiceContainer.Get<UiService>()
            ?? throw new InvalidOperationException(
                "UiService недоступен: LocalServiceContainer не поднят (LoadingGame.InitializeLocalServices " +
                "не выполнен) либо контейнер уже разрушен. См. docs/architecture/ui-service-bootstrap.md.");
        public static TimeScale TimeScale => LocalServiceContainer.Get<TimeScale>();
        public static MessagesService Messages => LocalServiceContainer.Get<MessagesService>();
        public static RewardedStateService RewardedState => LocalServiceContainer.Get<RewardedStateService>();
    }
}
