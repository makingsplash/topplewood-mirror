using System;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Game.Editor.McpBridge.Handlers
{
    // Invokes an editor menu item by its full path. Generic — no game-specific dependencies.
    //
    // WHY THIS EXISTS (TASK-309). Roles kept building editor-only probes (`Tools/.../Force Highlight`)
    // to trigger a code path the bridge cannot reach, because the bridge injects no input. The probe
    // was then only runnable BY HAND, so every "does it actually appear on screen?" question cost the
    // owner a live session — three of them on TASK-309 alone. With enter_play_mode + this +
    // capture_screenshot, an agent closes that loop itself: trigger, capture, look.
    //
    // ExecuteMenuItem is synchronous but the effect usually lands on a later frame (the menu action
    // queues work), so callers should let a frame or two pass before capturing.
    [InitializeOnLoad]
    public static class MenuItemHandler
    {
        static MenuItemHandler()
        {
            McpBridgeServer.RegisterHandler("execute_menu_item", ExecuteMenuItem);
        }

        // params: path (full menu path, e.g. "Tools/Hint Booster/Force Highlight (Play Mode)")
        private static object ExecuteMenuItem(JObject p)
        {
            var path = p["path"]?.ToString();
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("execute_menu_item needs a non-empty 'path'.");

            // Unity returns false when the item is missing OR its [MenuItem] validator says it is
            // disabled right now (e.g. a play-mode-only probe invoked in edit mode). It does not
            // distinguish the two, so report the raw result and let the caller decide — inventing a
            // reason here would be a guess.
            var executed = EditorApplication.ExecuteMenuItem(path);
            return new
            {
                path,
                executed,
                isPlaying = EditorApplication.isPlaying,
            };
        }
    }
}
