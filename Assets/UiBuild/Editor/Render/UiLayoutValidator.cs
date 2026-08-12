using TMPro;
using UnityEngine;

namespace Iliai.UiBuild
{
    /// <summary>
    /// Проверки, которые невозможно сделать по тексту спеки — только по посчитанной раскладке.
    ///
    /// Роль верстает вслепую: она не увидит, что заголовок не влез в свой бокс и уехал за панель.
    /// Поэтому измеряем сами и пишем в отчёт число «не хватило N px» — это превращает визуальный
    /// дефект в строку, по которой можно починить спеку.
    ///
    /// Меряем ТОЛЬКО на подмостках с Canvas: без него layout-группы не считаются, и любые размеры
    /// внутри групп — дефолтные, а не настоящие.
    /// </summary>
    public static class UiLayoutValidator
    {
        public static void Validate(string prefabPath, string specPath, UiBuildConfig cfg, UiBuildReport report)
        {
            UiRenderer.Stage stage = UiRenderer.CreateStage(prefabPath, cfg, report, false);
            if (stage == null) return;
            try { CheckTexts(stage.Instance, report, specPath); }
            finally { stage.Dispose(); }
        }

        static void CheckTexts(GameObject root, UiBuildReport report, string specPath)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                text.ForceMeshUpdate();
                Rect box = text.rectTransform.rect;
                if (box.width < 1f || box.height < 1f) continue;      // размер даёт ContentSizeFitter — мерить нечего

                string where = $"{specPath}:{text.gameObject.name}";

                if (text.textWrappingMode == TextWrappingModes.NoWrap && text.preferredWidth > box.width + 1f)
                {
                    int over = Mathf.CeilToInt(text.preferredWidth - box.width);
                    report.Warn(where, $"текст шире своего бокса на {over} px (нужно {Mathf.CeilToInt(text.preferredWidth)}, есть {Mathf.FloorToInt(box.width)}) — включи wrap, уменьши кегль или расширь узел. Локализация сделает строку ещё длиннее");
                }

                if (text.maxVisibleLines == 0 && text.preferredHeight > box.height + 1f)
                {
                    int over = Mathf.CeilToInt(text.preferredHeight - box.height);
                    report.Warn(where, $"текст выше своего бокса на {over} px — часть строк не увидят. Дай высоту под нужное число строк либо ограничь maxLines");
                }
            }
        }
    }
}
