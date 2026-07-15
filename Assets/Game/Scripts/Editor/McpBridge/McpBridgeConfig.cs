using UnityEditor;

namespace Game.Editor.McpBridge
{
    public static class McpBridgeConfig
    {
        private const string PortKey = "McpBridge.Port";
        private const string EnabledKey = "McpBridge.Enabled";
        private const int DefaultPort = 18711;

        public static int Port
        {
            get => EditorPrefs.GetInt(PortKey, DefaultPort);
            set => EditorPrefs.SetInt(PortKey, value);
        }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, true);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }
    }
}
