using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using System;
using System.IO;

namespace Game.Editor
{
    public class JenkinsBuildWindow : EditorWindow
    {
        private string _branchName = "";
        private int _platformIndex = 0;
        private readonly string[] _platforms = { "android", "ios" };
        private string _token = "***REMOVED***"; // Generated secure token

        private string _jenkinsUser = "";
        private string _jenkinsApiToken = "";

        private int _selectedTab = 0;
        private readonly string[] _tabs = { "Build", "Settings" };

        private const string PrefKeyUser = "JenkinsBuild_User";
        private const string PrefKeyToken = "JenkinsBuild_ApiToken";
        private const string PrefKeyAuthToken = "JenkinsBuild_AuthToken";
        private const string PrefKeyPlatform = "JenkinsBuild_Platform";

        [MenuItem("Game/Build/Jenkins")]
        public static void ShowWindow()
        {
            var window = GetWindow<JenkinsBuildWindow>("Jenkins Build");
            window._branchName = GetCurrentBranch();
            window.Show();
        }

        private void OnEnable()
        {
            _jenkinsUser = EditorPrefs.GetString(PrefKeyUser, "");
            _jenkinsApiToken = EditorPrefs.GetString(PrefKeyToken, "");
            _token = EditorPrefs.GetString(PrefKeyAuthToken, "***REMOVED***");

            string savedPlatform = EditorPrefs.GetString(PrefKeyPlatform, "android");
            _platformIndex = Array.IndexOf(_platforms, savedPlatform);
            if (_platformIndex < 0) _platformIndex = 0;
        }

        private void OnGUI()
        {
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabs);

            GUILayout.Space(10);

            if (_selectedTab == 0)
            {
                DrawBuildTab();
            }
            else
            {
                DrawSettingsTab();
            }
        }

        private void DrawBuildTab()
        {
            GUILayout.Label("Build Settings", EditorStyles.boldLabel);

            _branchName = EditorGUILayout.TextField("Branch Name", _branchName);

            EditorGUI.BeginChangeCheck();
            _platformIndex = EditorGUILayout.Popup("Platform", _platformIndex, _platforms);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PrefKeyPlatform, _platforms[_platformIndex]);
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Build", GUILayout.Height(30)))
            {
                TriggerJenkinsBuild();
            }
        }

        private void DrawSettingsTab()
        {
            GUILayout.Label("Authentication & API", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _jenkinsUser = EditorGUILayout.TextField("Jenkins Username", _jenkinsUser);
            _jenkinsApiToken = EditorGUILayout.PasswordField("API Token / Password", _jenkinsApiToken);
            _token = EditorGUILayout.TextField("Jenkins Auth Token", _token);

            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PrefKeyUser, _jenkinsUser);
                EditorPrefs.SetString(PrefKeyToken, _jenkinsApiToken);
                EditorPrefs.SetString(PrefKeyAuthToken, _token);
            }
        }

        private static string GetCurrentBranch()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --abbrev-ref HEAD",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process == null) return "master";
                    string branch = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    return string.IsNullOrEmpty(branch) ? "master" : branch;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Failed to get git branch: {e.Message}");
                return "master";
            }
        }

        private void TriggerJenkinsBuild()
        {
            if (string.IsNullOrEmpty(_branchName))
            {
                EditorUtility.DisplayDialog("Error", "Branch name cannot be empty", "OK");
                return;
            }

            if (string.IsNullOrEmpty(_jenkinsUser) || string.IsNullOrEmpty(_jenkinsApiToken))
            {
                EditorUtility.DisplayDialog("Error", "Jenkins Username and API Token are required", "OK");
                return;
            }

            string selectedPlatform = _platforms[_platformIndex];
            string jobName = "tile_sort"; // Matches tile_sort_android / tile_sort_ios
            string fullJobName = $"teams/job/team-game-pie/job/TileSort/job/{jobName}_{selectedPlatform}";
            string baseUrl = "https://jenkins.saygames.io";
            string auth = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_jenkinsUser}:{_jenkinsApiToken}"));

            try
            {
                string crumbHeader = null;
                string crumbValue = null;

                // Try to get CSRF crumb
                try
                {
                    var crumbRequest = (System.Net.HttpWebRequest)System.Net.WebRequest.Create($"{baseUrl}/crumbIssuer/api/xml?xpath=concat(//crumbRequestField,\":\",//crumb)");
                    crumbRequest.Headers["Authorization"] = "Basic " + auth;
                    using (var crumbResponse = (System.Net.HttpWebResponse)crumbRequest.GetResponse())
                    using (var reader = new StreamReader(crumbResponse.GetResponseStream()))
                    {
                        string crumbData = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(crumbData) && crumbData.Contains(":"))
                        {
                            var parts = crumbData.Split(':');
                            crumbHeader = parts[0];
                            crumbValue = parts[1];
                            UnityEngine.Debug.Log($"Jenkins Crumb acquired: {crumbHeader}={crumbValue}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log($"CSRF Crumb not required or failed to fetch: {ex.Message}");
                }

                string url = $"{baseUrl}/job/{fullJobName}/buildWithParameters?token={_token}";
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                request.Method = "POST";
                request.Headers["Authorization"] = "Basic " + auth;

                if (!string.IsNullOrEmpty(crumbHeader) && !string.IsNullOrEmpty(crumbValue))
                {
                    request.Headers[crumbHeader] = crumbValue;
                }

                string postData = $"branch={_branchName}&platform={selectedPlatform}";
                byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(postData);
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = byteArray.Length;

                using (Stream dataStream = request.GetRequestStream())
                {
                    dataStream.Write(byteArray, 0, byteArray.Length);
                }

                using (var response = (System.Net.HttpWebResponse)request.GetResponse())
                {
                    UnityEngine.Debug.Log($"Jenkins build triggered! Status: {response.StatusDescription}");
                    EditorUtility.DisplayDialog("Jenkins", $"Build triggered successfully for branch '{_branchName}' on platform '{selectedPlatform}'!", "OK");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to trigger Jenkins build: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to trigger build: {e.Message}", "OK");
            }
        }
    }
}
