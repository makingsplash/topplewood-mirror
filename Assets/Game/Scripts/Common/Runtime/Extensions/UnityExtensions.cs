using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Common.Extensions
{
    public static class UnityExtensions
    {
        public static void Destroy(this Object obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            DestroyObject(obj);
        }
        
        public static void SafeDestroy(this Object obj)
        {
            if (obj == null)
                return;

            DestroyObject(obj);
        }

        private static void DestroyObject(Object obj)
        {
            if (Application.isPlaying) 
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }
        
        public static void DestroyGameObject(this Component component)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component));

            Destroy(component.gameObject);
        }
        
        public static void SafeDestroyGameObject(this Component component)
        {
            if (component == null)
                return;

            Destroy(component.gameObject);
        }
    }
}
