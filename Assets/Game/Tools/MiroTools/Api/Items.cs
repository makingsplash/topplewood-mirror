using System.Collections.Generic;
using Newtonsoft.Json;

namespace MiroTools
{
    public static partial class ApiClient
    {
        public class ItemData { [JsonProperty("content")] public string Content; }
        
        public class MiroItemsResponse { [JsonProperty("data")] public List<MiroItem> Data; }
        public class MiroItem { [JsonProperty("id")] public string Id; [JsonProperty("type")] public string Type; [JsonProperty("data")] public ItemData Data; }
    }
}
