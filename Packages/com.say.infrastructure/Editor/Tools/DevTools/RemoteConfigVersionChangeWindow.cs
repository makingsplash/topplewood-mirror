using UnityEditor;

using UnityEngine;

namespace SGS.Editor.Tools.DevTools
{
    public class RemoteConfigVersionChangeWindow : EditorWindow
    {
        private const string _labelText = "Application Version:";
        private const string _buttonText = "Save Asset";

        private static string _value;
        
        [MenuItem("DevTools/RemoteConfig/ChangeVersion")]
        private static void ShowWindow()
        {
            var window = (RemoteConfigVersionChangeWindow)GetWindow(typeof(RemoteConfigVersionChangeWindow));
            window.Show();

            _value = PlayerSettings.bundleVersion;
        }

        private void OnGUI()
        {
            GUILayout.Label(_labelText, EditorStyles.boldLabel);
            _value = GUILayout.TextField(_value);

            if (GUILayout.Button(_buttonText))
            {
                PlayerSettings.bundleVersion = _value;
                AssetDatabase.Refresh();
                AssetDatabase.SaveAssets();
            }
        }
    }
}