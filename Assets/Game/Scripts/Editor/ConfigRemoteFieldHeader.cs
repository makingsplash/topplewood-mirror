using System;
using DataStorage;
using SGS.Infrastructure.Tools.Configs;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Показывает в шапке инспектора ConfigWithJson-ассета имя ремоут-поля SayKitGameConfig,
    /// из которого конфиг инициализируется. Источник данных — RemoteConfigMap,
    /// та же карта, по которой Configs.InitWithRemote реально раздаёт JSON в рантайме.
    /// </summary>
    [InitializeOnLoad]
    public static class ConfigRemoteFieldHeader
    {
        private static readonly Color FieldNameColor = new(0.15f, 0.95f, 0.35f);
        private static GUIStyle _fieldNameStyle;

        private static GUIStyle FieldNameStyle
        {
            get
            {
                if (_fieldNameStyle == null)
                {
                    _fieldNameStyle = new GUIStyle(EditorStyles.boldLabel);
                    _fieldNameStyle.normal.textColor = FieldNameColor;
                    _fieldNameStyle.hover.textColor = FieldNameColor;
                    _fieldNameStyle.focused.textColor = FieldNameColor;
                    _fieldNameStyle.active.textColor = FieldNameColor;
                }

                return _fieldNameStyle;
            }
        }

        static ConfigRemoteFieldHeader()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawRemoteField;
        }

        private static void DrawRemoteField(UnityEditor.Editor editor)
        {
            if (editor.targets.Length != 1) return;

            var target = editor.target;
            if (target == null || !IsConfigWithJson(target.GetType())) return;

            if (RemoteConfigMap.TryGetRemoteFieldName(target.GetType(), out var fieldName))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Launcher Remote field");
                EditorGUILayout.SelectableLabel(fieldName, FieldNameStyle,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));

                if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    EditorGUIUtility.systemCopyBuffer = fieldName;
                }

                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField("Remote field", "— (не привязан в RemoteConfigMap)");
            }
        }

        private static bool IsConfigWithJson(Type type)
        {
            for (var current = type.BaseType; current != null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ConfigWithJson<>))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
