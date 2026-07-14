using UnityEngine;
using UnityEngine.Rendering.Universal;
using VContainer;
using Game.Services.LogService;

namespace Game.Gameplay
{
	public class MainCameraController : MonoBehaviour
	{

		[SerializeField] private UnityEngine.Camera _camera;
		private ILogService _logService;

		[Inject]
		public void Construct(ILogService logService)
		{
			_logService = logService;
		}
		
		
		public void RegisterOverlayCamera(UnityEngine.Camera overlayCamera)
		{
			if (overlayCamera == null)
			{
				_logService?.Log(LogLevel.Warning, "RegisterOverlayCamera: overlayCamera is null.", LogCategory.Gameplay);
				return;
			}

			if (_camera == null)
			{
				_logService?.Log(LogLevel.Warning, "RegisterOverlayCamera: base _camera is not assigned.", LogCategory.Gameplay);
				return;
			}



			var overlayData = overlayCamera.GetUniversalAdditionalCameraData();
			if (overlayData == null)
			{
				_logService?.Log(LogLevel.Warning, "RegisterOverlayCamera: UniversalAdditionalCameraData not found on overlay camera.", LogCategory.Gameplay);
				return;
			}

			if (overlayData.renderType != CameraRenderType.Overlay)
			{
				overlayData.renderType = CameraRenderType.Overlay;
			}
			
			var baseData = _camera.GetUniversalAdditionalCameraData();

			// Добавляем в стек, если ещё не добавлена
			if (!baseData.cameraStack.Contains(overlayCamera))
			{
				baseData.cameraStack.Add(overlayCamera);
			}
		}
		
	}
}
