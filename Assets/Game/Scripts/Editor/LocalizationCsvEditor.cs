using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    public abstract class LocalizationJsonViewerBase : UnityEditor.Editor
    {
        private const string LocalizationJsonDir = "Assets/Resources/SayKit/Localizations";
        private const float DefaultFirstColumnWidth = 260f;
        private const float DefaultLanguageColumnWidth = 360f;
        private const float MinColumnWidth = 120f;
        private const float HeaderHeight = 18f;
        private const float SplitterHitWidth = 8f;
        private const string CodeWidthPrefKey = "TileSort.LocalizationJsonViewer.CodeColumnWidth";
        private const string LanguageWidthPrefKey = "TileSort.LocalizationJsonViewer.LanguageColumnWidth";

        private UnityEditor.Editor _defaultEditor;
        private Vector2 _scroll;
        private bool _isLocalizationAsset;
        private bool _isLoaded;
        private string _errorMessage;
        private string _searchQuery = string.Empty;
        private string _languageCode = string.Empty;
        private float _codeColumnWidth = DefaultFirstColumnWidth;
        private float _languageColumnWidth = DefaultLanguageColumnWidth;
        private bool _isResizingColumn;
        private float _resizeStartMouseX;
        private float _resizeStartCodeWidth;
        private float _resizeStartLanguageWidth;
        private readonly List<List<string>> _rows = new();

        protected abstract string DefaultInspectorTypeName { get; }

        private void OnEnable()
        {
            _isLocalizationAsset = IsSayKitLocalizationAsset(target);

            if (_isLocalizationAsset)
            {
                LoadColumnWidths();
                LoadFromDisk();
                return;
            }

            var type = Type.GetType(DefaultInspectorTypeName);
            if (type != null)
            {
                _defaultEditor = CreateEditor(targets, type);
            }
        }

        private void OnDisable()
        {
            if (_defaultEditor == null)
            {
                return;
            }

            DestroyImmediate(_defaultEditor);
            _defaultEditor = null;
        }

        public override void OnInspectorGUI()
        {
            if (!_isLocalizationAsset)
            {
                if (_defaultEditor == null)
                {
                    DrawDefaultInspector();
                }
                else
                {
                    _defaultEditor.OnInspectorGUI();
                }

                return;
            }

            var previousEnabled = GUI.enabled;
            GUI.enabled = true;
            try
            {
                DrawToolbar();

                if (!string.IsNullOrWhiteSpace(_errorMessage))
                {
                    EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
                    return;
                }

                if (!_isLoaded || _rows.Count == 0)
                {
                    EditorGUILayout.HelpBox("No localization data.", MessageType.Info);
                    return;
                }

                DrawTable();
            }
            finally
            {
                GUI.enabled = previousEnabled;
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Localization", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(_languageCode))
            {
                EditorGUILayout.LabelField("Language:", GUILayout.Width(62));
                EditorGUILayout.LabelField(_languageCode, EditorStyles.boldLabel, GUILayout.Width(40));
            }

            if (GUILayout.Button("Reload", GUILayout.Width(80)))
            {
                LoadFromDisk();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search", GUILayout.Width(50));
            var updatedSearch = EditorGUILayout.TextField(_searchQuery);
            if (!string.Equals(updatedSearch, _searchQuery, StringComparison.Ordinal))
            {
                _searchQuery = updatedSearch;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        private void DrawTable()
        {
            var header = _rows[0];
            DrawHeader(header);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int rowIndex = 1; rowIndex < _rows.Count; rowIndex++)
            {
                var row = _rows[rowIndex];
                if (!MatchesSearch(row))
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                DrawCell(row, 0, _codeColumnWidth);
                DrawCell(row, 1, _languageColumnWidth);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader(IReadOnlyList<string> header)
        {
            var totalWidth = _codeColumnWidth + _languageColumnWidth + 4f;
            var rowRect = GUILayoutUtility.GetRect(totalWidth, HeaderHeight, GUILayout.ExpandWidth(false));

            var codeRect = new Rect(rowRect.x, rowRect.y, _codeColumnWidth, rowRect.height);
            GUI.Label(codeRect, header.Count > 0 ? header[0] : string.Empty, EditorStyles.boldLabel);

            var langRect = new Rect(codeRect.xMax, rowRect.y, _languageColumnWidth, rowRect.height);
            GUI.Label(langRect, header.Count > 1 ? header[1] : string.Empty, EditorStyles.boldLabel);

            HandleColumnResize(codeRect, langRect);
        }

        private static void DrawCell(List<string> row, int colIndex, float width)
        {
            var value = colIndex < row.Count ? row[colIndex] : string.Empty;
            EditorGUILayout.SelectableLabel(
                value,
                EditorStyles.textField,
                GUILayout.Width(width),
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        private void HandleColumnResize(Rect codeRect, Rect langRect)
        {
            var splitterRect = new Rect(codeRect.xMax - SplitterHitWidth * 0.5f, codeRect.y, SplitterHitWidth, codeRect.height);
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

            var evt = Event.current;
            if (evt == null)
            {
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (splitterRect.Contains(evt.mousePosition))
                {
                    _isResizingColumn = true;
                    _resizeStartMouseX = evt.mousePosition.x;
                    _resizeStartCodeWidth = _codeColumnWidth;
                    _resizeStartLanguageWidth = _languageColumnWidth;
                    evt.Use();
                    return;
                }
            }

            if (_isResizingColumn && evt.type == EventType.MouseDrag)
            {
                var delta = evt.mousePosition.x - _resizeStartMouseX;
                var total = _resizeStartCodeWidth + _resizeStartLanguageWidth;
                _codeColumnWidth = Mathf.Clamp(_resizeStartCodeWidth + delta, MinColumnWidth, total - MinColumnWidth);
                _languageColumnWidth = Mathf.Clamp(total - _codeColumnWidth, MinColumnWidth, total - MinColumnWidth);
                Repaint();
                evt.Use();
                return;
            }

            if (_isResizingColumn && (evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp))
            {
                _isResizingColumn = false;
                SaveColumnWidths();
                evt.Use();
            }
        }

        private void LoadColumnWidths()
        {
            _codeColumnWidth = EditorPrefs.GetFloat(CodeWidthPrefKey, DefaultFirstColumnWidth);
            _languageColumnWidth = EditorPrefs.GetFloat(LanguageWidthPrefKey, DefaultLanguageColumnWidth);
            var total = _codeColumnWidth + _languageColumnWidth;
            _codeColumnWidth = Mathf.Clamp(_codeColumnWidth, MinColumnWidth, total - MinColumnWidth);
            _languageColumnWidth = Mathf.Clamp(_languageColumnWidth, MinColumnWidth, total - MinColumnWidth);
        }

        private void SaveColumnWidths()
        {
            EditorPrefs.SetFloat(CodeWidthPrefKey, _codeColumnWidth);
            EditorPrefs.SetFloat(LanguageWidthPrefKey, _languageColumnWidth);
        }

        private bool MatchesSearch(List<string> row)
        {
            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                return true;
            }

            for (int i = 0; i < row.Count; i++)
            {
                var value = row[i];
                if (value?.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void LoadFromDisk()
        {
            try
            {
                _rows.Clear();
                _errorMessage = null;
                _isLoaded = false;
                _languageCode = string.Empty;

                var assetPath = AssetDatabase.GetAssetPath(target);
                var fullPath = Path.GetFullPath(assetPath);

                if (!File.Exists(fullPath))
                {
                    _errorMessage = $"File not found: {assetPath}";
                    return;
                }

                var json = File.ReadAllText(fullPath, Encoding.UTF8);
                var obj = JObject.Parse(json);

                _languageCode = obj["language"]?.Value<string>() ?? string.Empty;
                var strings = obj["strings"]?.ToObject<Dictionary<string, string>>();

                if (strings == null)
                {
                    _errorMessage = "No 'strings' key found in JSON.";
                    return;
                }

                var allKeys = new SortedSet<string>(strings.Keys, StringComparer.OrdinalIgnoreCase);

                _rows.Add(new List<string> { "code", _languageCode });

                foreach (var key in allKeys)
                {
                    _rows.Add(new List<string> { key, strings[key] });
                }

                _isLoaded = true;
            }
            catch (Exception e)
            {
                _errorMessage = $"Failed to load localization: {e.Message}";
            }
        }

        private static bool IsSayKitLocalizationAsset(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return false;
            }

            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.Equals(dir, LocalizationJsonDir, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return Regex.IsMatch(Path.GetFileName(path), @"^saykit_localization_[a-z]+\.json$", RegexOptions.IgnoreCase);
        }
    }

    [CustomEditor(typeof(TextAsset))]
    public sealed class LocalizationJsonTextAssetEditor : LocalizationJsonViewerBase
    {
        protected override string DefaultInspectorTypeName => "UnityEditor.TextAssetInspector, UnityEditor";
    }

    [CustomEditor(typeof(DefaultAsset))]
    public sealed class LocalizationJsonDefaultAssetEditor : LocalizationJsonViewerBase
    {
        protected override string DefaultInspectorTypeName => "UnityEditor.DefaultAssetInspector, UnityEditor";
    }
}
