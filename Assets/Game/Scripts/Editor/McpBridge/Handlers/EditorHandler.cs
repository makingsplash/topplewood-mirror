using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Game.Editor.McpBridge.Handlers
{
    // Generic editor state + lifecycle. No game-specific dependencies.
    [InitializeOnLoad]
    public static class EditorHandler
    {
        static EditorHandler()
        {
            McpBridgeServer.RegisterHandler("get_editor_state", GetEditorState);
            McpBridgeServer.RegisterHandler("refresh_assets", RefreshAssets);
            McpBridgeServer.RegisterHandler("enter_play_mode", EnterPlayMode);
            McpBridgeServer.RegisterHandler("exit_play_mode", ExitPlayMode);
        }

        private static object GetEditorState(JObject p)
        {
            var active = SceneManager.GetActiveScene();
            return new
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                willChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                activeScene = active.path,
                activeSceneDirty = active.isDirty,
                selectionCount = Selection.objects.Length,
            };
        }

        private static object RefreshAssets(JObject p)
        {
            AssetDatabase.Refresh();
            return new { refreshed = true };
        }

        private static object EnterPlayMode(JObject p)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return new { entering = false, reason = "already playing or entering" };
            EditorApplication.EnterPlaymode();
            return new { entering = true };
        }

        private static object ExitPlayMode(JObject p)
        {
            if (!EditorApplication.isPlaying)
                return new { exiting = false, reason = "not playing" };
            EditorApplication.ExitPlaymode();
            return new { exiting = true };
        }
    }
}
