using System;
using System.Collections;

using SayKitInternal;

using Unity.EditorCoroutines.Editor;

using UnityEngine;
using UnityEngine.Networking;

using Random = UnityEngine.Random;


namespace SGS.Editor.Tools.DevTools
{
    public static class RemoteConfigTools
    {
        /// <summary>
        /// Tries to download and parse remote config of the given version
        /// </summary>
        /// <param name="version">version of remote config</param>
        /// <param name="setResultMethod">bool – downloaded and parsed successfully, RemoteConfig – resulting remote config</param>
        /// <returns></returns>
        public static IEnumerator DownloadAndParseRemoteConfig(string version, Action<bool, RemoteConfig> setResultMethod)
        {
            string downloadedConfig = string.Empty;
            yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                DownloadConfig(version, result => downloadedConfig = result));
            
            // "config not found" is valid result with result code 200, so we need to process it separately
            if (string.IsNullOrEmpty(downloadedConfig) || downloadedConfig == "\"config not found\"")
            {
                Debug.LogError($"Config of version ({version}) was not found");
                setResultMethod?.Invoke(false, null);
                yield break;
            }
            
            RemoteConfig remoteConfig;
            try
            {
                remoteConfig = JsonUtility.FromJson<RemoteConfig>(downloadedConfig);
            }
            catch (Exception e)
            {
                Debug.LogError($"Can't parse downloaded config:\n{e}");
                setResultMethod?.Invoke(false, null);
                yield break;
            }
            
            setResultMethod?.Invoke(true, remoteConfig);
        }
        
        private static IEnumerator DownloadConfig(string version, Action<string> setResultMethod)
        {
            // SayKitApp
            string downloadedConfig = string.Empty;
            string appKey = GetAppKey();
            string url = "https://app.saygames.io/config/" + appKey
                                                           + "?version=" + version
                                                           + "&device_id=" + SystemInfo.deviceUniqueIdentifier
                                                           + "&idfa="
                                                           + "&saykit=" + SKManager.Instance.Version
                                                           + "&lng=en"
                                                           + "&_=" + Random.Range(100000000, 900000000)
                                                           + "&memory=" + SystemInfo.systemMemorySize
                                                           + "&graphics_memory=" +SystemInfo.graphicsMemorySize
                                                           + "&device_name=unity_editor";
            Debug.Log($"url: <a>{url}</a>");
            
            using UnityWebRequest webRequest = UnityWebRequest.Get(url);
            
            webRequest.timeout = 5;
            yield return webRequest.SendWebRequest();
            
            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                //Assert("SayKit: DownloadAvailableDatabaseList NetworkError: " + webRequest.error);
                Debug.LogError("Config download error occured");
            }
            else
            {
                downloadedConfig = webRequest.downloadHandler?.text;
                Debug.Log($"Downloaded config successfully, {downloadedConfig.Length} chars");
            }

            setResultMethod?.Invoke(downloadedConfig);
        }
        
        private static string GetAppKey() => SayKitAppSettingsLoader.ResolveAppKey();
    }
}