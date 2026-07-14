using System;
using System.IO;

using UnityEditor;

using UnityEngine;

namespace SGS.Editor.Tools.BuildReport
{
    public static class BuildReportMenu
    {
        [MenuItem("DevTools/BuildReport/Show Build Report Window")]
        private static void ShowWindow()
        {
            EditorWindow.GetWindow<BuildReportWindow>().Show();
        }

        public static UnityEditor.Build.Reporting.BuildReport LoadBuildReport()
        {
            const string buildReportDir = "Assets/BuildReports";
            if (Directory.Exists(buildReportDir) == false)
            {
                Directory.CreateDirectory(buildReportDir);
            }

            string assetPath = buildReportDir + "/New Report.buildreport";
            try
            {
                File.Copy("Library/LastBuild.buildreport", assetPath, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Не удалось получить свежий отчёт о сборке. Используется старый.");
                Debug.LogException(e);
            }
            AssetDatabase.ImportAsset(assetPath);

            var report = AssetDatabase.LoadAssetAtPath<UnityEditor.Build.Reporting.BuildReport>(assetPath);
            return report;
        }
    }
}