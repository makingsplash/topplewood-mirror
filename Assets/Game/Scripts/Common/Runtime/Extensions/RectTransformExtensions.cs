using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Common.Extensions
{
    public static class RectTransformExtensions
    {
        public static void FitToRenderers(
            this RectTransform target,
            IReadOnlyList<Renderer> renderers,
            Camera cam,
            Canvas canvas)
        {
            if (target == null || renderers == null || renderers.Count == 0 || cam == null || canvas == null)
                return;

            var anyVisible = false;
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;

            using var _ = ListPool<Vector3>.Get(out var boundCorners);
            boundCorners.Capacity = 8;

            foreach (var renderer in renderers)
            {
                if (renderer == null) 
                    continue;
                
                var bounds = renderer.bounds;
                
                if (bounds.size == Vector3.zero) 
                    continue;

                GetBoundsCorners(bounds, boundCorners);

                for (var i = 0; i < 8; i++)
                {
                    var sp = cam.WorldToScreenPoint(boundCorners[i]);

                    if (sp.z < 0f) 
                        continue;

                    anyVisible = true;
                    if (sp.x < minX) minX = sp.x;
                    if (sp.y < minY) minY = sp.y;
                    if (sp.x > maxX) maxX = sp.x;
                    if (sp.y > maxY) maxY = sp.y;
                }
            }

            if (anyVisible == false)
            {
                target.gameObject.SetActive(false);
                return;
            }

            var rootRect = canvas.transform as RectTransform;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rootRect, new Vector2(minX, minY), null, out var localMin);
            
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rootRect, new Vector2(maxX, maxY), null, out var localMax);

            var size = localMax - localMin;
            var center = (localMax + localMin) * 0.5f;

            target.anchorMin = target.anchorMax = new Vector2(0.5f, 0.5f);

            target.sizeDelta = new Vector2(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y));
            target.anchoredPosition = center;
        }

        private static void GetBoundsCorners(Bounds b, List<Vector3> outCorners8)
        {
            var min = b.min;
            var max = b.max;

            outCorners8.Clear();
            outCorners8.Add(new Vector3(min.x, min.y, min.z));
            outCorners8.Add(new Vector3(max.x, min.y, min.z));
            outCorners8.Add(new Vector3(min.x, max.y, min.z));
            outCorners8.Add(new Vector3(max.x, max.y, min.z));
            outCorners8.Add(new Vector3(min.x, min.y, max.z));
            outCorners8.Add(new Vector3(max.x, min.y, max.z));
            outCorners8.Add(new Vector3(min.x, max.y, max.z));
            outCorners8.Add(new Vector3(max.x, max.y, max.z));
        }
    }
}
