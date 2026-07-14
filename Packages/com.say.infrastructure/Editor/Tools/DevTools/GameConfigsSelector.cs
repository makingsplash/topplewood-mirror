using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SGS.Infrastructure.Tools.Configs;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UnityToolbarExtender;

namespace SGS.Editor.Tools.DevTools
{
    [InitializeOnLoad]
    public class GameConfigsSelector
    {
        private static readonly GUIContent _buttonContents;
        private static List<AssetItem> _configs;
        private class AssetItem
        {
            public readonly Type ConfigType; 
            public readonly string Path;
            public readonly string Group;
            public readonly GUIContent Content;

            public AssetItem(Type configType, GameConfigAttribute attr)
            {
                ConfigType =  configType;
                Path = attr.AssetDbPath;
                Group = attr.Group;
                Content = new GUIContent(attr.ConfigName, AssetDatabase.GetCachedIcon(Path));
            }
        }
        
        static GameConfigsSelector()
        {
            _buttonContents = new GUIContent("Edit config...");
            ToolbarExtender.LeftToolbarGUI.Add(OnToolbarGUI);
            AssemblyReloadEvents.afterAssemblyReload += ScriptsHasBeenReloaded;
            ScriptsHasBeenReloaded();
        }

        private static void ScriptsHasBeenReloaded()
        {
            _configs = new List<AssetItem>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var assemblyTypes = assembly.GetTypes();
                foreach (var type in assemblyTypes)
                {
                    if (type.IsDefined(typeof(GameConfigAttribute), false))
                    {
                        _configs.Add(new AssetItem(type, type.GetCustomAttribute<GameConfigAttribute>()));
                    }
                }
            }

            _configs = _configs.OrderBy(a => a.Group).ThenBy(a => a.Content.text).ToList();
        }

        private static void OnToolbarGUI()
        {
            if (_configs.Count <= 0)
            {
                return;
            }

            GUILayout.Space(16);

            Rect buttonRect = GUILayoutUtility.GetRect(_buttonContents, EditorStyles.toolbarButton, GUILayout.Width(150));
            if (GUI.Button(buttonRect, _buttonContents, EditorStyles.toolbarButton))
            {
                var menu = new GenericMenu();
                foreach (var configGroup in _configs.GroupBy(a => a.Group))
                {
                    if (menu.GetItemCount() != 0)
                    {
                        menu.AddSeparator("");
                    }

                    foreach (var config in configGroup)
                    {
                        menu.AddItem(config.Content, false, OnItemSelected, config);
                    }
                }

                menu.DropDown(buttonRect);
            }
        }

        private static void OnItemSelected(object userdata)
        {
            var assetItem = userdata as AssetItem;
            if (assetItem == null)
            {
                return;
            }

            var selectedObj = AssetDatabase.LoadAssetAtPath(assetItem.Path, assetItem.ConfigType);
            if (selectedObj == null)
            {
                selectedObj = ScriptableObject.CreateInstance(assetItem.ConfigType);
                AssetDatabase.CreateAsset(selectedObj, assetItem.Path);
            }

            Selection.activeObject = selectedObj;
        }
    }
}