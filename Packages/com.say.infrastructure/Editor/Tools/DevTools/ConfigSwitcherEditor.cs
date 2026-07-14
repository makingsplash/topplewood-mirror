using SGS.Infrastructure.Tools.Configs;
using UnityEditor;
using UnityEngine;
using UnityToolbarExtender;

namespace SGS.Editor.Tools.DevTools
{
 #if GPIE_DEVELOPMENT
    [InitializeOnLoad]
    public class ConfigSwitcherEditor
    {
        private static ConfigSwitcher.Settings _settings;

        static ConfigSwitcherEditor()
        {
            ToolbarExtender.RightToolbarGUI.Add(OnToolbarGUI);
            _settings = ConfigSwitcher.LoadSettings();
        }

        private static void OnToolbarGUI()
        {
            GUILayout.Space(16);

            GUILayout.Label("Config:",EditorStyles.boldLabel, GUILayout.Width(50));
            if (GUILayout.Toggle(_settings.UseLocalConfig, "Local", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                _settings.UseLocalConfig = true;
                ConfigSwitcher.SaveSettings(_settings);
            }

            if (GUILayout.Toggle(!_settings.UseLocalConfig, "Remote", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                _settings.UseLocalConfig = false;
                ConfigSwitcher.SaveSettings(_settings);
            }

            GUI.enabled = !_settings.UseLocalConfig && !Application.isPlaying;
            var v = GUILayout.TextArea(PlayerSettings.bundleVersion,  EditorStyles.toolbarTextField, GUILayout.Width(120));
            if (v != PlayerSettings.bundleVersion)
            {
                PlayerSettings.bundleVersion = v;
            }

            GUI.enabled = true;
        }
    }
#endif
}