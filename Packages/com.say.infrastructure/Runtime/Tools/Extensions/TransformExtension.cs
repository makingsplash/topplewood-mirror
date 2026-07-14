using SGS.Infrastructure.Tools.TransformTools;
using UnityEngine;

namespace SGS.Infrastructure.Tools.Extensions
{
    public static class TransformExtension
    {
        public static void ApplyTransformData(this Transform transform, TransformData data)
        {
            data.ApplyToTransform(transform);
        }
    }
}