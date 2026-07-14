using System;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MiroTools
{
    public static partial class ApiClient
    {
        public static async UniTask<string> UploadImage(string token, string boardId, byte[] imageBytes, string fileName, float x, float y, float width, float height, string parentId = null)
        {
            string url = $"{BaseUrl}/boards/{boardId}/images";
            
            WWWForm form = new WWWForm();
            form.AddBinaryData("resource", imageBytes, fileName, "image/png");
            
            // Динамически формируем геометрию, чтобы Miro не ругался на fixed aspect ratio
            // и при этом мы могли контролировать размер
            object geometry = null;
            if (width > 0 && height > 0) geometry = new { width = width, height = height };
            else if (width > 0) geometry = new { width = width };
            else if (height > 0) geometry = new { height = height };

            var dataPayload = new
            {
                position = (parentId != null) ? new { x = x, y = y } : null,
                geometry = geometry,
                parent = !string.IsNullOrEmpty(parentId) ? new { id = parentId } : null
            };
            form.AddField("data", JsonConvert.SerializeObject(dataPayload, JsonSettings));

            using var request = UnityWebRequest.Post(url, form);
            request.SetRequestHeader("Authorization", $"Bearer {token}");
            request.SetRequestHeader("Accept", "application/json");

            try
            {
                await request.SendWebRequest();
                var result = JsonConvert.DeserializeObject<MiroItem>(request.downloadHandler.text);
                return result?.Id;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Miro API] Image upload failed: {e.Message}");
                return null;
            }
        }
    }
}
