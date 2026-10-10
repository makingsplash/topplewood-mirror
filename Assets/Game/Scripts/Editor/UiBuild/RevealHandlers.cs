using System;
using System.Collections.Generic;
using DG.Tweening;
using Iliai.UiBuild;
using Ui.Components.Reveal;
using UnityEngine;

namespace Game.Editor.UiBuild
{
    /// <summary>
    /// `behaviour: Reveal` — the show-timeline of ONE node (TASK-425). Project handler, not core: the runtime is
    /// DOTween + the SayKit-stack IWindowAnimator. Replaces the node's <see cref="RevealTrack"/> wholesale, so a
    /// rebuild of an existing root never accumulates steps.
    /// <code>
    /// { "type": "Reveal", "skipShift": 450, "unlocksInput": false, "steps": [
    ///   { "kind": "in",    "at": 80,   "dur": 300, "scale": 0.5, "alpha": 0, "alphaDur": 120, "y": -80, "ease": "OutBack", "overshoot": 1.70158 },
    ///   { "kind": "count", "at": 560,  "dur": 550, "from": 0, "ease": "OutQuad" },
    ///   { "kind": "punch", "at": 1110, "dur": 140, "scale": 1.12 },
    ///   { "kind": "loop",  "at": 2000, "period": 2400, "scale": 1.04, "alpha": 0.85, "ease": "InOutSine" } ] }
    /// </code>
    /// Field semantics — docs/art/ui-authoring.md §«Анимация показа экрана».
    /// </summary>
    [UiHandler("Reveal")]
    public sealed class RevealHandler : IUiBehaviourHandler
    {
        public IEnumerable<Type> ManagedTypes => new[] { typeof(RevealTrack) };

        public void Apply(UiBuildContext ctx, GameObject target, UiBehaviourSpec spec)
        {
            UiReader r = spec.Reader(ctx.Report);
            int skipShift = r.Int("skipShift", 0);
            bool unlocksInput = r.Bool("unlocksInput");
            List<object> rawSteps = r.List("steps");
            r.Finish();

            if (rawSteps == null || rawSteps.Count == 0)
            {
                ctx.Report.Error(spec.Where, "Reveal без 'steps': трек без шагов ничего не анимирует");
                return;
            }
            if (skipShift < 0)
                ctx.Report.Error(spec.Where, "Reveal.skipShift < 0: сдвиг при пропуске узла тянет таймлайн только РАНЬШЕ");

            var steps = new List<RevealStep>();
            for (int i = 0; i < rawSteps.Count; i++)
            {
                string where = $"{spec.Where}.steps[{i}]";
                if (!(rawSteps[i] is Dictionary<string, object> map))
                {
                    ctx.Report.Error(where, "шаг Reveal должен быть объектом");
                    continue;
                }
                RevealStep step = ReadStep(new UiReader(map, ctx.Report, where), ctx.Report, where);
                if (step != null) steps.Add(step);
            }

            var track = target.GetComponent<RevealTrack>() ?? target.AddComponent<RevealTrack>();
            track.Configure(steps, skipShift, unlocksInput);
        }

