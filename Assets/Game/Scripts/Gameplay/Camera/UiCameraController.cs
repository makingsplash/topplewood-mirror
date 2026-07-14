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
			var ctrl = mainCamera.GetComponent<MainCameraController>();
			if (ctrl != null)
			{
				ctrl.RegisterOverlayCamera(_camera);
				Data.Scene.UiCamera = _camera;
			}
			
		}
	}
}
