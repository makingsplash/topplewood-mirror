using System.Collections.Generic;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Кто с кем соприкасается в СОБРАННОЙ фигуре уровня — граф по видам деталей. Один на два потребителя:
    /// цвет (<c>Game.Editor.WoodIttenPalette</c>: соседи отбиваются тоном) и волокно
    /// (<see cref="WoodGrainAssignment"/>, TASK-559: соседи получают разную доску). Жил внутри редакторской
    /// раскраски; вынесен сюда, чтобы рантайм раздавал варианты волокна по тому же графу, а не по копии.
    ///
    /// Именно собранная фигура: игрок судит о материале по финальной картинке, а на старте детали висят
    /// порознь и соседей там нет вовсе.
    /// </summary>
    public static class WoodKindNeighbours
    {
        /// <summary>Зазор, при котором две детали считаются соседями (мировые единицы).</summary>
        public const float TouchEpsilon = 0.06f;

        /// <summary>Вид → форма: первая деталь уровня с этим видом. Слот несёт только имя вида.</summary>
        public static Dictionary<string, Shape> ShapesByKind(LevelData level)
        {
            var shapes = new Dictionary<string, Shape>();
            if (level?.Pieces == null)
                return shapes;
            foreach (var placement in level.Pieces)
            {
                var type = placement.Type;
                if (type?.Shape != null && !shapes.ContainsKey(type.Kind))
                    shapes[type.Kind] = type.Shape;
            }
            return shapes;
        }

        /// <summary>Граф касаний видов в целевой позе. Вид сам с собой не связывается.</summary>
        public static Dictionary<string, List<string>> Build(LevelData level, Dictionary<string, Shape> shapes)
        {
            var result = new Dictionary<string, List<string>>();
            var slots = level?.Target?.Slots;
            if (slots == null)
                return result;

            var posed = new List<(string kind, Vector2[] poly)>();
            foreach (var slot in slots)
            {
                if (!shapes.TryGetValue(slot.Kind, out var shape))
                    continue;
                posed.Add((slot.Kind, Pose(shape, slot.Position, slot.Rotation)));
            }

            for (var i = 0; i < posed.Count; i++)
            {
                for (var j = i + 1; j < posed.Count; j++)
                {
                    if (posed[i].kind == posed[j].kind)
                        continue;
                    if (Separation(posed[i].poly, posed[j].poly) > TouchEpsilon)
                        continue;
                    Link(result, posed[i].kind, posed[j].kind);
                    Link(result, posed[j].kind, posed[i].kind);
                }
            }
            return result;
        }

        /// <summary>Площадь вида в собранной фигуре — разрулить ничью по связности (крупное заметнее).</summary>
        public static Dictionary<string, float> Masses(LevelData level, Dictionary<string, Shape> shapes)
        {
            var result = new Dictionary<string, float>();
            var slots = level?.Target?.Slots;
            if (slots != null)
            {
                foreach (var slot in slots)
                {
                    if (!shapes.TryGetValue(slot.Kind, out var shape))
                        continue;
                    result.TryGetValue(slot.Kind, out var acc);
                    result[slot.Kind] = acc + Area(shape);
                }
            }
            foreach (var kind in shapes.Keys)
                if (!result.ContainsKey(kind))
                    result[kind] = Area(shapes[kind]);
            return result;
        }

        private static void Link(Dictionary<string, List<string>> map, string a, string b)
        {
            if (!map.TryGetValue(a, out var list))
                map[a] = list = new List<string>();
            if (!list.Contains(b))
                list.Add(b);
        }

        private static Vector2[] Pose(Shape shape, Vec2 position, float rotation)
        {
            var c = Mathf.Cos(rotation);
            var s = Mathf.Sin(rotation);
            var v = shape.Vertices;
            var result = new Vector2[v.Length];
            for (var i = 0; i < v.Length; i++)
                result[i] = new Vector2(
                    v[i].X * c - v[i].Y * s + position.X,
                    v[i].X * s + v[i].Y * c + position.Y);
            return result;
        }

        /// <summary>
        /// Зазор между двумя ВЫПУКЛЫМИ многоугольниками по теореме о разделяющей оси: положительное
        /// число — расстояние, отрицательное — глубина пересечения. У составной freeform-формы берётся её
        /// внешний контур <see cref="Shape.Vertices"/> — для «касаются или нет» этого достаточно.
        /// </summary>
        private static float Separation(Vector2[] a, Vector2[] b)
        {
            var best = float.MinValue;
            best = Mathf.Max(best, SeparationAlong(a, a, b));
            best = Mathf.Max(best, SeparationAlong(b, a, b));
            return best;
        }

        private static float SeparationAlong(Vector2[] axesFrom, Vector2[] a, Vector2[] b)
        {
            var best = float.MinValue;
            for (var i = 0; i < axesFrom.Length; i++)
            {
                var edge = axesFrom[(i + 1) % axesFrom.Length] - axesFrom[i];
                var len = edge.magnitude;
                if (len <= 1e-6f)
                    continue;
                var axis = new Vector2(-edge.y / len, edge.x / len);
                Project(a, axis, out var a0, out var a1);
                Project(b, axis, out var b0, out var b1);
                var gap = Mathf.Max(b0 - a1, a0 - b1);
                if (gap > best)
                    best = gap;
            }
            return best;
        }

        private static void Project(Vector2[] poly, Vector2 axis, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (var p in poly)
            {
                var d = Vector2.Dot(p, axis);
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }

        private static float Area(Shape shape)
        {
            var v = shape.Vertices;
            if (v == null || v.Length < 3)
                return 0f;
            var sum = 0f;
            for (var i = 0; i < v.Length; i++)
            {
                var a = v[i];
                var b = v[(i + 1) % v.Length];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return Mathf.Abs(sum) * 0.5f;
        }
    }
}
