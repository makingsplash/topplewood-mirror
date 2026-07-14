using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEngine;

namespace Game
{
    public static class Capture
    {

        private static Coroutine _coroutine;
        
        [MenuItem("Game/Tools/Capture/Screenshots")]
        private static void MakeScreenshots()
        {
            var config = EditorExt.GetAssetWithType<CaptureConfig>();
            var controllerSettings = EditorExt.GetAssetWithType<RecorderControllerSettings>();
            var imageRecorderSettings = EditorExt.GetAssetWithType<ImageRecorderSettings>();


            if (Application.isPlaying)
            {
                if (controllerSettings != null && imageRecorderSettings != null)
                {
                    var captureInst = Object.FindFirstObjectByType<CaptureScreenShot>();
                    if (captureInst == null)
                    {
                        var captureObj = new GameObject("Capture");
                        captureInst = captureObj.AddComponent<CaptureScreenShot>();
                    }
                    
                    captureInst.StartCapturing(controllerSettings, imageRecorderSettings, config.Presets);
                }
            }
        }
        
        [MenuItem("Game/Tools/Capture/Config")]
        public static void SelectCaptureConfig()
        {
            EditorExt.SelectAssetsWithType("CaptureConfig", new [] {"Assets/Game"});
        }
        
        //[MenuItem("Game/Tools/Capture/Create Configs")]
        private static void CreateSettings()
        {
            RecorderControllerSettings recorderControllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            string path = "Assets/Game/Modules/Capture/Configs/RecorderControllerSettings.asset";

            AssetDatabase.CreateAsset(recorderControllerSettings, path);

            ImageRecorderSettings imageRecorderSettings = ScriptableObject.CreateInstance<ImageRecorderSettings>();
            string path2 = "Assets/Game/Modules/Capture/Configs/ImageRecorderSettings.asset";
            AssetDatabase.CreateAsset(imageRecorderSettings, path2);
            AssetDatabase.SaveAssets();
            
            AssetDatabase.Refresh();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = recorderControllerSettings;
        }
        
    }

}
