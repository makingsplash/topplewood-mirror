using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Iliai.UiBuild
{
    /// <summary>
    /// ЕДИНСТВЕННОЕ место, где живёт проектная специфика ядра: пути и reference resolution.
    /// Ядро не знает ни одного имени проекта — оно читает этот файл. Нет файла → дефолты + честная
    /// заметка в отчёте (чтобы «почему префаб уехал не туда» не выяснялось экспериментально).
    /// </summary>
    public sealed class UiBuildConfig
    {
        public const string DefaultPath = "design/config.json";

        public Vector2Int ReferenceResolution = new Vector2Int(1080, 1920);
        public string DesignRoot = "design";
        public string SpriteRoot = "Assets/UI/Design";
        public string ScreenPrefabRoot = "Assets/UI/Screens";
        public string ComponentPrefabRoot = "Assets/UI/Components";
        public string RenderRoot = "design/.render";
        public string TokensPath = "design/tokens.json";
        public string PrefabRegistryPath = "design/prefabs.json";

        public string ProjectRoot { get; private set; }
        public bool LoadedFromFile { get; private set; }

        public static string FindProjectRoot()
        {
            DirectoryInfo parent = Directory.GetParent(Application.dataPath);
            return parent != null ? parent.FullName.Replace('\\', '/') : Application.dataPath;
        }

        public string Abs(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return ProjectRoot;
            if (Path.IsPathRooted(relative)) return relative.Replace('\\', '/');
            return (ProjectRoot + "/" + relative).Replace('\\', '/');
        }

        public string PrefabRootFor(string kind) => kind == "component" ? ComponentPrefabRoot : ScreenPrefabRoot;

        public static UiBuildConfig Load(UiBuildReport report)
        {
            var cfg = new UiBuildConfig { ProjectRoot = FindProjectRoot() };
            string path = cfg.Abs(DefaultPath);
            if (!File.Exists(path))
            {
                report.Note($"{DefaultPath} не найден — работаю на дефолтах (спрайты: {cfg.SpriteRoot}, экраны: {cfg.ScreenPrefabRoot})");
                return cfg;
            }

            object root;
            try { root = UiJson.Parse(File.ReadAllText(path)); }
            catch (UiJsonException e)
            {
                report.Error(DefaultPath, "битый JSON: " + e.Message);
                return cfg;
            }

            if (!(root is Dictionary<string, object> map))
            {
                report.Error(DefaultPath, "корень конфига должен быть объектом");
                return cfg;
            }

            var r = new UiReader(map, report, DefaultPath);
            Vector2Int? refRes = r.SizeOpt("referenceResolution");
            if (refRes.HasValue) cfg.ReferenceResolution = refRes.Value;
            cfg.DesignRoot = r.Str("designRoot", cfg.DesignRoot);
            cfg.SpriteRoot = r.Str("spriteRoot", cfg.SpriteRoot);
            cfg.ScreenPrefabRoot = r.Str("screenPrefabRoot", cfg.ScreenPrefabRoot);
            cfg.ComponentPrefabRoot = r.Str("componentPrefabRoot", cfg.ComponentPrefabRoot);
            cfg.RenderRoot = r.Str("renderRoot", cfg.RenderRoot);
            cfg.TokensPath = r.Str("tokens", cfg.TokensPath);
            cfg.PrefabRegistryPath = r.Str("prefabs", cfg.PrefabRegistryPath);
            r.Finish();

            cfg.LoadedFromFile = true;
            if (cfg.ReferenceResolution.x <= 0 || cfg.ReferenceResolution.y <= 0)
            {
                report.Error(DefaultPath, "referenceResolution должен быть положительным");
                cfg.ReferenceResolution = new Vector2Int(1080, 1920);
            }
            return cfg;
        }
    }
}
