using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Iliai.UiBuild
{
    /// <summary>Всё, что нужно хэндлеру и билдеру во время сборки одной спеки.</summary>
    public sealed class UiBuildContext
    {
        public UiBuildConfig Config { get; }
        public UiTokens Tokens { get; }
        public UiBuildReport Report { get; }
        public UiSpec Spec { get; internal set; }

        /// <summary>Компонент-контроллер на корне (если спека его объявила) — цель для `bind`.</summary>
        public MonoBehaviour Controller { get; internal set; }

        readonly Dictionary<string, string> _prefabRegistry = new Dictionary<string, string>();

        public UiBuildContext(UiBuildConfig config, UiTokens tokens, UiBuildReport report)
        {
            Config = config;
            Tokens = tokens;
            Report = report;
            LoadPrefabRegistry();
        }

        void LoadPrefabRegistry()
        {
            string path = Config.Abs(Config.PrefabRegistryPath);
            if (!File.Exists(path)) return;
            object root;
            try { root = UiJson.Parse(File.ReadAllText(path)); }
            catch (UiJsonException e)
            {
                Report.Error(Config.PrefabRegistryPath, "битый JSON: " + e.Message);
                return;
            }
            if (!(root is Dictionary<string, object> map))
            {
                Report.Error(Config.PrefabRegistryPath, "корень должен быть объектом id → путь префаба");
                return;
            }
            foreach (KeyValuePair<string, object> kv in map)
            {
                if (kv.Value is string p) _prefabRegistry[kv.Key] = p;
                else Report.Error(Config.PrefabRegistryPath, $"'{kv.Key}' должен быть строкой-путём");
            }
        }

        public IEnumerable<string> KnownPrefabs() => _prefabRegistry.Keys;

        /// <summary>
        /// Путь спрайта. Без слэша — рядом со спекой (`&lt;spriteRoot&gt;/&lt;имя спеки&gt;/файл`),
        /// со слэшем — от корня спрайтов. Конвенция ровно одна, чтобы спека не таскала длинные пути.
        /// </summary>
        public Sprite ResolveSprite(string reference, string where)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            string path = reference.Contains("/")
                ? Config.SpriteRoot + "/" + reference
                : Config.SpriteRoot + "/" + Spec.Name + "/" + reference;

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Report.Error(where, $"спрайт не найден: {path} (ссылка '{reference}')");
            return sprite;
        }

        public GameObject ResolvePrefab(string id, string where)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!_prefabRegistry.TryGetValue(id, out string path))
            {
                Report.Error(where, $"префаб '{id}' не зарегистрирован в {Config.PrefabRegistryPath}. Известные: {string.Join(", ", KnownPrefabs())}");
                return null;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Report.Error(where, $"префаб '{id}' зарегистрирован как {path}, но там ассета нет");
            return prefab;
        }
    }
}
