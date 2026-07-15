using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Game.Editor.McpBridge.Handlers
{
    // Generic scene inspection + open/save. No game-specific dependencies.
    [InitializeOnLoad]
    public static class SceneHandler
    {
        static SceneHandler()
        {
            McpBridgeServer.RegisterHandler("get_scenes", GetScenes);
            McpBridgeServer.RegisterHandler("open_scene", OpenScene);
            McpBridgeServer.RegisterHandler("save_scene", SaveScene);
        }

        private static object GetScenes(JObject p)
        {
            var open = new List<object>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                open.Add(new { path = s.path, name = s.name, isLoaded = s.isLoaded, isDirty = s.isDirty });
            }

            var build = EditorBuildSettings.scenes
                .Select(s => new { path = s.path, enabled = s.enabled })
                .ToArray();

            return new
            {
                active = SceneManager.GetActiveScene().path,
                open,
                build,
            };
        }

        private static object OpenScene(JObject p)
        {
            var path = p["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Missing 'path'");

            var additive = p["additive"]?.ToObject<bool>() ?? false;
            var scene = EditorSceneManager.OpenScene(
                path, additive ? OpenSceneMode.Additive : OpenSceneMode.Single);
            return new { opened = scene.path, name = scene.name };
        }

        private static object SaveScene(JObject p)
        {
            var saved = EditorSceneManager.SaveOpenScenes();
            return new { saved };
        }
    }
}
