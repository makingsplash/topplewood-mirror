#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace Game
{
    public class CaptureScreenShot : MonoBehaviour
    {
        private RecorderController _recorderController;
        private RecorderControllerSettings _recorderControllerSettings;
        private ImageRecorderSettings _imageRecorderSettings;
        private IReadOnlyList<CaptureConfig.Preset> _presets;

        private Coroutine _coroutine;
        
        public void StartCapturing(RecorderControllerSettings recorderControllerSettings, ImageRecorderSettings imageRecorderSettings, IReadOnlyList<CaptureConfig.Preset> presets)
        {
            _recorderControllerSettings = recorderControllerSettings;
            _imageRecorderSettings = imageRecorderSettings;
            _presets = presets;
            
            _recorderController = new RecorderController(_recorderControllerSettings);

            // Image
            _imageRecorderSettings.Enabled = true;
            _imageRecorderSettings.OutputFormat = ImageRecorderSettings.ImageRecorderOutputFormat.PNG;
            _imageRecorderSettings.CaptureAlpha = false;
            

            // Setup Recording
            _recorderControllerSettings.AddRecorderSettings(_imageRecorderSettings);
            _recorderControllerSettings.SetRecordModeToSingleFrame(0);
            
            if (_coroutine != null)
            {
                StopCoroutine(_coroutine);
                _coroutine = null;
            }

            _coroutine = StartCoroutine(MakeScreenshotsCorr());
        }
        

        private IEnumerator MakeScreenshotsCorr()
        {
            foreach (var preset in _presets)
            {
                GetScreenShot(preset);
                yield return new WaitForSeconds(0.3f);
            }
            
            Debug.Log("Done!");
        }
        
        
        private void GetScreenShot(CaptureConfig.Preset preset)
        {
            if (_imageRecorderSettings != null)
            {
                var resolution = preset.Resolution;
                
                _imageRecorderSettings.imageInputSettings = new GameViewInputSettings
                {
                    OutputWidth = resolution.x,
                    OutputHeight = resolution.y,
                };
                
                var mediaOutputFolder = Path.Combine(Application.dataPath, "..", "SampleRecordings", preset.Name);
                _imageRecorderSettings.OutputFile = Path.Combine(mediaOutputFolder,
                    $"screenshot_{resolution.x.ToString()}x{resolution.y.ToString()}_{preset.Name}_") + DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            }
            
            _recorderController.PrepareRecording();
            _recorderController.StartRecording();
            
            Debug.Log($"Screen captured: {preset.Name} {preset.Resolution}");
        }
    }
}

#endif
