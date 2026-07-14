using Newtonsoft.Json;

namespace SGS.Data
{
    public class AnalyticsMeta
    {
        [JsonObject]
        public class ContextData
        {
            public string LevelGuid;
            public string levelId;
            public int FieldSize;
            public int PlayerLevel;
            public int Energy;
        }
        
        

    }
}