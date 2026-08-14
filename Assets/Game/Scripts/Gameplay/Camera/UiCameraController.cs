using DataStorage;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Game.Services.LogService;

namespace Game.Gameplay
{
	public class UiCameraController : MonoBehaviour
	{
		[SerializeField] private UnityEngine.Camera _camera;
		[SerializeField] private Vector3 _worldPos;
		[SerializeField] private float _planeDistance = 10f;

		public float PlaneDistance => _planeDistance;


		private void Start()
		{
			transform.position = _worldPos;

			var mainCamera = Data.Scene.MainCamera;
			var ctrl = mainCamera != null ? mainCamera.GetComponent<MainCameraController>() : null;

			if (ctrl != null)
			{
				// Игровая сцена: MainCameraController владеет стеком базовой камеры — регистрируем через него
				// (прежний путь, не тронут).
				ctrl.RegisterOverlayCamera(_camera);
				Data.Scene.UiCamera = _camera;
				return;
			}

			// TASK-126 (owner 08:11): на Loading.unity (и любом прод-буте до игровой сцены) геймплейного
			// MainCameraController ещё нет — Data.Scene.MainCamera == null. Но UiPrefabs._useSeparateCanvasCamera
			// == 1, значит UI-канвас рендерится ЭТОЙ отдельной overlay-камерой, а overlay в URP выводит пиксели
			// ТОЛЬКО находясь в cameraStack базовой камеры. Прежний ранний return пропускал регистрацию: попап
			// жил в иерархии, но рендерился в никуда (белый экран). Пристёгиваем overlay напрямую к базовой
			// камере сцены.
			AttachOverlayToSceneBaseCamera();
		}

		// Прямое добавление UI-overlay-камеры в стек базовой камеры сцены, когда MainCameraController недоступен.
		// Выбран этот путь, а НЕ переключение канваса на ScreenSpaceOverlay (owner 08:11, п.2): прод-конфиг —
		// _useSeparateCanvasCamera == 1, и overlay-камера даёт тот же порядок отрисовки, что в игре; перевод на
		// ScreenSpaceOverlay разошёлся бы с прод-рендером и дал другой z-order, то есть проверяли бы не то, что
		// в билде. Базовую камеру ищем через Camera.main — на Loading.unity это объект `Main Camera`, tag
		// MainCamera (сверено по сцене). Результат логируем через StaticLogService.Current: этот контроллер
		// висит на UI-канвасе, который инстанцирует пакетный UiService через Addressables, DI сюда НЕ доходит
		// (нет [Inject]), а Current — тот же ILogService, что резолвит RootLifetimeScope (StaticLogService.Set),
		// и пишет в видимый лог Unity (Debug.* режется filterLogType, см. LoadingGame.InitializeGameSettings).
		private void AttachOverlayToSceneBaseCamera()
		{
			var log = StaticLogService.Current;

			if (_camera == null)
			{
				log.Log(LogLevel.Warning,
					"UiCameraController: serialized _camera is null — nothing to attach; UI popups will not render.",
					LogCategory.UI, this);
				return;
			}

			var baseCamera = UnityEngine.Camera.main;
			if (baseCamera == null || baseCamera == _camera)
			{
				log.Log(LogLevel.Warning,
					"UiCameraController: no base scene camera found via Camera.main (tag MainCamera) — UI overlay " +
					"camera not attached, popups will not render.",
					LogCategory.UI, this);
				return;
			}

			var overlayData = _camera.GetUniversalAdditionalCameraData();
			if (overlayData.renderType != CameraRenderType.Overlay)
				overlayData.renderType = CameraRenderType.Overlay;

			var baseData = baseCamera.GetUniversalAdditionalCameraData();
			if (!baseData.cameraStack.Contains(_camera))
				baseData.cameraStack.Add(_camera);

			Data.Scene.UiCamera = _camera;

			log.Log(LogLevel.Info,
				$"UiCameraController: attached UI overlay camera '{_camera.name}' to base camera " +
				$"'{baseCamera.name}'; base cameraStack now holds {baseData.cameraStack.Count.ToString()} overlay camera(s).",
				LogCategory.UI, this);
		}
	}
}
