using System.Collections.Generic;
using UnityEngine;

namespace Iliai.UiBuild
{
    public enum UiAnchorKind
    {
        None,
        Stretch,          // оба измерения от родителя
        StretchTop,       // растянут по X, фиксированная height
        StretchBottom,
        StretchLeft,      // растянут по Y, фиксированная width
        StretchRight,
        Point,            // точка привязки + anchoredPosition
    }

    /// <summary>
    /// Пресеты привязки. Вся арифметика RectTransform живёт ЗДЕСЬ и нигде больше: пресет — это
    /// договор между спекой и движком, и он обязан читаться одинаково в префабе и в wireframe.
    /// </summary>
    public static class UiAnchor
    {
        struct Preset
        {
            public Vector2 Min, Max;
            public UiAnchorKind Kind;
        }

        static readonly Dictionary<string, Preset> Presets = new Dictionary<string, Preset>
        {
            { "stretch",        new Preset { Min = new Vector2(0, 0),     Max = new Vector2(1, 1),     Kind = UiAnchorKind.Stretch } },
            { "top-stretch",    new Preset { Min = new Vector2(0, 1),     Max = new Vector2(1, 1),     Kind = UiAnchorKind.StretchTop } },
            { "bottom-stretch", new Preset { Min = new Vector2(0, 0),     Max = new Vector2(1, 0),     Kind = UiAnchorKind.StretchBottom } },
            { "left-stretch",   new Preset { Min = new Vector2(0, 0),     Max = new Vector2(0, 1),     Kind = UiAnchorKind.StretchLeft } },
            { "right-stretch",  new Preset { Min = new Vector2(1, 0),     Max = new Vector2(1, 1),     Kind = UiAnchorKind.StretchRight } },
            { "center",         new Preset { Min = new Vector2(0.5f, 0.5f), Max = new Vector2(0.5f, 0.5f), Kind = UiAnchorKind.Point } },
            { "top-left",       new Preset { Min = new Vector2(0, 1),     Max = new Vector2(0, 1),     Kind = UiAnchorKind.Point } },
            { "top-center",     new Preset { Min = new Vector2(0.5f, 1),  Max = new Vector2(0.5f, 1),  Kind = UiAnchorKind.Point } },
            { "top-right",      new Preset { Min = new Vector2(1, 1),     Max = new Vector2(1, 1),     Kind = UiAnchorKind.Point } },
            { "bottom-left",    new Preset { Min = new Vector2(0, 0),     Max = new Vector2(0, 0),     Kind = UiAnchorKind.Point } },
            { "bottom-center",  new Preset { Min = new Vector2(0.5f, 0),  Max = new Vector2(0.5f, 0),  Kind = UiAnchorKind.Point } },
            { "bottom-right",   new Preset { Min = new Vector2(1, 0),     Max = new Vector2(1, 0),     Kind = UiAnchorKind.Point } },
            { "left-center",    new Preset { Min = new Vector2(0, 0.5f),  Max = new Vector2(0, 0.5f),  Kind = UiAnchorKind.Point } },
            { "right-center",   new Preset { Min = new Vector2(1, 0.5f),  Max = new Vector2(1, 0.5f),  Kind = UiAnchorKind.Point } },
        };

        public static string KnownList()
        {
            var keys = new List<string>(Presets.Keys);
            keys.Sort();
            return string.Join(", ", keys);
        }

        public static UiAnchorKind KindOf(string anchor)
        {
            if (string.IsNullOrEmpty(anchor)) return UiAnchorKind.None;
            return Presets.TryGetValue(anchor, out Preset p) ? p.Kind : UiAnchorKind.None;
        }

