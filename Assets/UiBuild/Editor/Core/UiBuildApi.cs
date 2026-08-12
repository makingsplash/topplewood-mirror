using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace Iliai.UiBuild
{
    /// <summary>
    /// Единственная точка входа для всех потребителей: меню, файловый эндпоинт для агента, тесты.
    /// Всё, что снаружи — тонкие обёртки над этими методами, чтобы человек и агент запускали
    /// РОВНО одну и ту же сборку (иначе «у меня собралось» и «у агента не собралось» становятся
    /// разными процессами, и расхождение никто не ловит).
    /// </summary>
    public static class UiBuildApi
    {
        public static UiBuildReport BuildAll()
        {
            var report = new UiBuildReport();
            UiBuildConfig cfg = UiBuildConfig.Load(report);
            var ctx = new UiBuildContext(cfg, UiTokens.Load(cfg, report), report);

            List<string> specs = new List<string>();
            specs.AddRange(FindSpecs(cfg, "components"));        // компоненты раньше экранов: экраны на них ссылаются
            specs.AddRange(FindSpecs(cfg, "screens"));

            if (specs.Count == 0)
            {
                report.Note($"спек не найдено в {cfg.DesignRoot}/components и {cfg.DesignRoot}/screens");
                return report;
            }

            int built = 0;
            foreach (string rel in specs)
            {
                UiSpec spec = UiSpec.Load(cfg.Abs(rel), rel, report);
                if (spec == null) continue;
                UiBuildResult result = UiPrefabBuilder.Build(spec, ctx);
                if (!result.Saved) continue;
                built++;
                UiLayoutValidator.Validate(result.PrefabPath, rel, cfg, report);
            }
            report.Note($"собрано префабов: {built} из {specs.Count}");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return report;
        }

        public static UiBuildReport BuildOne(string specRef)
        {
            var report = new UiBuildReport();
            UiBuildConfig cfg = UiBuildConfig.Load(report);
            string rel = ResolveSpecRef(cfg, specRef, report);
            if (rel == null) return report;

            var ctx = new UiBuildContext(cfg, UiTokens.Load(cfg, report), report);
            UiSpec spec = UiSpec.Load(cfg.Abs(rel), rel, report);
            if (spec == null) return report;

            UiBuildResult result = UiPrefabBuilder.Build(spec, ctx);
            // Геометрию проверяем ПОСЛЕ записи, на подмостках с Canvas: раскладку групп иначе не посчитать.
            if (result.Saved) UiLayoutValidator.Validate(result.PrefabPath, rel, cfg, report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return report;
        }

        /// <summary>Рендер по спеке: собранный префаб → PNG. flat = серый wireframe для генератора арта.</summary>
        public static UiBuildReport Render(string specRef, bool flat, string outRel)
        {
            var report = new UiBuildReport();
            UiBuildConfig cfg = UiBuildConfig.Load(report);
            string rel = ResolveSpecRef(cfg, specRef, report);
            if (rel == null) return report;

            UiSpec spec = UiSpec.Load(cfg.Abs(rel), rel, report);
            if (spec == null) return report;

            string prefabPath = $"{cfg.PrefabRootFor(spec.Kind)}/{spec.Name}.prefab";
            string output = string.IsNullOrEmpty(outRel)
                ? $"{cfg.RenderRoot}/{spec.Name}{(flat ? ".wire" : "")}.png"
                : outRel;

            UiRenderer.Render(prefabPath, cfg.Abs(output), flat, cfg, report);
            return report;
        }

        /// <summary>Каталог того, что компилятор реально умеет. Роль читает его, а не догадывается.</summary>
        public static string CatalogText()
        {
            var report = new UiBuildReport();
            UiBuildConfig cfg = UiBuildConfig.Load(report);
            var tokens = UiTokens.Load(cfg, report);
            var ctx = new UiBuildContext(cfg, tokens, report);

            var sb = new StringBuilder();
            sb.Append("anchors: ").Append(UiAnchor.KnownList()).Append('\n');
            sb.Append("behaviour: ").Append(UiBehaviourRegistry.KnownList()).Append('\n');
            sb.Append("post-build: ");
            var steps = new List<string>();
            foreach (IUiPostBuildStep s in UiBehaviourRegistry.PostBuildSteps) steps.Add(s.GetType().Name);
            sb.Append(steps.Count == 0 ? "—" : string.Join(", ", steps)).Append('\n');
            sb.Append("colors: ").Append(Join(tokens.KnownColors())).Append('\n');
            sb.Append("styles: ").Append(Join(tokens.KnownStyles())).Append('\n');
            sb.Append("prefabs: ").Append(Join(ctx.KnownPrefabs())).Append('\n');
            sb.Append("spriteRoot: ").Append(cfg.SpriteRoot).Append('\n');
            sb.Append("screens → ").Append(cfg.ScreenPrefabRoot).Append('\n');
            sb.Append("components → ").Append(cfg.ComponentPrefabRoot).Append('\n');
            sb.Append("referenceResolution: ").Append(cfg.ReferenceResolution.x).Append('×').Append(cfg.ReferenceResolution.y).Append('\n');
            sb.Append("specs: ").Append(Join(AllSpecs(cfg))).Append('\n');
            if (!report.Ok) sb.Append(report.ToText());
            return sb.ToString();
        }

        static string Join(IEnumerable<string> items)
        {
            var list = new List<string>(items);
            list.Sort();
            return list.Count == 0 ? "—" : string.Join(", ", list);
        }

        static IEnumerable<string> AllSpecs(UiBuildConfig cfg)
        {
            var all = new List<string>();
            all.AddRange(FindSpecs(cfg, "screens"));
            all.AddRange(FindSpecs(cfg, "components"));
            return all;
        }

        static List<string> FindSpecs(UiBuildConfig cfg, string subdir)
        {
            var found = new List<string>();
            string abs = cfg.Abs(cfg.DesignRoot + "/" + subdir);
            if (!Directory.Exists(abs)) return found;
            foreach (string file in Directory.GetFiles(abs, "*.json", SearchOption.AllDirectories))
            {
                string rel = file.Replace('\\', '/').Substring(cfg.ProjectRoot.Length + 1);
                found.Add(rel);
            }
            found.Sort();
            return found;
        }

        /// <summary>Принимает и полный путь спеки, и короткое имя — роль не должна помнить раскладку папок.</summary>
        static string ResolveSpecRef(UiBuildConfig cfg, string specRef, UiBuildReport report)
        {
            if (string.IsNullOrEmpty(specRef))
            {
                report.Error("—", "не сказано, какую спеку собирать");
                return null;
            }

            string direct = specRef.EndsWith(".json") ? specRef : specRef + ".json";
            if (File.Exists(cfg.Abs(direct))) return direct;

            foreach (string candidate in AllSpecs(cfg))
            {
                if (candidate == direct) return candidate;
                if (Path.GetFileNameWithoutExtension(candidate) == Path.GetFileNameWithoutExtension(specRef)) return candidate;
            }

            report.Error(specRef, $"спека не найдена. Известные: {Join(AllSpecs(cfg))}");
            return null;
        }
    }
}
