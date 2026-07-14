using UnityEngine;

namespace SGS.Services.DebugTools
{
    public static class DebugDraw
    {
        public static void DrawCircle(Vector3 position, float radius, Color color)
        {
            if (radius <= 0.0f)
            {
                return;
            }

            int segments = 32;
            float angleStep = (2 * Mathf.PI / segments);

            Vector3 lineStart;
            Vector3 lineEnd = Vector3.right;

            for (int i = 0; i < segments; i++)
            {
                lineStart = lineEnd;

                lineEnd.x = Mathf.Cos(angleStep * (i + 1));
                lineEnd.z = Mathf.Sin(angleStep * (i + 1));

                Debug.DrawLine(lineStart * radius + position, lineEnd * radius + position, color);
            }
        }

        public static void DrawRect(Vector3 position, Vector2 size, Color color)
        {
            var tl = new Vector3(-size.x / 2, 0, -size.y / 2);
            var tr = new Vector3(size.x / 2, 0, -size.y / 2);
            var bl = new Vector3(-size.x / 2, 0, size.y / 2);
            var br = new Vector3(size.x / 2, 0, size.y / 2);

            Debug.DrawLine(position + tl, position + tr, color);
            Debug.DrawLine(position + tr, position + br, color);
            Debug.DrawLine(position + br, position + bl, color);
            Debug.DrawLine(position + bl, position + tl, color);
        }

    }
}