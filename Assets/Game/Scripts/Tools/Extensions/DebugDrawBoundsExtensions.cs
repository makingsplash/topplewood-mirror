namespace Game.Tools.Extensions
{
	using UnityEngine;

	public static class DebugDrawBoundsExtensions
	{
		// Рисует AABB (Bounds)
		public static void DrawBounds(this Bounds bounds, Color color, float duration = 0f, bool depthTest = true)
		{
			var c = bounds.center;
			var e = bounds.extents;

			// 8 углов
			Vector3 p0 = c + new Vector3(-e.x, -e.y, -e.z);
			Vector3 p1 = c + new Vector3( e.x, -e.y, -e.z);
			Vector3 p2 = c + new Vector3( e.x, -e.y,  e.z);
			Vector3 p3 = c + new Vector3(-e.x, -e.y,  e.z);

			Vector3 p4 = c + new Vector3(-e.x,  e.y, -e.z);
			Vector3 p5 = c + new Vector3( e.x,  e.y, -e.z);
			Vector3 p6 = c + new Vector3( e.x,  e.y,  e.z);
			Vector3 p7 = c + new Vector3(-e.x,  e.y,  e.z);

			void L(Vector3 a, Vector3 b) => Debug.DrawLine(a, b, color, duration, depthTest);

			// Нижняя грань
			L(p0, p1); L(p1, p2); L(p2, p3); L(p3, p0);
			// Верхняя грань
			L(p4, p5); L(p5, p6); L(p6, p7); L(p7, p4);
			// Вертикальные рёбра
			L(p0, p4); L(p1, p5); L(p2, p6); L(p3, p7);
		}

		// Удобные перегрузки для Collider и Renderer
		public static void DrawBounds(this Collider collider, Color color, float duration = 0f, bool depthTest = true)
			=> collider.bounds.DrawBounds(color, duration, depthTest);

		public static void DrawBounds(this Renderer renderer, Color color, float duration = 0f, bool depthTest = true)
			=> renderer.bounds.DrawBounds(color, duration, depthTest);
	}
}
