using System;

using UnityEngine;

namespace SGS.Infrastructure.Tools.TransformTools
{
    [Serializable]
    public struct TransformData
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale;

        public TransformData(Vector3 position, Vector3 rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public void ApplyToTransform(Transform transform)
        {
            transform.localPosition = Position;
            transform.localEulerAngles = Rotation;
            transform.localScale = Scale;
        }

        public static TransformData FromTransform(Transform transform)
        {
            return new TransformData(transform.position, transform.localEulerAngles, transform.localScale);
        }
    }
}