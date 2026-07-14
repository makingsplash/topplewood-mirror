using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json;

using SayKitInternal;

using TMPro;

using Unity.EditorCoroutines.Editor;

using UnityEditor;

using UnityEngine;

namespace SGS.Editor.Tools.DevTools
{
    public class LocalizationFontsWindow : EditorWindow
    {
        private const string _chineseFontAssetPath = "Assets/Fonts/NotoSans/NotoSans-SC.asset";
        private const string _japaneseFontAssetPath = "Assets/Fonts/NotoSans/NotoSans-JP.asset";
        private const string _koreanFontAssetPath = "Assets/Fonts/NotoSans/NotoSans-KR.asset";
        
        private const string _gameMessagesCsvFile = "Assets/hxasrta_game_messages.csv";
        
        private TMP_FontAsset _chineseFontAsset;
        private TMP_FontAsset _japaneseFontAsset;
        private TMP_FontAsset _koreanFontAsset;
        
        private string _chineseCharacters;
        private string _japaneseCharacters;
        private string _koreanCharacters;
        
        private readonly Vector2Int[] _chineseRange = {new(0x4E00, 0x9FFF)};
        private readonly Vector2Int[] _japaneseRange = {new(0x3000, 0x30ff), new(0xFF01, 0xFFEF)};
        private readonly Vector2Int[] _koreanRange = {new(0x1100, 0x11FF), new(0xAC00, 0xD7AF)};
        
        
        
        private bool _isDownloadingRemoteConfig;
        private RemoteConfig _remoteConfig;
        private string _lastDownloadRemoteConfigVersion;
        private string _lastBundleVersionText;
        [MenuItem("DevTools/Update Fonts")]
        private static void ShowWindow()
        {
            var window = (LocalizationFontsWindow)GetWindow(typeof(LocalizationFontsWindow));
            window.Show();
        }

        private IEnumerator DownloadRemoteConfig(string bundleVersion)
        {
            _isDownloadingRemoteConfig = true;
            yield return EditorCoroutineUtility.StartCoroutine(RemoteConfigTools.DownloadAndParseRemoteConfig(bundleVersion, (success, result) =>
            {
                _isDownloadingRemoteConfig = false;
                if (success)
                {
                    _remoteConfig = result;
                }
                else
                {
                    Debug.LogError("Не удалось загрузить Remote конфиг");
                }
                
            }), this);
        }

