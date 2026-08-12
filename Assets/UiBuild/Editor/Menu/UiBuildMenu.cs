using System.IO;
using UnityEditor;
using UnityEngine;

namespace Iliai.UiBuild
{
    /// <summary>Ручные входы для человека. Внутри — те же вызовы <see cref="UiBuildApi"/>, что у агента.</summary>
    public static class UiBuildMenu
    {
        [MenuItem("Tools/UI Build/Собрать все спеки", priority = 0)]
        public static void BuildAll() => Log(UiBuildApi.BuildAll().ToText());

        [MenuItem("Tools/UI Build/Собрать выделенную спеку", priority = 1)]
        public static void BuildSelected()
        {
            string path = SelectedSpecPath();
            if (path == null)
            {
                Debug.LogWarning("[uibuild] выдели в Project джейсон спеки (design/screens/*.json или design/components/*.json)");
                return;
            }
            Log(UiBuildApi.BuildOne(path).ToText());
        }

        [MenuItem("Tools/UI Build/Рендер выделенной спеки (скриншот)", priority = 20)]
        public static void RenderSelected() => RenderSelectedInternal(false);

        [MenuItem("Tools/UI Build/Рендер выделенной спеки (wireframe)", priority = 21)]
        public static void RenderSelectedWireframe() => RenderSelectedInternal(true);

        [MenuItem("Tools/UI Build/Каталог возможностей", priority = 40)]
        public static void PrintCatalog() => Debug.Log("[uibuild] каталог:\n" + UiBuildApi.CatalogText());

        static void RenderSelectedInternal(bool flat)
        {
            string path = SelectedSpecPath();
            if (path == null)
            {
                Debug.LogWarning("[uibuild] выдели в Project джейсон спеки");
                return;
            }
            Log(UiBuildApi.Render(path, flat, null).ToText());
        }

        /// <summary>Путь выделенной спеки относительно корня проекта (спеки лежат ВНЕ Assets, поэтому
        /// через Selection.assetGUIDs их не выбрать — берём объектный путь либо путь из EditorPrefs-независимого поля).</summary>
        static string SelectedSpecPath()
        {
            foreach (Object obj in Selection.objects)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".json")) return assetPath;
            }
            return null;
        }

        static void Log(string reportText)
        {
            if (reportText.StartsWith("FAILED")) Debug.LogError("[uibuild]\n" + reportText);
            else Debug.Log("[uibuild]\n" + reportText);
        }
    }
}
