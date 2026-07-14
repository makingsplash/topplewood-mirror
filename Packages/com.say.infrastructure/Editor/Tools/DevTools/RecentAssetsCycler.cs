using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Newtonsoft.Json;

using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityToolbarExtender;

using Object = UnityEngine.Object;

namespace SGS.Editor.Tools.DevTools
{
    [InitializeOnLoad]
    public class RecentAssetsCycler : ScriptableSingleton<RecentAssetsCycler>
    {
        private static GUIStyle _popupStyle;
        private static GUIStyle PopupStyle => _popupStyle ??= new GUIStyle("ToolbarPopup")
        {
            fixedWidth = 300,
            stretchWidth = false,
        };

        [JsonObject(MemberSerialization.OptIn)]
        [Serializable]
        private struct AssetItem
        {
            [JsonProperty]
            public string Path;
            [NonSerialized]
            public GUIContent Content;

            public AssetItem(string path)
            {
                Path = path;
                var lastSlash = path.LastIndexOf('/');
                var name = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
                Content = new GUIContent(name, AssetDatabase.GetCachedIcon(path));
            }
        }

        private static List<AssetItem> _assetsStack = null;
        private AssetItem _selectedItem;
        private EditorCoroutine _ctrlStateCoroutine;
        private bool _ctrlState;
        private int _stackOffset;
        private bool _jumpTriggered;
        private const string _settingsPath = "UserSettings/AssetsCycler.settings";

        [JsonObject]
        public class RecentAssetsCyclerConfig
        {
            public string[] AssetStack;
        }

        static RecentAssetsCycler()
        {
            ToolbarExtender.LeftToolbarGUI.Add(OnToolbarGUI);
            EditorApplication.update += OnUpdate;
            Selection.selectionChanged += OnSelectionChanged;
            AssemblyReloadEvents.afterAssemblyReload += ScriptsHasBeenReloaded;
        }
        
        private static void ScriptsHasBeenReloaded()
        {
            LoadStack();
            SceneView.duringSceneGui -= DuringSceneGui;
            SceneView.duringSceneGui += DuringSceneGui;
        }

        private static void OnSelectionChanged()
        {
            if (Selection.objects.Length != 1)
            {
                return;
            }

            Object obj = Selection.activeObject;

            if (obj == null)
            {
                return;
            }

            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (Directory.Exists(path))
            {
                return;
            }

            if (_assetsStack.Count != 0)
            {
                var idx = _assetsStack.FindIndex(i => i.Path == path);
                if (idx >= 0)
                {
                    _assetsStack.RemoveAt(idx);
                }
            }

            _assetsStack.Add(new AssetItem(path));
            SaveStack();
            instance._selectedItem = _assetsStack.Count == 0 ? new AssetItem("") : _assetsStack[ _assetsStack.Count - 1];
        }

        
        private static void OnUpdate()
        {
            if (_assetsStack == null)
            {
                LoadStack();
            }
            
            instance._selectedItem = _assetsStack.Count == 0 ? new AssetItem("") : _assetsStack[ _assetsStack.Count - instance._stackOffset % _assetsStack.Count - 1];
            
            if (instance._jumpTriggered)
            {
                JumpToAsset(instance._selectedItem.Path);
                instance._jumpTriggered = false;
                instance._stackOffset = 0;
            }
        }


        private static void OnToolbarGUI()
        {
            GUILayout.Space(16);
            Rect rMode = GUILayoutUtility.GetRect(instance._selectedItem.Content, PopupStyle);
            if (GUI.Button(rMode, instance._selectedItem.Content, PopupStyle))
            {
                var menu = new GenericMenu();
                foreach (var prefabItem in ((IEnumerable<AssetItem>)_assetsStack).Reverse())
                {
                    menu.AddItem(prefabItem.Content, false, OnItemSelected, prefabItem.Path);
                }

                menu.DropDown(rMode);
            }
        }
        
        private static void OnItemSelected(object userdata)
        {
            var assetName = userdata as string;
            if (assetName == null)
            {
                return;
            } 
            JumpToAsset(assetName);
        }

        private static void JumpToAsset(string assetName)
        {
            var selectedObj = AssetDatabase.LoadAssetAtPath<Object>(assetName);
            if (selectedObj != null)
            {
                Selection.activeObject = selectedObj;
            }
            else
            {
                var idx = _assetsStack.FindIndex(i => i.Path == assetName);
                if (idx >= 0)
                {
                    _assetsStack.RemoveAt(idx);
                }
            }
        }

        [Shortcut("Cycle open assets", KeyCode.Tab, ShortcutModifiers.Control)]
        static void AddShift(ShortcutArguments args)
        {
            instance._stackOffset += 1;
            instance._ctrlStateCoroutine ??= EditorCoroutineUtility.StartCoroutineOwnerless(WaitForControlCoroutine());
        }
        
        static IEnumerator WaitForControlCoroutine()
        {
            yield return new EditorWaitForSeconds(0.4f);
            
            while (instance._ctrlState)
            {
                yield return null;
            }
            instance._jumpTriggered = true;
            instance._ctrlStateCoroutine = null;
        }

        private static void DuringSceneGui(SceneView sceneView)
        {
            instance._ctrlState = Event.current.control;
        }
        
        private static void SaveStack()
        {
            var config = new RecentAssetsCyclerConfig();
            config.AssetStack = _assetsStack.Select(i => i.Path).ToArray();
            
            File.WriteAllText(_settingsPath, JsonConvert.SerializeObject(config));
        }

        private static void LoadStack()
        {
            _assetsStack = new List<AssetItem>();
            try
            {
                var config = JsonConvert.DeserializeObject<RecentAssetsCyclerConfig>(File.ReadAllText(_settingsPath));
                foreach (string assetPath in config.AssetStack)
                {
                    if (!AssetDatabase.GUIDFromAssetPath(assetPath).Empty())
                    {
                        _assetsStack.Add(new AssetItem(assetPath));
                    }
                }

                return;
            }
            catch
            {
            }

            var oldData = EditorPrefs.GetString("RecentAssetsCycler.Stack", "[]");
            JsonConvert.PopulateObject(oldData, _assetsStack);
            for (int i = 0; i < _assetsStack.Count; i++)
            {
                _assetsStack[i] = new AssetItem(_assetsStack[i].Path);
            }
        }
    }
}