        private void OnGUI()
        {
            GUILayout.Label("Asian fonts:", EditorStyles.boldLabel);

            if (string.IsNullOrEmpty(_lastBundleVersionText))
            {
                _lastBundleVersionText = $"Bundle version: {PlayerSettings.bundleVersion}";
            }
            GUILayout.Label(_lastBundleVersionText, EditorStyles.boldLabel);
            
            
            // переинициализация данных ссылок
            if (!_chineseFontAsset)
            {
                _chineseFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(_chineseFontAssetPath);
            }
            if (!_japaneseFontAsset)
            {
                _japaneseFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(_japaneseFontAssetPath);
            }
            if (!_koreanFontAsset)
            {
                _koreanFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(_koreanFontAssetPath);
            }

            // список ссылок на азиатские скрипты
            _chineseFontAsset = EditorGUILayout.ObjectField("Font", _chineseFontAsset, typeof(TMP_FontAsset), false) as TMP_FontAsset;
            if (_chineseCharacters != null)
            {
                GUILayout.Label("Characters to draw");
                GUILayout.TextArea(_chineseCharacters);
            }
            _japaneseFontAsset = EditorGUILayout.ObjectField("Japanese", _japaneseFontAsset, typeof(TMP_FontAsset), false) as TMP_FontAsset;
            if (_japaneseCharacters != null)
            {
                GUILayout.Label("Characters to draw");
                GUILayout.TextArea(_japaneseCharacters);
            }
            _koreanFontAsset = EditorGUILayout.ObjectField("Korean", _koreanFontAsset, typeof(TMP_FontAsset), false) as TMP_FontAsset;
            if (_koreanCharacters != null)
            {
                GUILayout.Label("Characters to draw");
                GUILayout.TextArea(_koreanCharacters);
            }

            //
            bool hasRemoteConfig = _remoteConfig != null;
            if (!_isDownloadingRemoteConfig 
                && (!hasRemoteConfig
                    || _lastDownloadRemoteConfigVersion != PlayerSettings.bundleVersion))
            {
                _lastDownloadRemoteConfigVersion = PlayerSettings.bundleVersion;
                _lastBundleVersionText = $"Bundle version: {PlayerSettings.bundleVersion}";
                _isDownloadingRemoteConfig = true;

                EditorCoroutineUtility.StartCoroutine(DownloadRemoteConfig(_lastDownloadRemoteConfigVersion), this);
            }
                        
            // отображение информации о загрузке remote-конфига
            if (_isDownloadingRemoteConfig)
            {
                GUILayout.Label("Downloading remote config...", EditorStyles.boldLabel);
            }
            
            GUI.enabled = hasRemoteConfig;
            if (GUILayout.Button("Load characters from remote config"))
            {
                var appKey = SayKitAppSettingsLoader.ResolveAppKey();
                var url = " https://app.saygames.io/localization/" + appKey;
                var errorTemplate = "Can't load localization for " + appKey + " version " + Application.version;
                
                SKLocalizationService.Instance.DownloadLocalizationsWithAttempts(url, JsonConvert.SerializeObject(new
                {
                    version = Application.version,
                    saykit = SKManager.Instance.Version,
                    v = 1,
                    embedded = true
                }),
                1,
                (data, error) =>
                {
                    if (string.IsNullOrEmpty(error))
                    {
                        if (!string.IsNullOrEmpty(data))
                        {
                            var localizations = JsonConvert.DeserializeObject<SKLanguageLocalization[]>(data);

                            if (localizations.Length > 0)
                            {
                                var allLocalizationStringBuilder = new StringBuilder();
                                
                                var jaLoc = localizations.FirstOrDefault(l => l.Language == SayKitLanguageConverter.ConvertFromSayKitLanguage(SayKitLanguage.Japanese));
                                if (jaLoc != null)
                                {
                                    foreach (var value in jaLoc.Strings.Values)
                                    {
                                        CollectLocaleCharacters(allLocalizationStringBuilder, value);
                                    }
                                    
                                    _japaneseCharacters = SelectCharacterRange(allLocalizationStringBuilder.ToString(), _japaneseRange);
                                    allLocalizationStringBuilder.Clear();
                                }
                                
                                var kpLoc = localizations.FirstOrDefault(l => l.Language == SayKitLanguageConverter.ConvertFromSayKitLanguage(SayKitLanguage.Korean));
                                if (kpLoc != null)
                                {
                                    foreach (var value in kpLoc.Strings.Values)
                                    {
                                        CollectLocaleCharacters(allLocalizationStringBuilder, value);
                                    }
                                    
                                    _koreanCharacters = SelectCharacterRange(allLocalizationStringBuilder.ToString(), _koreanRange);
                                    allLocalizationStringBuilder.Clear();
                                }
                                
                                var chSLoc = localizations.FirstOrDefault(l => l.Language == SayKitLanguageConverter.ConvertFromSayKitLanguage(SayKitLanguage.Chinese));
                                if (chSLoc != null)
                                {
                                    foreach (var value in chSLoc.Strings.Values)
                                    {
                                        CollectLocaleCharacters(allLocalizationStringBuilder, value);
                                    }
                                    
                                    _chineseCharacters = SelectCharacterRange(allLocalizationStringBuilder.ToString(), _chineseRange);
                                    allLocalizationStringBuilder.Clear();
                                }
                            }
                        }
                        else
                        {
                            _ = "Empty localizations data";
                        }
                    }
                    else
                    {
                        _ = $"{errorTemplate} {error}";
                    }
                });
            }
            GUI.enabled = true;

            if (GUILayout.Button("Load characters from local file"))
            {
                StringBuilder allLocalizationStringBuilder = new StringBuilder();
                CollectLocaleCharacters(allLocalizationStringBuilder, "text_ja");
                CollectLocaleCharacters(allLocalizationStringBuilder, "text_ko");
                CollectLocaleCharacters(allLocalizationStringBuilder, "text_zh");

                var s = allLocalizationStringBuilder.ToString();
                _chineseCharacters = SelectCharacterRange(s,_chineseRange);
                _koreanCharacters = SelectCharacterRange(s,_koreanRange);
                _japaneseCharacters = SelectCharacterRange(s,_japaneseRange);
            }

            
            // кнопка перегенерации азиатских шрифтов
            GUI.enabled = _chineseCharacters != null && _chineseFontAsset != null;
            if (GUILayout.Button("Regenerate atlases"))
            {
                bool isSuccess = true;
                if (_chineseFontAsset)
                {
                    isSuccess = TryRegenerateAtlas(_chineseFontAsset, _chineseCharacters);
                }

                if (_koreanFontAsset)
                {
                    isSuccess = TryRegenerateAtlas(_koreanFontAsset, _koreanCharacters) && isSuccess;
                }

                if (_japaneseFontAsset)
                {
                    isSuccess = TryRegenerateAtlas(_japaneseFontAsset, _japaneseCharacters) && isSuccess;
                }

                if (isSuccess)
                {
                    // сохраняем правки
                    EditorApplication.ExecuteMenuItem("File/Save Project");
                
                    Debug.Log("<color=#00ff00>Font atlases generation complete!</color>");
                }
            }
            GUI.enabled = true;

        }

        private string SelectCharacterRange(string localeChars, Vector2Int[] ranges)
        {
            HashSet<uint> codepoints = new(); 
            foreach (uint unicode in localeChars)
            {
                if (ranges.Any(r => r.x <= unicode && unicode <= r.y))
                {
                    codepoints.Add(unicode);
                }
            }
            
            StringBuilder filteredStingBuilder = new StringBuilder();

            foreach (uint unicode in codepoints)
            {
                filteredStingBuilder.Append((char)unicode);
            }

            return filteredStingBuilder.ToString();
        }
        
        private void CollectLocaleCharacters(StringBuilder localeStringBuilder, string localizedString)
        {
            if (string.IsNullOrWhiteSpace(localizedString))
            {
                return;
            }

            // удаление лишних тегов
            if (localizedString.Contains("<"))
            {
                localizedString = Regex.Replace(localizedString, @"<[^>]*>", String.Empty);
            }
            if (localizedString.Contains("{"))
            {
                localizedString = Regex.Replace(localizedString, @"{\d+}", String.Empty);
            }
            
            localeStringBuilder.Append(localizedString);
        }
        
        private bool TryRegenerateAtlas(TMP_FontAsset fontAsset, string localeChars)
        {
            // запись уникальной коллекции символов в атлас
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.ClearFontAssetData(true);
            bool success = fontAsset.TryAddCharacters(localeChars, out var missingUnicodes,  true);
            if (!success)
            {
                if (missingUnicodes.Length > 0)
                {
                    Debug.LogError($"В шрифте {fontAsset} не нашлось символов: {string.Join(',',missingUnicodes)}", fontAsset);
                }
                Debug.LogError($"Не поместились все символы в атлас. Необходимо увеличить размер атласа. ({fontAsset})", fontAsset);
                return false;
            }
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);
            EditorUtility.SetDirty(fontAsset);
            
            return true;
        }
    }
}