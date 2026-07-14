using System.Collections.Generic;
using System.Text;
using UnityEngine;

using Object = UnityEngine.Object;

namespace SGS.UnityExtensions
{
    public static class TransformExtension
    {
        public static void RemoveChildren(this Transform transform)
        {
            foreach (Transform child in transform)
            {
                Object.Destroy(child.gameObject);
            }
        }
        
        public static void RemoveChildrenImmediate(this Transform transform)
        {
            foreach (Transform child in transform)
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }
        
        public static void SetLayer(this Transform transform, int layer)
        {
            transform.gameObject.layer = layer;
            
            foreach (Transform child in transform)
            {
                child.SetLayer(layer);
            }
        }
        
        public static void FillChildrenNames(this Transform transform, Dictionary<string, Transform> dictionary)
        {
            Queue<Transform> toProcess = new Queue<Transform>();
            
            toProcess.Enqueue(transform);

            while (toProcess.Count > 0)
            {
                Transform bone = toProcess.Dequeue();
                if (dictionary.ContainsKey(bone.name) == false)
                {
                    dictionary.Add(bone.name, bone);
                }

                foreach (Transform child in bone)
                {
                    toProcess.Enqueue(child);
                }
            }
        }

        public static string GetFullPath(this Transform transform)
        {
            Transform currentTransform = transform;
            StringBuilder fullPath = new StringBuilder(currentTransform.name);
            while (currentTransform.parent != null)
            {
                currentTransform = currentTransform.parent;
                string name = currentTransform.name.Replace("(Clone)", "");
                fullPath.Insert(0, $"{name}/");
            }

            return fullPath.ToString();
        }
    }
}