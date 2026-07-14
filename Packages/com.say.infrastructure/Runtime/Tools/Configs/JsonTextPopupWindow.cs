#if UNITY_EDITOR

using System;

using UnityEditor;

using UnityEngine;

namespace SGS.Infrastructure.Tools.Configs
{
    public class JsonTextPopupWindow : EditorWindow
    {
        private string _text;
        private Action<string> _onClose;
        private Vector2 _scrollPos;

        public static void Show( string text, Action<string> onClose )
        {
            var window = CreateInstance<JsonTextPopupWindow>();
            window.position = new Rect(Screen.width / 2, Screen.height / 2, 550, 350);
            window.minSize = new Vector2( 550, 350);
            window.Init(text, onClose);
            window.titleContent = new GUIContent("Json config");
            window.ShowAuxWindow();
        }

        private void Init(string text, Action<string> onClose)
        {
            _text = text;
            _onClose = onClose;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("JSON config");
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, true, true, GUILayout.Width(position.width), GUILayout.Height(position.height - 50));
            _text = EditorGUILayout.TextArea(_text, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (_onClose != null)
            {
                if (GUILayout.Button("Apply config"))
                {
                    _onClose(_text);
                    Close();
                }
            }
            else
            {
                if (GUILayout.Button("Copy to clipboard"))
                {
                    GUIUtility.systemCopyBuffer = _text;
                }
            }

            if (GUILayout.Button("Close"))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

        }
    }
}
#endif