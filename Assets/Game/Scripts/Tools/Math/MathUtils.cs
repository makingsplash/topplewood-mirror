
using System.Collections.Generic;
using UnityEngine;

namespace Game.Tools.Math
{
	public static class MathUtils
	{
		public static List<Vector2> GetFilledArea(Vector2 origin, float radius, int count)
		{
			var result = new List<Vector2>(Mathf.Max(0, count));
			if (count <= 0 || radius <= 0f)
				return result;

			// Алгоритм Фибоначчи-диска (sunflower) — равномерное заполнение круга
			float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f)); // ~2.399963

			for (int i = 0; i < count; i++)
			{
				float t = (i + 0.5f) / count;     // (0,1]
				float r = radius * Mathf.Sqrt(t); // радиальная координата для равномерности по площади
				float theta = i * goldenAngle;

				float x = origin.x + r * Mathf.Cos(theta);
				float y = origin.y + r * Mathf.Sin(theta);

				result.Add(new Vector2(x, y));
			}

			return result;
		}
	}
}
