using UnityEditor;
using UnityEngine;

namespace Game.Editor.McpBridge
{
    public class McpBridgeWindow : EditorWindow
    {
        [MenuItem("Tools/MCP Bridge")]
        public static void ShowWindow()
        {
            GetWindow<McpBridgeWindow>("MCP Bridge");
        }

        private void OnGUI()
        {
            GUILayout.Label("MCP Bridge Status", EditorStyles.boldLabel);
            GUILayout.Space(10);

            var enabled = McpBridgeConfig.Enabled;
            var newEnabled = EditorGUILayout.Toggle("Enabled", enabled);
            if (newEnabled != enabled)
            {
                McpBridgeConfig.Enabled = newEnabled;
                if (newEnabled) McpBridgeServer.Start();
                else McpBridgeServer.Stop();
            }

            var port = McpBridgeConfig.Port;
            var newPort = EditorGUILayout.IntField("Port", port);
            if (newPort != port) McpBridgeConfig.Port = newPort;

            GUILayout.Space(10);
            GUILayout.Label($"Status: {(enabled ? "Running on port " + port : "Stopped")}");

            GUILayout.Space(10);
            if (GUILayout.Button("Restart Server"))
            {
                McpBridgeServer.Stop();
                McpBridgeServer.Start();
            }
        }
    }
}
