using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TMPro;

namespace Iliai.UiBuild
{
    public sealed class UiTextStyle
    {
        public string Name;
        public float FontSize = 36f;
        public bool Bold;
        public bool Uppercase;
        public float LineSpacing;
        public float CharacterSpacing;
        public string Color;
        public string Font;                      // путь к TMP_FontAsset
    }

    /// <summary>
    /// Токены цвета и типографики. Смысл: цвет и кегль в спеке — ИМЯ, а не значение. Смена палитры
    /// или шрифта правится в одном файле, а не в тридцати спеках; и роль не изобретает hex сама.
    /// Hex в спеке разрешён (для быстрых плейсхолдеров), но попадает в отчёт предупреждением.
    /// </summary>
    public sealed class UiTokens
    {
        readonly Dictionary<string, string> _colors = new Dictionary<string, string>();
        readonly Dictionary<string, UiTextStyle> _styles = new Dictionary<string, UiTextStyle>();
        readonly Dictionary<string, TMP_FontAsset> _fontCache = new Dictionary<string, TMP_FontAsset>();

        public bool HasAny => _colors.Count > 0 || _styles.Count > 0;

        public static UiTokens Load(UiBuildConfig cfg, UiBuildReport report)
        {
            var tokens = new UiTokens();
            string path = cfg.Abs(cfg.TokensPath);
            if (!File.Exists(path)) return tokens;              // токены опциональны

            object root;
            try { root = UiJson.Parse(File.ReadAllText(path)); }
            catch (UiJsonException e)
            {
                report.Error(cfg.TokensPath, "битый JSON: " + e.Message);
                return tokens;
            }
            if (!(root is Dictionary<string, object> map))
            {
                report.Error(cfg.TokensPath, "корень должен быть объектом");
                return tokens;
            }

            var r = new UiReader(map, report, cfg.TokensPath);
            Dictionary<string, object> colors = r.Map("colors");
            if (colors != null)
            {
                foreach (KeyValuePair<string, object> kv in colors)
                {
                    if (kv.Value is string hex) tokens._colors[kv.Key] = hex;
                    else report.Error(cfg.TokensPath, $"colors.{kv.Key} должен быть строкой '#RRGGBB'");
                }
            }

            Dictionary<string, object> styles = r.Map("styles");
            if (styles != null)
            {
                foreach (KeyValuePair<string, object> kv in styles)
                {
                    if (!(kv.Value is Dictionary<string, object> sm))
                    {
                        report.Error(cfg.TokensPath, $"styles.{kv.Key} должен быть объектом");
                        continue;
                    }
                    var sr = new UiReader(sm, report, cfg.TokensPath + ".styles." + kv.Key);
                    var style = new UiTextStyle
                    {
                        Name = kv.Key,
                        FontSize = sr.Float("fontSize", 36f),
                        Bold = sr.Bool("bold"),
                        Uppercase = sr.Bool("uppercase"),
                        LineSpacing = sr.Float("lineSpacing", 0f),
                        CharacterSpacing = sr.Float("characterSpacing", 0f),
                        Color = sr.Str("color"),
                        Font = sr.Str("font"),
                    };
                    sr.Finish();
                    tokens._styles[kv.Key] = style;
                }
            }
            r.Finish();
            return tokens;
        }

        public UiTextStyle Style(string name, UiBuildReport report, string where)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_styles.TryGetValue(name, out UiTextStyle s)) return s;
            report.Error(where, $"неизвестный стиль '{name}'. Известные: {string.Join(", ", KnownStyles())}");
            return null;
        }

        public IEnumerable<string> KnownStyles() => _styles.Keys;
        public IEnumerable<string> KnownColors() => _colors.Keys;

        public bool TryColor(string nameOrHex, UiBuildReport report, string where, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(nameOrHex)) return false;

            string hex = nameOrHex;
            if (!nameOrHex.StartsWith("#"))
            {
                if (!_colors.TryGetValue(nameOrHex, out hex))
                {
                    report.Error(where, $"неизвестный цвет-токен '{nameOrHex}'. Известные: {string.Join(", ", KnownColors())}");
                    return false;
                }
            }
            else
            {
                report.Warn(where, $"hex '{nameOrHex}' вместо токена — для плейсхолдера ок, в готовой вёрстке цвет должен быть именем из tokens.json");
            }

            if (ColorUtility.TryParseHtmlString(hex, out color)) return true;
            report.Error(where, $"не разобрал цвет '{hex}' (ожидается #RRGGBB или #RRGGBBAA)");
            return false;
        }

        public TMP_FontAsset Font(string path, UiBuildReport report, string where)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_fontCache.TryGetValue(path, out TMP_FontAsset cached)) return cached;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null) report.Error(where, $"шрифт не найден: {path}");
            _fontCache[path] = font;
            return font;
        }
    }
}
