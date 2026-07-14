using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using UnityEditor;

using UnityEngine;

namespace SGS.Editor.Tools.DevTools
{
    public class ReferencesWindow : EditorWindow
    {
        public class ObjectWithReferences
        {
            public Object MainObject;
            public readonly List<Object> References = new();
        }

        private Vector2 _scrollPos;
        private List<ObjectWithReferences> _objects;

        [MenuItem("Assets/Find references #R")]
        static void FindReferences()
        {
            var objects = new List<ObjectWithReferences>();
            var processedGuids = new HashSet<string>();
            int count = 0;
            foreach (Object obj in Selection.objects)
            {
                var assetPath = AssetDatabase.GetAssetPath(obj);
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (processedGuids.Contains(guid))
                {
                    continue;
                }

                count += 1;
                EditorUtility.DisplayProgressBar("Find references", assetPath, (float)count / Selection.objects.Length);

                processedGuids.Add(guid);
                
                var references = new ObjectWithReferences
                {
                    MainObject = obj
                };
                
                // usual references
                var fileNames = FindFilesWithString(guid, "Assets");
                foreach (var fileName in fileNames)
                {
                    var asset = AssetDatabase.LoadAssetAtPath(fileName, typeof(Object));
                    if (asset != null)
                    {
                        references.References.Add(asset);
                    }
                }
                
                fileNames = FindFilesWithString(guid,  "Packages");
                foreach (var fileName in fileNames)
                {
                    var asset = AssetDatabase.LoadAssetAtPath(fileName, typeof(Object));
                    if (asset != null)
                    {
                        references.References.Add(asset);
                    }
                }
                
                // ObjectLink references
                fileNames = FindFilesWithString(assetPath,  "Assets");
                foreach (var fileName in fileNames)
                {
                    var asset = AssetDatabase.LoadAssetAtPath(fileName, typeof(Object));
                    if (asset != null)
                    {
                        references.References.Add(asset);
                    }
                }
                objects.Add(references);
            }
            
            EditorUtility.ClearProgressBar();
            ShowWindow(objects);
        }

        private static List<string> FindFilesWithString(string guid, string  workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                FileName = "git",
                RedirectStandardOutput = true,
                WorkingDirectory = Path.Combine(Application.dataPath, "..", workingDirectory),
                Arguments = $"grep --name-only --fixed-strings --untracked {guid}"
            };

            var found = new List<string>();
            try
            {
                var engineProcess = Process.Start(startInfo)!;
                while (!engineProcess.StandardOutput.EndOfStream)
                {
                    string line = engineProcess.StandardOutput.ReadLine();
                    if (line != null && !line.EndsWith(".meta") && !line.EndsWith(".buildreport"))
                    {
                        found.Add($"{workingDirectory}/{line}");
                    }
                }

                engineProcess.WaitForExit();
            }
            catch
            {
                UnityEngine.Debug.Log("Error when running Git");
                return found;
            }

            return found;
        }

        private static void ShowWindow(List<ObjectWithReferences> objects)
        {
            var dialog = GetWindow<ReferencesWindow>(false, "Использование ассетов");
            dialog._objects = objects;
            dialog.Show();
        }

        private void OnGUI()
        {
            if (_objects == null)
            {
                return;
            }
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            foreach (var obj in _objects)
            {
                EditorGUILayout.LabelField("Ассет");
                EditorGUILayout.ObjectField(obj.MainObject, typeof(Object), false);
                GUILayout.BeginHorizontal();
                GUILayout.Space(20f);

                GUILayout.BeginVertical();

                if (obj.References.Count == 0)
                {
                    EditorGUILayout.LabelField("Не используется");
                }
                else
                {
                    EditorGUILayout.LabelField("Используется в ассетах:");
                    foreach (var reference in obj.References)
                    {
                        EditorGUILayout.ObjectField(reference, reference.GetType(), false);
                    }
                }

                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                EditorGUILayout.Separator();
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
