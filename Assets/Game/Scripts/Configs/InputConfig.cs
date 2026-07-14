using System;
using Newtonsoft.Json;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;

namespace Game.Configs
{
    [CreateAssetMenu(fileName = "InputConfig", menuName = "Configs/InputConfig")]
    public sealed class InputConfig : ConfigWithJson<InputConfig.Data>
    {
        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        public class Data
        {
            [JsonProperty("DragDetectionDistance")] [SerializeField] private float _dragDetectionDistance = 50f;
            [JsonProperty("HoldTimeThreshold")] [SerializeField] private float _holdTimeThreshold = 0.5f;
            [JsonProperty("StickRadius")] [SerializeField] private float _stickRadius = 30f;
            [JsonProperty("DirectionDetectionThreshold")] [SerializeField] private float _directionDetectionThreshold = 0.5f;
            [JsonProperty("InputForwardRotationEuler")] [SerializeField] private float _inputForwardRotationEuler;

            [Header("Multitouch")]
            [JsonProperty("MaxMultiTouchZoomAmount")] [SerializeField] private float _maxMultiTouchZoomAmount = 1f;
            [JsonProperty("MaxMultiTouchRotationSpeed")] [SerializeField] private float _maxMultiTouchRotationSpeed = 1f;
            [JsonProperty("RotationSmoothingEnabled")] [SerializeField] private bool _rotationSmoothingEnabled = true;
            [JsonProperty("RotationSmoothingTime")] [SerializeField, Min(0f)] private float _rotationSmoothingTime = 0.08f; // seconds to ~63% convergence

            [JsonProperty("RotationDegreesPerCm")] [SerializeField] private float _rotationDegreesPerCm = 40f;

            [Header("CameraControl")]
            [JsonProperty("MinOffset")] [SerializeField] private float _minOffset = -5f;
            [JsonProperty("MaxOffset")] [SerializeField] private float _maxOffset = -15f;
            [JsonProperty("ZoomSensitivity")] [SerializeField] private float _zoomSensitivity = 0.2f;
            [JsonProperty("ZoomSmoothTime")] [SerializeField] private float _zoomSmoothTime = 0.08f;
            [JsonProperty("InvertScroll")] [SerializeField] private bool _invertScroll;
            [JsonProperty("ZoomDefault")] [SerializeField, Range(0, 1)] private float _zoomDefault = 0.5f;

            [Header("Raycast")]
            [JsonProperty("AreaRaycastRadius")] [SerializeField] private float _areaRaycastRadius = 0.2f;
            [JsonProperty("AreaRaycastCount")] [SerializeField] private int _areaRaycastCount = 6;

            [Header("Device")]
            [JsonProperty("FallbackScreenDpi")] [SerializeField] private float _fallbackScreenDpi = 400f;

            [Header("Device")]
            [JsonProperty("MouseHorizontalScrollMultiplier")] [SerializeField] private float _mouseHorizontalScrollMultiplier = 0.1f;

            [Header("Gameplay")]
            [JsonProperty("SpoolsWaitTimeoutSecondsWhenSlotsFull")] [SerializeField] private float _spoolsWaitTimeoutSecondsWhenSlotsFull = 3f;
            [JsonProperty("GameplayClickBufferExpirationSeconds")] [SerializeField, Min(0f)] private float _gameplayClickBufferExpirationSeconds = 0.3f;

            public bool IsRotationSmoothingEnabled() => _rotationSmoothingEnabled;
            public float GetRotationSmoothingTime() => _rotationSmoothingTime;
            public float MinOffset => _minOffset;
            public float MaxOffset => _maxOffset;
            public float ZoomSensitivity => _zoomSensitivity;
            public float ZoomSmoothTime => _zoomSmoothTime;
            public bool InvertScroll => _invertScroll;
            public float GetMouseHorizontalScrollMultiplier() => _mouseHorizontalScrollMultiplier;
            public float GetZoomDefault() => _zoomDefault;
            public float GetFallbackScreenDpi() => _fallbackScreenDpi;
            public float GetRotationDegreesPerCm() => _rotationDegreesPerCm;
            public float GetMultiTouchRotationSpeed() => _maxMultiTouchRotationSpeed;
            public float AreaRaycastRadius => _areaRaycastRadius;
            public int AreaRaycastCount => _areaRaycastCount;
            public float HoldTimeThreshold => _holdTimeThreshold;
            public float StickRadius => _stickRadius;
            public float DirectionDetectionThreshold => _directionDetectionThreshold;
            public float MaxMultiTouchZoomAmount => _maxMultiTouchZoomAmount;
            public float SpoolsWaitTimeoutSecondsWhenSlotsFull => _spoolsWaitTimeoutSecondsWhenSlotsFull;
            public float GameplayClickBufferExpirationSeconds => _gameplayClickBufferExpirationSeconds;

            public float DragDetectionDistance
            {
                get => _dragDetectionDistance;
                set => _dragDetectionDistance = value;
            }
        }

        public bool IsRotationSmoothingEnabled() => Config.IsRotationSmoothingEnabled();
        public float GetRotationSmoothingTime() => Config.GetRotationSmoothingTime();
        public float MinOffset => Config.MinOffset;
        public float MaxOffset => Config.MaxOffset;
        public float ZoomSensitivity => Config.ZoomSensitivity;
        public float ZoomSmoothTime => Config.ZoomSmoothTime;
        public bool InvertScroll => Config.InvertScroll;
        public float GetMouseHorizontalScrollMultiplier() => Config.GetMouseHorizontalScrollMultiplier();
        public float GetZoomDefault() => Config.GetZoomDefault();
        public float GetFallbackScreenDpi() => Config.GetFallbackScreenDpi();
        public float GetRotationDegreesPerCm() => Config.GetRotationDegreesPerCm();
        public float GetMultiTouchRotationSpeed() => Config.GetMultiTouchRotationSpeed();
        public float AreaRaycastRadius => Config.AreaRaycastRadius;
        public int AreaRaycastCount => Config.AreaRaycastCount;
        public float HoldTimeThreshold => Config.HoldTimeThreshold;
        public float StickRadius => Config.StickRadius;
        public float DirectionDetectionThreshold => Config.DirectionDetectionThreshold;
        public float MaxMultiTouchZoomAmount => Config.MaxMultiTouchZoomAmount;
        public float SpoolsWaitTimeoutSecondsWhenSlotsFull => Config.SpoolsWaitTimeoutSecondsWhenSlotsFull;
        public float GameplayClickBufferExpirationSeconds => Config.GameplayClickBufferExpirationSeconds;

        public float DragDetectionDistance
        {
            get => Config.DragDetectionDistance;
            set => Config.DragDetectionDistance = value;
        }
    }
}
