using UnityEditor;
using UnityEngine;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using R3;
using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;

namespace MiroTools.Editor
{
    public class MiroToolsWindow : OdinEditorWindow
    {
        private const string ConfigFolder = "Assets/Game/Tools/MiroTools/Config/";
        private const string ConfigName = "MiroToolsConfig";

        [MenuItem("Game/Tools/Miro")]
        private static void OpenWindow()
        {
            var window = GetWindow<MiroToolsWindow>();
            window.titleContent = new GUIContent("Miro Tools");
            window.Show();
        }

        [TabGroup("Features")]
        [ShowInInspector]
        [ListDrawerSettings(ShowFoldout = true, IsReadOnly = true, HideAddButton = true, HideRemoveButton = true)]
        [Searchable] // Полезно, если фич станет много
        private List<Feature> _features = new();

        [TabGroup("Settings")]
        [InlineEditor(InlineEditorModes.FullEditor)]
        public MiroToolsConfig _toolsConfig;

        [PropertyOrder(1000)]
        [BoxGroup("System Status")]
        [DisplayAsString, HideLabel, ShowInInspector]
        private string ConnectionStatus => _statusProperty.Value;

        private readonly ReactiveProperty<string> _statusProperty = new("Ready");
        private IDisposable _statusSubscription;

        protected override void OnEnable()
        {
            base.OnEnable();

            if (_toolsConfig == null)
            {
                string[] guids = AssetDatabase.FindAssets($"t:{ConfigName}");
                if (guids.Length > 0)
                {
                    _toolsConfig = AssetDatabase.LoadAssetAtPath<MiroToolsConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
                else
                {
                    _toolsConfig = CreateNewConfig();
                }
            }

            if (_toolsConfig != null)
            {
                _toolsConfig.OnCreateBoardRequest = () => NewBoardPopup.ShowWindow(_toolsConfig);

                if (!string.IsNullOrEmpty(_toolsConfig.ApiToken))
                {
                    RefreshMiroData().Forget();
                }
                else
                {
                    _statusProperty.Value = "Please enter API Token in Settings";
                }
            }

            _features = new List<Feature>
            {
                //new LevelsTableFeature(),
                //new LevelPrefabsTableFeature()
            };

            foreach (var feature in _features)
            {
                var featureConfig = GetOrCreateFeatureConfig(feature);
                feature.Initialize(_toolsConfig, _statusProperty, featureConfig);
            }

            _statusSubscription = _statusProperty.Subscribe(_ => Repaint());
        }


        private ScriptableObject GetOrCreateFeatureConfig(Feature feature)
        {
            Type configType = feature.GetConfigType();
            if (configType == null) return null;

            string configName = configType.Name;
            string[] guids = AssetDatabase.FindAssets($"t:{configName}");

            if (guids.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            // Создаем новый, если не нашли
            var configPath = Path.Combine(ConfigFolder, $"{configName}.asset").Replace("\\", "/");
            var directory = Path.GetDirectoryName(configPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var config = CreateInstance(configType) as ScriptableObject;
            AssetDatabase.CreateAsset(config, configPath);
            AssetDatabase.SaveAssets();

            return config;
        }


        private MiroToolsConfig CreateNewConfig()
        {
            var configPath = Path.Combine(ConfigFolder, $"{ConfigName}.asset");
            // Заменяем обратные слеши на прямые для Unity (актуально для Windows)
            configPath = configPath.Replace("\\", "/");

            var directory = Path.GetDirectoryName(configPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            var config = CreateInstance<MiroToolsConfig>();
            AssetDatabase.CreateAsset(config, configPath);
            AssetDatabase.SaveAssets();

            // Фокусируемся на новом конфиге в инспекторе, чтобы пользователь сразу его увидел
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = config;

            Debug.Log($"[Miro] New config created at: {configPath}");
            return config;
        }


        private async UniTaskVoid RefreshMiroData()
        {
            _statusProperty.Value = "Connecting to Miro...";
            try
            {
                await _toolsConfig.RefreshBoards();
                _statusProperty.Value = "Connected";
            }
            catch (Exception e)
            {
                _statusProperty.Value = $"Connection Error: {e.Message}";
                Debug.LogError($"[Miro] Connection failed: {e.Message}");
            }
        }

        protected override void OnDestroy()
        {
            if (_toolsConfig != null)
                _toolsConfig.OnCreateBoardRequest = null;

            _statusSubscription?.Dispose();
            base.OnDestroy();
        }
    }
}
