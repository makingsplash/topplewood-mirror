using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace MiroTools
{
    public static partial class ApiClient
    {
        [Serializable]
        public class WorkspaceData
        {
            [JsonProperty("id")] public string Id;
            [JsonProperty("name")] public string Name;

            // Для корректной работы Distinct в LINQ
            public override bool Equals(object obj) => obj is WorkspaceData data && Id == data.Id;
            public override int GetHashCode() => Id.GetHashCode();
        }
        
        
        public static async UniTask<List<WorkspaceData>> GetWorkspaces(string token)
        {
            // Так как /v2/teams требует Enterprise, мы получаем спейсы через список досок
            var (success, response) = await SendRequest(token, $"{BaseUrl}/boards?limit=10", "GET");
            if (!success) return new List<WorkspaceData>();

            var boards = JsonConvert.DeserializeObject<BoardsListResponse>(response);
            if (boards?.Data == null) return new List<WorkspaceData>();

            // Собираем уникальные команды из списка досок
            return boards.Data
                .Where(b => b.Team != null)
                .Select(b => b.Team)
                .Distinct()
                .ToList();
        }
    }
}
