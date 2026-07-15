using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Game.Editor.McpBridge.Handlers
{
    // Generic AssetDatabase search. No game-specific dependencies.
    [InitializeOnLoad]
    public static class AssetHandler
    {
        static AssetHandler()
        {
            McpBridgeServer.RegisterHandler("find_assets", FindAssets);
        }

        // filter: an AssetDatabase search string, e.g. "t:Prefab", "t:Scene", "t:ScriptableObject name".
        // folders: optional array of folders to scope the search (e.g. ["Assets/Game"]).
        private static object FindAssets(JObject p)
        {
            var filter = p["filter"]?.ToString() ?? "";
            var folders = p["folders"]?.ToObject<string[]>();

            var guids = folders != null && folders.Length > 0
                ? AssetDatabase.FindAssets(filter, folders)
                : AssetDatabase.FindAssets(filter);

            return guids.Take(500).Select(guid =>
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                return new { guid, path, type = type != null ? type.Name : null };
            }).ToArray();
        }
    }
}
