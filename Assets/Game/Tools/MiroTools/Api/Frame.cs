using System;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace MiroTools
{
    public static partial class ApiClient
    {
        [Serializable]
        public class FrameData
        {
            [JsonProperty("title")] public string Title;
        }
        
        [Serializable]
        public class FrameStyle
        {
            [JsonProperty("fillColor")] public string FillColor;
        }
        
        [Serializable]
        public class CreateFrameRequest
        {
            [JsonProperty("data")] public FrameData Data;
            [JsonProperty("style")] public FrameStyle Style;
            [JsonProperty("position")] public PositionData Position;
            [JsonProperty("geometry")] public GeometryData Geometry;
            
        }
        
        public static async UniTask<string> CreateFrame(string token, string boardId, string title, float x, float y, float width, float height, string fillColor = "#ffffffff")
        {
            var requestData = new CreateFrameRequest
            {
                Data = new FrameData { Title = title },
                Style = !string.IsNullOrEmpty(fillColor) ? new FrameStyle { FillColor = fillColor } : null,
                Position = new PositionData { X = x, Y = y },
                Geometry = new GeometryData { Width = width, Height = height }
            };

            string json = JsonConvert.SerializeObject(requestData, JsonSettings);
            var (success, response) = await SendRequest(token, $"{BaseUrl}/boards/{boardId}/frames", "POST", json);
                
            if (success)
            {
                var result = JsonConvert.DeserializeObject<MiroItem>(response);
                return result?.Id;
            }
            return null;
        }
    }
}
