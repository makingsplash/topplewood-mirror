using System;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;

namespace SGS.Data
{
    public class SayMeta
    {
        // 1-based level counter, increments on every level win
        public readonly PlayerPrefsInt CurrentLevel = new PlayerPrefsInt("Meta.CurrentLevel", 1);
        // 0-based level index in levels config, calculated from CurrentLevel on level start
        public readonly PlayerPrefsInt CurrentLevelIndex = new ("Meta.CurrentLevelIndex", 0);
        public readonly PlayerPrefsListString CompletedTutorials = new ("Meta.CompetedTutorials");
        public readonly AnalyticsMeta Analytics = new ();
        
        public DateTime LastInterstitial = DateTime.MinValue;
    }
}