        static RevealStep ReadStep(UiReader r, UiBuildReport report, string where)
        {
            string kind = r.Str("kind", "in");
            var step = new RevealStep { AtMs = r.Int("at", 0) };

            switch (kind)
            {
                case "in": step.Kind = RevealKind.In; break;
                case "count": step.Kind = RevealKind.Count; break;
                case "punch": step.Kind = RevealKind.Punch; break;
                case "loop": step.Kind = RevealKind.Loop; break;
                default:
                    report.Error(where, $"Reveal.kind '{kind}' — допустимо in | count | punch | loop");
                    r.Finish();
                    return null;
            }

            // loop задаёт ПОЛНЫЙ цикл ключом 'period' — чтобы «длительность» и «период» не путались в спеке.
            step.DurationMs = step.Kind == RevealKind.Loop ? r.Int("period", 0) : r.Int("dur", 0);
            if (r.Has("scale")) { step.HasScale = true; step.Scale = r.Float("scale", 1f); }
            if (r.Has("alpha")) { step.HasAlpha = true; step.Alpha = r.Float("alpha", 1f); }
            if (r.Has("x") || r.Has("y"))
            {
                step.HasOffset = true;
                step.Offset = new Vector2(r.Int("x", 0), r.Int("y", 0));
            }
            step.AlphaDurationMs = r.Int("alphaDur", 0);
            step.Overshoot = r.Float("overshoot", 1.70158f);
            step.CountFrom = r.Float("from", 0f);

            string ease = r.Str("ease", "OutQuad");
            if (Enum.TryParse(ease, false, out Ease parsed) && parsed != Ease.INTERNAL_Zero && parsed != Ease.INTERNAL_Custom)
                step.Ease = parsed;
            else
                report.Error(where, $"Reveal.ease '{ease}' — имя кривой DOTween (OutQuad, OutBack, InOutSine, …)");
            r.Finish();

            if (step.DurationMs <= 0)
                report.Error(where, step.Kind == RevealKind.Loop ? "loop без 'period' > 0" : "шаг без 'dur' > 0");
            if (step.AtMs < 0)
                report.Error(where, "'at' < 0: таймлайн начинается в 0");
            switch (step.Kind)
            {
                case RevealKind.In when !step.HasScale && !step.HasAlpha && !step.HasOffset:
                    report.Error(where, "in без scale/alpha/x/y: шагу не из чего въезжать");
                    break;
                case RevealKind.Punch when !step.HasScale:
                    report.Error(where, "punch без 'scale' (пик удара, например 1.12)");
                    break;
                case RevealKind.Loop when !step.HasScale && !step.HasAlpha:
                    report.Error(where, "loop без scale/alpha: нечему дышать");
                    break;
                case RevealKind.Loop when step.HasOffset:
                    report.Error(where, "loop со сдвигом x/y не поддержан — только scale/alpha");
                    break;
            }
            if (step.AlphaDurationMs > 0 && step.Kind != RevealKind.In)
                report.Error(where, "'alphaDur' имеет смысл только у шага in");
            return step;
        }
    }

    /// <summary>`behaviour: ScreenReveal` on the screen ROOT: puts the <see cref="ScreenReveal"/> animator there
    /// (UiService plays it via IWindowAnimator). `inputLag` — ms after the last unlocking node lands;
    /// `tapToComplete` — a tap during the intro fast-forwards it (and never presses a button).</summary>
    [UiHandler("ScreenReveal")]
    public sealed class ScreenRevealHandler : IUiBehaviourHandler
    {
        public IEnumerable<Type> ManagedTypes => new[] { typeof(ScreenReveal) };

        public void Apply(UiBuildContext ctx, GameObject target, UiBehaviourSpec spec)
        {
            UiReader r = spec.Reader(ctx.Report);
            if (!r.Has("inputLag"))
                ctx.Report.Error(spec.Where, "ScreenReveal без 'inputLag' (мс после посадки последней кнопки до интерактива) — значение из дизайна экрана, дефолта нет");
            int inputLag = r.Int("inputLag", 0);
            bool tapToComplete = r.Bool("tapToComplete", true);
            r.Finish();

            if (target.transform.parent != null)
                ctx.Report.Error(spec.Where, "ScreenReveal ставится только на корень экрана — UiService ищет IWindowAnimator на корне");

            var reveal = target.GetComponent<ScreenReveal>() ?? target.AddComponent<ScreenReveal>();
            reveal.Configure(inputLag, tapToComplete);
        }
    }

    /// <summary>Tracks without a root animator are silently inert at runtime — make that a red build instead.</summary>
    public sealed class RevealConsistencyStep : IUiPostBuildStep
    {
        public int Order => 0;

        public void Run(UiBuildContext ctx, UiSpec spec, GameObject prefabRoot, string prefabPath)
        {
            bool hasTracks = prefabRoot.GetComponentsInChildren<RevealTrack>(true).Length > 0;
            bool hasAnimator = prefabRoot.GetComponent<ScreenReveal>() != null;
            if (hasTracks && !hasAnimator)
                ctx.Report.Error(spec.SourcePath, "есть узлы с behaviour 'Reveal', но на корне нет 'ScreenReveal' — анимация не сыграет. Добавь { \"type\": \"ScreenReveal\", \"inputLag\": … } в behaviour корня");
            if (hasAnimator && !hasTracks)
                ctx.Report.Warn(spec.SourcePath, "ScreenReveal на корне без единого узла 'Reveal' — экран появится мгновенно, но будет держать тапы до 0 мс");
        }
    }
}
