using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Iliai.UiBuild.Handlers
{
    /// <summary>
    /// Хэндлеры переносимого ядра — ТОЛЬКО чистый UGUI. Здесь не должно появиться ни одного типа,
    /// зависящего от фреймворка конкретного проекта: анимации показа/скрытия, safe-area, локализация,
    /// свои контроллеры состояний живут в проектных хэндлерах (PORTING.md).
    /// </summary>
    [UiHandler("Button")]
    public sealed class ButtonHandler : IUiBehaviourHandler
    {
        public IEnumerable<Type> ManagedTypes => new[] { typeof(Button) };

        public void Apply(UiBuildContext ctx, GameObject target, UiBehaviourSpec spec)
        {
            UiReader r = spec.Reader(ctx.Report);
            bool interactable = r.Bool("interactable", true);
            string transition = r.Str("transition", "colorTint");
            r.Finish();

            var button = target.GetComponent<Button>() ?? target.AddComponent<Button>();
            button.interactable = interactable;

            var graphic = target.GetComponent<Graphic>();
            if (graphic == null)
            {
                // Кнопка без Graphic не ловит тапы вообще — это не стилистика, это неработающая кнопка.
                ctx.Report.Error(spec.Where, "behaviour 'Button' на узле без Image/текста: нечему принимать тап. Добавь узлу sprite/color");
                return;
            }
            graphic.raycastTarget = true;
            button.targetGraphic = graphic;

            switch (transition)
            {
                case "none": button.transition = Selectable.Transition.None; break;
                case "colorTint": button.transition = Selectable.Transition.ColorTint; break;
                default: ctx.Report.Error(spec.Where, $"Button.transition '{transition}' — допустимо none | colorTint"); break;
            }
        }
    }

    [UiHandler("ContentSizeFitter")]
    public sealed class ContentSizeFitterHandler : IUiBehaviourHandler
    {
        public IEnumerable<Type> ManagedTypes => new[] { typeof(ContentSizeFitter) };

        public void Apply(UiBuildContext ctx, GameObject target, UiBehaviourSpec spec)
        {
            UiReader r = spec.Reader(ctx.Report);
            string h = r.Str("horizontal", "unconstrained");
            string v = r.Str("vertical", "preferred");
            r.Finish();

            var fitter = target.GetComponent<ContentSizeFitter>() ?? target.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ParseMode(h, ctx, spec.Where);
            fitter.verticalFit = ParseMode(v, ctx, spec.Where);
        }

        static ContentSizeFitter.FitMode ParseMode(string raw, UiBuildContext ctx, string where)
        {
            switch (raw)
            {
                case "unconstrained": return ContentSizeFitter.FitMode.Unconstrained;
                case "min": return ContentSizeFitter.FitMode.MinSize;
                case "preferred": return ContentSizeFitter.FitMode.PreferredSize;
                default:
                    ctx.Report.Error(where, $"ContentSizeFitter '{raw}' — допустимо unconstrained | min | preferred");
                    return ContentSizeFitter.FitMode.Unconstrained;
            }
        }
    }

    [UiHandler("LayoutElement")]
    public sealed class LayoutElementHandler : IUiBehaviourHandler
    {
        public IEnumerable<Type> ManagedTypes => new[] { typeof(LayoutElement) };

        public void Apply(UiBuildContext ctx, GameObject target, UiBehaviourSpec spec)
        {
            UiReader r = spec.Reader(ctx.Report);
            float minW = r.Float("minWidth", -1f);
            float minH = r.Float("minHeight", -1f);
            float prefW = r.Float("preferredWidth", -1f);
            float prefH = r.Float("preferredHeight", -1f);
            float flexW = r.Float("flexibleWidth", -1f);
            float flexH = r.Float("flexibleHeight", -1f);
            bool ignore = r.Bool("ignoreLayout");
            r.Finish();

            var element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.minWidth = minW;
            element.minHeight = minH;
            element.preferredWidth = prefW;
            element.preferredHeight = prefH;
            element.flexibleWidth = flexW;
            element.flexibleHeight = flexH;
            element.ignoreLayout = ignore;
        }
    }

    [UiHandler("RectMask2D")]
    public sealed class RectMask2DHandler : IUiBehaviourHandler
    {
        public IEnumerable<Type> ManagedTypes => new[] { typeof(RectMask2D) };

        public void Apply(UiBuildContext ctx, GameObject target, UiBehaviourSpec spec)
        {
            UiReader r = spec.Reader(ctx.Report);
            float softness = r.Float("softness", 0f);
            r.Finish();

            var mask = target.GetComponent<RectMask2D>() ?? target.AddComponent<RectMask2D>();
            int s = Mathf.Max(0, Mathf.RoundToInt(softness));
            mask.softness = new Vector2Int(s, s);
        }
    }

    [UiHandler("CanvasGroup")]
    public sealed class CanvasGroupHandler : IUiBehaviourHandler
    {
        public IEnumerable<Type> ManagedTypes => new[] { typeof(CanvasGroup) };

        public void Apply(UiBuildContext ctx, GameObject target, UiBehaviourSpec spec)
        {
            UiReader r = spec.Reader(ctx.Report);
            float alpha = r.Float("alpha", 1f);
            bool interactable = r.Bool("interactable", true);
            bool blocksRaycasts = r.Bool("blocksRaycasts", true);
            r.Finish();

            var group = target.GetComponent<CanvasGroup>() ?? target.AddComponent<CanvasGroup>();
            group.alpha = Mathf.Clamp01(alpha);
            group.interactable = interactable;
            group.blocksRaycasts = blocksRaycasts;
        }
    }

    [UiHandler("ScrollRect")]
    public sealed class ScrollRectHandler : IUiBehaviourHandler
    {
        public IEnumerable<Type> ManagedTypes => new[] { typeof(ScrollRect) };

        public void Apply(UiBuildContext ctx, GameObject target, UiBehaviourSpec spec)
        {
            UiReader r = spec.Reader(ctx.Report);
            string dir = r.Str("dir", "vertical");
            string contentId = r.Str("content");
            float sensitivity = r.Float("sensitivity", 1f);
            string movement = r.Str("movement", "elastic");
            r.Finish();

            var scroll = target.GetComponent<ScrollRect>() ?? target.AddComponent<ScrollRect>();
            scroll.horizontal = dir == "horizontal" || dir == "both";
            scroll.vertical = dir == "vertical" || dir == "both";
            if (dir != "horizontal" && dir != "vertical" && dir != "both")
                ctx.Report.Error(spec.Where, $"ScrollRect.dir '{dir}' — допустимо horizontal | vertical | both");
            scroll.scrollSensitivity = sensitivity;

            switch (movement)
            {
                case "elastic": scroll.movementType = ScrollRect.MovementType.Elastic; break;
                case "clamped": scroll.movementType = ScrollRect.MovementType.Clamped; break;
                case "unrestricted": scroll.movementType = ScrollRect.MovementType.Unrestricted; break;
                default: ctx.Report.Error(spec.Where, $"ScrollRect.movement '{movement}' — допустимо elastic | clamped | unrestricted"); break;
            }

            if (string.IsNullOrEmpty(contentId))
            {
                ctx.Report.Error(spec.Where, "ScrollRect без 'content': скролл без content-объекта не работает. Укажи id ребёнка-контейнера");
                return;
            }
            Transform content = target.transform.Find(contentId);
            if (content == null)
            {
                ctx.Report.Error(spec.Where, $"ScrollRect.content '{contentId}': такого ребёнка у узла нет");
                return;
            }
            scroll.content = content as RectTransform;
            scroll.viewport = target.GetComponent<RectTransform>();
        }
    }
}
