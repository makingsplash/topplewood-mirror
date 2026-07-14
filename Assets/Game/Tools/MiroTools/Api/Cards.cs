using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace MiroTools
{
    public static partial class ApiClient
    {
        [Serializable]
        public class CreateCardRequest
        {
            [JsonProperty("data")] public CardData Data;
            [JsonProperty("position")] public PositionData Position;
        }


        [Serializable]
        public class CardData
        {
            [JsonProperty("title")] public string Title;
            [JsonProperty("description")] public string Description;
        }
        
        
        public static async UniTask<bool> CreateCard(string token, string boardId, string title, string description, float x = 0, float y = 0)
        {
            var requestData = new CreateCardRequest 
            { 
                Data = new CardData { Title = title, Description = description },
                Position = new PositionData { X = x, Y = y }
            };
            string json = JsonConvert.SerializeObject(requestData, JsonSettings);
            var (success, _) = await SendRequest(token, $"{BaseUrl}/boards/{boardId}/cards", "POST", json);
            return success;
        }
    }
}
