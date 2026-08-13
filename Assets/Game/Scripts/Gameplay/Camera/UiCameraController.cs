using DataStorage;
using UnityEngine;

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
			if (mainCamera == null)
			{
				// TASK-126: UI-скоуп теперь поднимается app-level (LoadingGame.InitializeLocalServices),
				// поэтому UI-канвас (на нём висит этот контроллер) инстанцируется и на Loading.unity, где
				// геймплейной MainCamera ещё нет (Data.Scene.MainCamera == null) — раньше UI-канвас без
				// игровой сцены не поднимался вовсе, и эта ветка не достигалась. Overlay-камеру регистрировать
				// не на что; попап-канвас ScreenSpaceOverlay в ней не нуждается. На игровой сцене (будущий
				// TODO LoadingGame:69) MainCamera появится и регистрация пойдёт как прежде.
				return;
			}

			var ctrl = mainCamera.GetComponent<MainCameraController>();
			if (ctrl != null)
			{
				ctrl.RegisterOverlayCamera(_camera);
				Data.Scene.UiCamera = _camera;
			}
			
		}
	}
}
