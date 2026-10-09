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
        public string Material;                  // путь к материалу TMP (.mat) — обводка/тень/свечение; атлас обязан совпадать со шрифтом
        public string GradientTop;               // цвет-токен верха вертикального градиента заливки
        public string GradientBottom;            // цвет-токен низа; задаются только парой
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
        readonly Dictionary<string, Material> _materialCache = new Dictionary<string, Material>();

        public bool HasAny => _colors.Count > 0 || _styles.Count > 0;

        public static UiTokens Load(UiBuildConfig cfg, UiBuildReport report)
        {
            string path = cfg.Abs(cfg.TokensPath);
            if (!File.Exists(path)) return new UiTokens();      // токены опциональны
            return Parse(File.ReadAllText(path), cfg.TokensPath, report);
        }

        /// <summary>Разбор текста tokens.json. Отдельно от <see cref="Load"/>, чтобы тесты гоняли ровно
        /// тот же разбор, что и сборка, без файла на диске. <paramref name="where"/> — имя файла для отчёта.</summary>
        public static UiTokens Parse(string json, string where, UiBuildReport report)
        {
            var tokens = new UiTokens();
            object root;
            try { root = UiJson.Parse(json); }
            catch (UiJsonException e)
            {
                report.Error(where, "битый JSON: " + e.Message);
                return tokens;
            }
            if (!(root is Dictionary<string, object> map))
            {
                report.Error(where, "корень должен быть объектом");
                return tokens;
            }

            var r = new UiReader(map, report, where);
            Dictionary<string, object> colors = r.Map("colors");
            if (colors != null)
            {
                foreach (KeyValuePair<string, object> kv in colors)
                {
                    if (kv.Value is string hex) tokens._colors[kv.Key] = hex;
                    else report.Error(where, $"colors.{kv.Key} должен быть строкой '#RRGGBB'");
                }
            }

            Dictionary<string, object> styles = r.Map("styles");
            if (styles != null)
            {
                foreach (KeyValuePair<string, object> kv in styles)
                {
                    if (!(kv.Value is Dictionary<string, object> sm))
                    {
                        report.Error(where, $"styles.{kv.Key} должен быть объектом");
                        continue;
                    }
                    var sr = new UiReader(sm, report, where + ".styles." + kv.Key);
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
                        Material = sr.Str("material"),
                    };
                    ReadGradient(sr, style, report);
                    sr.Finish();
                    tokens._styles[kv.Key] = style;
                }
            }
            r.Finish();
            return tokens;
        }

        /// <summary>`gradient: { top, bottom }` — только парой: половина градиента в TMP значит «вторая
        /// половина белая», то есть молча другой цвет, а не отсутствие градиента.</summary>
        static void ReadGradient(UiReader sr, UiTextStyle style, UiBuildReport report)
        {
            UiReader g = sr.Sub("gradient");
            if (g == null) return;
            style.GradientTop = g.Str("top");
            style.GradientBottom = g.Str("bottom");
            g.Finish("у gradient есть только 'top' и 'bottom'");
            if (string.IsNullOrEmpty(style.GradientTop) || string.IsNullOrEmpty(style.GradientBottom))
            {
                report.Error(g.Where, "gradient задаётся парой: нужны и 'top', и 'bottom' (цвет-токены)");
                style.GradientTop = style.GradientBottom = null;
            }
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

        public Material Material(string path, UiBuildReport report, string where)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_materialCache.TryGetValue(path, out Material cached)) return cached;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) report.Error(where, $"материал не найден: {path}");
            _materialCache[path] = material;
            return material;
        }
    }
}
