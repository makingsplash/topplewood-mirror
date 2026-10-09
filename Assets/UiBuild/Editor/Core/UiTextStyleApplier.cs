using TMPro;
using UnityEngine;

namespace Iliai.UiBuild
{
    /// <summary>
    /// Стиль типографики из tokens.json → TMP-текст. Отдельно от билдера, чтобы тесты гоняли РОВНО тот
    /// код, что и сборка, без записи префаба на диск.
    ///
    /// Порядок значим: шрифт раньше материала — атлас материала сверяется со шрифтом, который у текста
    /// БУДЕТ, а не с тем, что стоял до стиля (иначе ложный отказ при смене шрифта либо пропуск чужого атласа).
    /// </summary>
    public static class UiTextStyleApplier
    {
        public static void Apply(TMP_Text text, UiTextStyle style, UiTokens tokens, UiBuildReport report, string where)
        {
            if (text == null || style == null) return;

            text.fontSize = style.FontSize;
            text.fontStyle = (style.Bold ? FontStyles.Bold : FontStyles.Normal) | (style.Uppercase ? FontStyles.UpperCase : FontStyles.Normal);
            if (Mathf.Abs(style.LineSpacing) > 0.001f) text.lineSpacing = style.LineSpacing;
            if (Mathf.Abs(style.CharacterSpacing) > 0.001f) text.characterSpacing = style.CharacterSpacing;
            TMP_FontAsset font = tokens.Font(style.Font, report, where);
            if (font != null) text.font = font;

            ApplyMaterial(text, style, tokens, report, where);
            ApplyGradient(text, style, tokens, report, where);
        }

        /// <summary>
        /// Обводка, тень (underlay) и свечение TMP живут в МАТЕРИАЛЕ, а не в компоненте. Материал обязан
        /// сэмплить атлас ТОГО ЖЕ шрифта: иначе TMP при загрузке (TextMeshProUGUI.LoadFontAsset) молча
        /// возвращает дефолтный материал шрифта — в префабе обводки нет, а сборка зелёная. Поэтому здесь
        /// это ошибка с `файл:id`, а не тихая подмена.
        /// </summary>
        static void ApplyMaterial(TMP_Text text, UiTextStyle style, UiTokens tokens, UiBuildReport report, string where)
        {
            Material material = tokens.Material(style.Material, report, where);
            if (material == null) return;

            TMP_FontAsset font = text.font;
            if (font == null || font.atlasTexture == null)
            {
                report.Error(where, $"стиль '{style.Name}': material задан, а у текста нет шрифта с атласом — сверять материал не с чем. Задай 'font' в стиле");
                return;
            }

            Texture atlas = material.HasProperty(ShaderUtilities.ID_MainTex) ? material.GetTexture(ShaderUtilities.ID_MainTex) : null;
            if (atlas != font.atlasTexture)
            {
                report.Error(where, $"стиль '{style.Name}': материал '{style.Material}' собран под атлас '{(atlas == null ? "—" : atlas.name)}', а шрифт '{font.name}' — под '{font.atlasTexture.name}'. TMP молча вернул бы материал шрифта, и обводки бы не было. Материал делается из материала ЭТОГО шрифта (TMP: Create → Material Preset)");
                return;
            }

            text.fontSharedMaterial = material;
        }

        /// <summary>
        /// Вертикальный градиент заливки. TMP УМНОЖАЕТ градиент на `color` текста — `color` остаётся
        /// тинтом и фолбэком: под градиент его обычно оставляют белым.
        /// </summary>
        static void ApplyGradient(TMP_Text text, UiTextStyle style, UiTokens tokens, UiBuildReport report, string where)
        {
            if (string.IsNullOrEmpty(style.GradientTop)) return;
            if (!tokens.TryColor(style.GradientTop, report, where, out Color top)) return;
            if (!tokens.TryColor(style.GradientBottom, report, where, out Color bottom)) return;

            text.colorGradientPreset = null;                     // пресет перекрыл бы colorGradient
            text.colorGradient = new VertexGradient(top, top, bottom, bottom);
            text.enableVertexGradient = true;
        }
    }
}
