using UnityEngine;

namespace Game
{
	public static class CameraTools
	{
		public static Vector3 ConvertFromMainToDesk(Vector3 mainCamPosition, Camera mainCam, Camera uiCamera)
		{
			if (mainCam == null || uiCamera == null)
				return mainCamPosition;

			// Проецируем мировую точку в нормализованные координаты экрана (viewport) главной камеры
			var vp = mainCam.WorldToViewportPoint(mainCamPosition);

			// Если точка за главной камерой (z < 0), просто возвращаем исходную позицию
			// (либо можно инвертировать/клиповать по потребности)
			if (vp.z < 0f)
				return mainCamPosition;

			// Пересчитываем в мировую точку относительно UI-камеры:
			// x,y — те же нормализованные координаты экрана; z — на плоскости near клиппинга UI-камеры
			return uiCamera.ViewportToWorldPoint(new Vector3(vp.x, vp.y, uiCamera.nearClipPlane));
		}
	}
}
