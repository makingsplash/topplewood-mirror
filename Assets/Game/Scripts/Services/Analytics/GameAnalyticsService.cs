using Services.Analytics;
using SGS.Services.Analytics;

namespace Services
{
    public class GameAnalyticsService : AnalyticsService
    {
        private GameplayAnalytics _gameplay;
        private UIAnalytics _ui;
        private LevelAnalytics _level;

        public GameplayAnalytics Gameplay => _gameplay;
        public UIAnalytics UI => _ui;
        public LevelAnalytics Level => _level;

        public override void Init()
        {
            base.Init();

            _gameplay = new();
            _ui = new();
            _level = new();
        }
    }
}
