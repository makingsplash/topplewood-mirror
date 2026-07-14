using System;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace MiroTools
{
    public static partial class ApiClient
    {
        private const string BaseUrl = "https://api.miro.com/v2";

        public static bool IsLogEnabled { get; set; } = true;

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        #region Common Models
        public class PositionData { [JsonProperty("x")] public float X; [JsonProperty("y")] public float Y; }

        [Serializable]
        public class GeometryData
        {
            [JsonProperty("width")] public float? Width;
            [JsonProperty("height")] public float? Height;

            public GeometryData WithWidth(float width)
            {
                Width = width;
                return this;
            }

            public GeometryData WithHeight(float height)
            {
                Height = height;
                return this;
            }
        }
        #endregion
        

        private static async UniTask<(bool success, string body)> SendRequest(string token, string url, string method, string jsonBody = null)
        {
            using var request = new UnityWebRequest(url, method);
        
            if (!string.IsNullOrEmpty(jsonBody))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            }
            request.downloadHandler = new DownloadHandlerBuffer();
        
            request.SetRequestHeader("Authorization", $"Bearer {token}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            try
            {
                await request.SendWebRequest();
                return (true, request.downloadHandler.text);
            }
            catch (UnityWebRequestException e)
            {
                if (e.ResponseCode == 429)
                {
                    Debug.LogError("[Miro API] Rate limit exceeded. Please wait before next operation.");
                }
                
                string friendlyError = ErrorHandler.GetFriendlyError(e.UnityWebRequest);
                throw new Exception(friendlyError); 
            }
        }
    }
}
