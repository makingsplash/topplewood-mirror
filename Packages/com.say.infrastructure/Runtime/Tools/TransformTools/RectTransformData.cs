using System;

using UnityEngine;

namespace SGS.Infrastructure.Tools.TransformTools
{
    [Serializable]
    public struct RectTransformData
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale;
        public Vector2 SizeDelta;
        public Vector2 AnchorMin;
        public Vector2 AnchorMax;
        public Vector2 AnchoredPosition;
        public Vector2 Pivot;
        
        public RectTransformData(RectTransform rectTransform)
        {
            Position = rectTransform.position;
            Rotation = rectTransform.localEulerAngles;
            Scale = rectTransform.localScale;
            SizeDelta = rectTransform.sizeDelta;
            AnchorMin = rectTransform.anchorMin;
            AnchorMax = rectTransform.anchorMax;
            AnchoredPosition = rectTransform.anchoredPosition;
            Pivot = rectTransform.pivot;
        }

        public void ApplyToTransform(RectTransform rectTransform)
        {
            rectTransform.localPosition = Position;
            rectTransform.localEulerAngles = Rotation;
            rectTransform.localScale = Scale;
            rectTransform.sizeDelta = SizeDelta;
            rectTransform.anchorMin = AnchorMin;
            rectTransform.anchorMax = AnchorMax;
            rectTransform.anchoredPosition = AnchoredPosition;
            rectTransform.pivot = Pivot;
        }
    }
}