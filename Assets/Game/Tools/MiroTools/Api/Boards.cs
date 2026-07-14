using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace MiroTools
{
    public static partial class ApiClient
    {
        public class BoardResponse { [JsonProperty("id")] public string Id; }
        

        public class BoardsListResponse { [JsonProperty("data")] public List<BoardData> Data; }
        public class BoardData 
        { 
            [JsonProperty("id")] public string Id;
            [JsonProperty("name")] public string Name;
            [JsonProperty("team")] public WorkspaceData Team; 
        }
        
        
        public static async UniTask<List<BoardData>> GetBoards(string token)
        {
            var (success, response) = await SendRequest(token, $"{BaseUrl}/boards?limit=50", "GET");
            if (!success) return new List<BoardData>();

            var boards = JsonConvert.DeserializeObject<BoardsListResponse>(response);
            return boards?.Data ?? new List<BoardData>();
        }
        
        
        public static async UniTask<string> CreateBoard(string token, string name, string teamId = null)
        {
            var body = new 
            { 
                name = name,
                policy = !string.IsNullOrEmpty(teamId) ? new { teamId = teamId } : null 
            };
            
            string json = JsonConvert.SerializeObject(body, JsonSettings);
            var (success, response) = await SendRequest(token, $"{BaseUrl}/boards", "POST", json);
            return success ? JsonConvert.DeserializeObject<BoardResponse>(response).Id : null;
        }
    }
}
