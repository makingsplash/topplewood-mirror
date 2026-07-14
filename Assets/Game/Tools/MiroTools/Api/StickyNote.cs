using System;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace MiroTools
{
    public static partial class ApiClient
    {
        [Serializable]
        public class CreateStickyNoteRequest
        {
            [JsonProperty("data")] public StickyData Data;
            [JsonProperty("position")] public PositionData Position;
            [JsonProperty("parent")] public ParentData Parent;

            [Serializable]
            public class StickyData { [JsonProperty("content")] public string Content; }

            [Serializable]
            public class ParentData { [JsonProperty("id")] public string Id; }
        }
        
        public static async UniTask<string> CreateStickyNote(string token, string boardId, string content, float x, float y, string parentId = null)
        {
            var requestData = new CreateStickyNoteRequest
            {
                Data = new CreateStickyNoteRequest.StickyData { Content = content },
                Position = new PositionData { X = x, Y = y },
                Parent = !string.IsNullOrEmpty(parentId) ? new CreateStickyNoteRequest.ParentData { Id = parentId } : null
            };

            string json = JsonConvert.SerializeObject(requestData, JsonSettings);
            var (success, response) = await SendRequest(token, $"{BaseUrl}/boards/{boardId}/sticky_notes", "POST", json);
                
            if (success)
            {
                var result = JsonConvert.DeserializeObject<MiroItem>(response);
                return result?.Id;
            }
            return null;
        }
    }
}