        /// <summary>
        /// Применяет пресет к RectTransform. Возвращает false, если раскладка не задана корректно —
        /// тогда билдер оставляет узел на дефолтном поведении из таблицы размеров.
        /// </summary>
        public static bool Apply(RectTransform rt, UiNode node, UiBuildReport report)
        {
            if (string.IsNullOrEmpty(node.Anchor)) return false;
            if (!Presets.TryGetValue(node.Anchor, out Preset preset))
            {
                report.Error(node.Where, $"неизвестный anchor '{node.Anchor}'. Известные: {KnownList()}");
                return false;
            }

            UiOffset off = node.Offset ?? new UiOffset();
            int? width = node.Width ?? (node.Size.HasValue ? node.Size.Value.x : (int?)null);
            int? height = node.Height ?? (node.Size.HasValue ? node.Size.Value.y : (int?)null);

            // Ключи offset обязаны соответствовать типу анкера. В родственных реализациях
            // несоответствие игнорировалось МОЛЧА, и вёрстка «почему-то в углу» отлаживалась глазами.
            if (preset.Kind == UiAnchorKind.Point && off.HasEdges)
                report.Error(node.Where, $"anchor '{node.Anchor}' — точечный, у него offset задаётся {{x, y}}; ключи left/right/top/bottom здесь не работают");
            if (preset.Kind != UiAnchorKind.Point && off.HasPoint)
                report.Error(node.Where, $"anchor '{node.Anchor}' — растягивающий, у него offset задаётся отступами left/right/top/bottom; ключи x/y здесь не работают");

            rt.anchorMin = preset.Min;
            rt.anchorMax = preset.Max;

            switch (preset.Kind)
            {
                case UiAnchorKind.Stretch:
                    if (width.HasValue || height.HasValue)
                        report.Warn(node.Where, "anchor 'stretch' растягивает узел на весь родитель — size/width/height здесь не действуют");
                    rt.offsetMin = new Vector2(off.Left, off.Bottom);
                    rt.offsetMax = new Vector2(-off.Right, -off.Top);
                    break;

                case UiAnchorKind.StretchTop:
                    if (!height.HasValue) { report.Error(node.Where, "anchor 'top-stretch' требует 'height' (или size.h)"); return false; }
                    rt.offsetMin = new Vector2(off.Left, -off.Top - height.Value);
                    rt.offsetMax = new Vector2(-off.Right, -off.Top);
                    break;

                case UiAnchorKind.StretchBottom:
                    if (!height.HasValue) { report.Error(node.Where, "anchor 'bottom-stretch' требует 'height' (или size.h)"); return false; }
                    rt.offsetMin = new Vector2(off.Left, off.Bottom);
                    rt.offsetMax = new Vector2(-off.Right, off.Bottom + height.Value);
                    break;

                case UiAnchorKind.StretchLeft:
                    if (!width.HasValue) { report.Error(node.Where, "anchor 'left-stretch' требует 'width' (или size.w)"); return false; }
                    rt.offsetMin = new Vector2(off.Left, off.Bottom);
                    rt.offsetMax = new Vector2(off.Left + width.Value, -off.Top);
                    break;

                case UiAnchorKind.StretchRight:
                    if (!width.HasValue) { report.Error(node.Where, "anchor 'right-stretch' требует 'width' (или size.w)"); return false; }
                    rt.offsetMin = new Vector2(-off.Right - width.Value, off.Bottom);
                    rt.offsetMax = new Vector2(-off.Right, -off.Top);
                    break;

                case UiAnchorKind.Point:
                    // Pivot едет за анкером: тогда offset читается как отступ ОТ ЭТОГО УГЛА, а не как
                    // смещение центра — иначе `top-right` + {x:-24,y:-24} ставил бы элемент серединой
                    // на угол и половину его выносило бы за экран.
                    rt.pivot = node.Pivot ?? preset.Min;
                    if (width.HasValue || height.HasValue)
                    {
                        rt.sizeDelta = new Vector2(
                            width ?? rt.sizeDelta.x,
                            height ?? rt.sizeDelta.y);
                    }
                    rt.anchoredPosition = new Vector2(off.X, off.Y);
                    break;
            }

            if (node.Pivot.HasValue && preset.Kind != UiAnchorKind.Point) rt.pivot = node.Pivot.Value;
            return true;
        }
    }
}
