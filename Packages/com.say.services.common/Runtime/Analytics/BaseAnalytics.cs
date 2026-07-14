using System.Collections.Generic;
using Newtonsoft.Json;
using SGS.Data;

namespace SGS.Services.Analytics
{
    public abstract class BaseAnalytics {
    
        protected int LevelNumber => SayData.Meta.CurrentLevel.Value;
        protected string LevelId => $"Level_{LevelNumber.ToString()}";

        protected Dictionary<string, object> CollectContextData()
        {
            var data = new Dictionary<string, object>()
            {
                //{"field_size", Data.Scene.Grid.GetActiveCellCount()}
            };
            
            return data;
        }
        
        protected string CollectContextDataString()
        {
            Dictionary<string, object> data = CollectContextData();
            return JsonConvert.SerializeObject(data);
        }
    }
}