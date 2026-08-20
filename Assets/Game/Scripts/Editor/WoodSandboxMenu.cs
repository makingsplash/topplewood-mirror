using Gameplay.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Editor
{
    /// <summary>
    /// Запуск песочницы деревянного стиля одним кликом: <c>Tools → Wood Style → Песочница ▶</c>.
    ///
    /// Открывает ПУСТУЮ сцену (в памяти, на диск ничего не пишется), кладёт в неё
    /// <see cref="WoodSandbox"/> и входит в Play. По выходу из Play возвращает ту сцену, которая
    /// была открыта до этого, — чтобы не приходилось руками искать ShapeFillBringup обратно.
    ///
    /// Почему не сцена-ассет: её пришлось бы держать в репозитории и вносить в Build Settings, а
    /// песочница нужна ровно на время настройки. Плюс так она не мешает открытому проекту.
    /// </summary>
    public static class WoodSandboxMenu
    {
        private const string ReturnSceneKey = "wood_sandbox_return_scene";

        [MenuItem("Tools/Wood Style/Песочница ▶", priority = 0)]
        public static void OpenSandbox()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }

            // Спросит сохранить, если в текущей сцене есть правки. Отказ — выходим, ничего не трогая.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var current = SceneManager.GetActiveScene().path;
            EditorPrefs.SetString(ReturnSceneKey, current);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("WoodSandbox", typeof(WoodSandbox));

            EditorApplication.isPlaying = true;
        }

        [MenuItem("Tools/Wood Style/Песочница ▶", validate = true)]
        private static bool CanOpenSandbox() => !EditorApplication.isCompiling;

        /// <summary>
        /// Каталог ВСЕХ деталей кампании — тем же способом, что и песочница: пустая сцена в памяти,
        /// компонент, Play, возврат прежней сцены на выходе. Отличие только в том, какой компонент
        /// кладём: <see cref="WoodCatalogSandbox"/> читает <c>shapefill_levels.json</c> и показывает
        /// настоящие детали настоящих размеров, страница на уровень.
        /// </summary>
        [MenuItem("Tools/Wood Style/Каталог деталей ▶", priority = 1)]
        public static void OpenCatalog()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorPrefs.SetString(ReturnSceneKey, SceneManager.GetActiveScene().path);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("WoodCatalog", typeof(WoodCatalogSandbox));

            EditorApplication.isPlaying = true;
        }

        [MenuItem("Tools/Wood Style/Каталог деталей ▶", validate = true)]
        private static bool CanOpenCatalog() => !EditorApplication.isCompiling;

        [InitializeOnLoadMethod]
        private static void HookPlaymodeExit()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
                return;

            var path = EditorPrefs.GetString(ReturnSceneKey, string.Empty);
            EditorPrefs.DeleteKey(ReturnSceneKey);

            // Пусто — значит Play запускали не из песочницы (или до этого была несохранённая сцена):
            // возвращать нечего, оставляем как есть.
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                return;

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }
    }
}
