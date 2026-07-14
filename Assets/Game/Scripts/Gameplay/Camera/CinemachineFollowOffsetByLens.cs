using Unity.Cinemachine;
using UnityEngine;

namespace Gameplay.Camera
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CinemachineFollowOffsetByLens : MonoBehaviour
    {
        [SerializeField] private CinemachineCamera _cinemachineMainCamera;
        [SerializeField] private CinemachineFollow _cinemachineFollow;
        [SerializeField, Range(0f, 1f)] private float _trackingTargetViewportY = 1f;

        private bool _initialized;
        private float _cachedOrthographicSize = float.MinValue;
        private Quaternion _cachedCameraRotation = Quaternion.identity;
        private Rect _cachedSafeArea;
        private float _followOffsetRightComponent;
        private float _followOffsetForwardComponent;

        private void OnValidate()
        {
            if (_cinemachineMainCamera == null)
            {
                _cinemachineMainCamera = GetComponent<CinemachineCamera>();
            }

            if (_cinemachineFollow == null)
            {
                _cinemachineFollow = GetComponent<CinemachineFollow>();
            }

            _initialized = false;
            RefreshFollowOffsetIfNeeded(true);
        }

        private void OnEnable()
        {
            _initialized = false;
            RefreshFollowOffsetIfNeeded(true);
        }

        private void LateUpdate()
        {
            RefreshFollowOffsetIfNeeded(false);
        }

        private void CacheHorizontalOffsetComponents()
        {
            Vector3 followOffset = _cinemachineFollow.FollowOffset;
            Transform cameraTransform = _cinemachineMainCamera.transform;
            _followOffsetRightComponent = Vector3.Dot(followOffset, cameraTransform.right);
            _followOffsetForwardComponent = Vector3.Dot(followOffset, cameraTransform.forward);
        }

        private void RefreshFollowOffsetIfNeeded(bool force)
        {
            if (_cinemachineMainCamera == null || _cinemachineFollow == null)
            {
                return;
            }

            if (!_initialized)
            {
                CacheHorizontalOffsetComponents();
                _initialized = true;
            }

            if (!_cinemachineMainCamera.Lens.Orthographic)
            {
                return;
            }

            float orthographicSize = _cinemachineMainCamera.Lens.OrthographicSize;
            Quaternion cameraRotation = _cinemachineMainCamera.transform.rotation;
            Rect safeArea = Screen.safeArea;
            if (!force
                && Mathf.Approximately(_cachedOrthographicSize, orthographicSize)
                && Quaternion.Angle(_cachedCameraRotation, cameraRotation) <= Mathf.Epsilon
                && _cachedSafeArea == safeArea)
            {
                return;
            }

            _cachedOrthographicSize = orthographicSize;
            _cachedCameraRotation = cameraRotation;
            _cachedSafeArea = safeArea;

            float viewportY = GetTrackingViewportYFromSafeArea(safeArea);
            float targetVerticalOffset = (viewportY - 0.5f) * 2f * orthographicSize;
            float followOffsetUpComponent = -targetVerticalOffset;

            Transform cameraTransform = _cinemachineMainCamera.transform;
            Vector3 right = cameraTransform.right;
            Vector3 up = cameraTransform.up;
            Vector3 forward = cameraTransform.forward;

            _cinemachineFollow.FollowOffset =
                (right * _followOffsetRightComponent)
                + (up * followOffsetUpComponent)
                + (forward * _followOffsetForwardComponent);
        }

        private float GetTrackingViewportYFromSafeArea(Rect safeArea)
        {
            if (Screen.height <= 0f)
            {
                return _trackingTargetViewportY;
            }

            float safeAreaMinY = safeArea.y / Screen.height;
            float safeAreaHeight = safeArea.height / Screen.height;
            return safeAreaMinY + (_trackingTargetViewportY * safeAreaHeight);
        }
    }
}
