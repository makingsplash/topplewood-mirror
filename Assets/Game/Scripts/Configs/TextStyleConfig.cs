using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace Game.Configs
{
    [CreateAssetMenu(fileName = "TextStyleConfig", menuName = "Configs/TextStyleConfig")]
    public sealed class TextStyleConfig : ScriptableObject
    {
        [InfoBox(
            "FontMaterial используется как шаблон только для чтения: шейдерные свойства копируются в " +
            "per-instance материал каждого TMP-компонента через CopyPropertiesFromMaterial.\n\n" +
            "• Один материал можно использовать в нескольких стилях — в рантайме он не изменяется.\n" +
            "• Стили с одним материалом получат одинаковые шейдерные свойства (outline, glow, dilate и т.д.);\n" +
            "  различаться могут только Color (vertex color) и FontSize.\n" +
            "• _MainTex всегда перезаписывается атласом шрифта компонента, поэтому текстура в материале не важна.")]
        [TableList(AlwaysExpanded = true)]
        [SerializeField] private List<TextStyle> _styles;

        public bool TryGetStyle(string styleName, out TextStyle style)
        {
            if (_styles != null)
            {
                foreach (var s in _styles)
                {
                    if (s.Name == styleName)
                    {
                        style = s;
                        return true;
                    }
                }
            }

            style = default;
            return false;
        }

        public IEnumerable<string> GetStyleNames()
        {
            if (_styles == null) yield break;
            foreach (var s in _styles)
                yield return s.Name;
        }

        [Serializable]
        public class TextStyle
        {
            public string Name;
            [Tooltip("Описание стиля — назначение и где используется.")]
            [Multiline(2)] public string Description;
            [Tooltip("TMP_FontAsset для замены шрифта. Если не задан, шрифт компонента не меняется.")]
            public TMP_FontAsset Font;
            [Tooltip("Шаблон шейдерных свойств (outline, glow, face dilate и т.д.). " +
                     "Можно переиспользовать в нескольких стилях — в рантайме не изменяется. " +
                     "Если не задан, применяются только FontSize и Color.")]
            public Material FontMaterial;
            [Tooltip("Размер шрифта в пунктах. 0 = не изменяется (остаётся значение, заданное на TMP-компоненте).")]
            [Min(0)] public float FontSize = 10f;
            [Tooltip("Цвет текста (vertex color). Перемножается с Face Color материала в шейдере. " +
                     "Может различаться у стилей, использующих один FontMaterial.")]
            public Color Color = Color.white;
            [Tooltip("Расстояние между символами в единицах TMP. 0 = не изменяется.")]
            public float CharacterSpacing = 0f;
        }
    }
}
