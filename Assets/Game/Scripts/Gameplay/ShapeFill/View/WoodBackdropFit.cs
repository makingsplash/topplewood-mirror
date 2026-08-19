using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Держит спрайт-стену на весь кадр. Не разово в Start, а каждый кадр в LateUpdate, потому что
    /// <see cref="CameraFramingPresenter"/> переставляет камеру и меняет orthographicSize на КАЖДЫЙ
    /// уровень — фон, подогнанный один раз, на высоком уровне открыл бы пустоту по краям.
    ///
    /// Подгонка «по большей стороне» (cover): картинка портретная (1059×1920), и на широком экране
    /// её лучше обрезать, чем оставить поля. Стенд редактора зовёт <see cref="Fit"/> напрямую —
    /// в режиме редактора LateUpdate не идёт.
    /// </summary>
    [ExecuteAlways]
    public sealed class WoodBackdropFit : MonoBehaviour
    {
        /// <summary>Расстояние от камеры по её взгляду. Дальше всей игры, но внутри far-плоскости.</summary>
        private const float DistanceFromCamera = 18f;

        private const float Overscan = 1.02f;   // чуть с запасом, чтобы не ловить щель на краю

        // Именно UnityEngine.Camera: в области видимости этого неймспейса «Camera» — НЕЙМСПЕЙС
        // (Game.Camera с CameraTools), поэтому короткое имя тут не компилируется. Тем же способом
        // уточняет тип CameraFramingPresenter.
        private UnityEngine.Camera _camera;
        private SpriteRenderer _renderer;

        public void Bind(UnityEngine.Camera camera, SpriteRenderer spriteRenderer)
        {
            _camera = camera;
            _renderer = spriteRenderer;
            Fit();
        }

        private void LateUpdate() => Fit();

        public void Fit()
        {
            if (_camera == null)
                _camera = UnityEngine.Camera.main;
            if (_renderer == null)
                _renderer = GetComponent<SpriteRenderer>();
            if (_camera == null || _renderer == null || _renderer.sprite == null)
                return;

            var t = transform;
            var camTransform = _camera.transform;
            t.position = camTransform.position + camTransform.forward * DistanceFromCamera;
            t.rotation = camTransform.rotation;

            // Ортокамера: высота кадра = 2·orthographicSize, ширина = высота·aspect.
            var worldHeight = _camera.orthographic
                ? _camera.orthographicSize * 2f
                : 2f * DistanceFromCamera * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var worldWidth = worldHeight * (_camera.aspect <= 0f ? 1f : _camera.aspect);

            var spriteSize = _renderer.sprite.bounds.size;
            if (spriteSize.x <= 1e-4f || spriteSize.y <= 1e-4f)
                return;

            var scale = Mathf.Max(worldWidth / spriteSize.x, worldHeight / spriteSize.y) * Overscan;
            t.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
