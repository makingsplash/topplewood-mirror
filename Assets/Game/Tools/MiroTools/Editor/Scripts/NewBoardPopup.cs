using UnityEditor;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace MiroTools.Editor
{
    public class NewBoardPopup : EditorWindow
    {
        private string _boardName = "New Board";
        private MiroToolsConfig _config;

        public static void ShowWindow(MiroToolsConfig config)
        {
            var window = GetWindow<NewBoardPopup>(true, "Create New Miro Board", true);
            window._config = config;
            window.minSize = window.maxSize = new Vector2(300, 100);
            
            // Центрируем окно относительно редактора
            var main = EditorGUIUtility.GetMainWindowPosition();
            var pos = window.position;
            float w = (main.width - pos.width) * 0.5f;
            float h = (main.height - pos.height) * 0.5f;
            window.position = new Rect(main.x + w, main.y + h, pos.width, pos.height);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(15);
            _boardName = EditorGUILayout.TextField("Board Name", _boardName);
            EditorGUILayout.Space(10);

            if (GUILayout.Button("Create Board", GUILayout.Height(30)))
            {
                CreateAndClose().Forget();
            }
        }

        private async UniTaskVoid CreateAndClose()
        {
            if (string.IsNullOrEmpty(_boardName)) return;

            var newId = await ApiClient.CreateBoard(_config.ApiToken, _boardName, _config.SelectedWorkspaceId);
            if (!string.IsNullOrEmpty(newId))
            {
                await _config.RefreshBoards();
                _config.BoardId = newId;
                EditorUtility.SetDirty(_config);
                AssetDatabase.SaveAssets();
            }
            Close();
        }
    }
}
