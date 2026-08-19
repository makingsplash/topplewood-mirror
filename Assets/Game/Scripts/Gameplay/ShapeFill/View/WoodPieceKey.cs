using System.Globalization;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Имя, под которым деталь получает СВОЮ настройку дерева (<see cref="WoodTuning.PieceOverrides"/>).
    ///
    /// Ключ — это ФОРМА (примитив + габарит), а НЕ пара «уровень/kind». Причина не в экономии кода:
    /// в кампании 85 типов деталей, но всего 72 разные формы — например, квадрат 0.40×0.40 живёт
    /// сразу в четырёх уровнях (<c>grand_seesaw/weight</c>, <c>well/bucket</c>,
    /// <c>libra_scales/weight</c>, <c>piggy_bank/snout</c>), а брус 2.80×0.30 в двух. Ключуй мы по
    /// уровню — владельцу пришлось бы настраивать один и тот же кубик четыре раза, и они бы
    /// неизбежно разъехались между уровнями. Ровно на «всё разное» он и жалуется.
    ///
    /// Побочная выгода: ключ выводится из самой формы, поэтому его знает и <see cref="PieceView"/>,
    /// и <see cref="PieceMeshBuilder"/>, и стенд — плести levelId через фабрику и DI не нужно.
    ///
    /// Формат: <c>Box 0.40x0.40</c>. Габарит округляется до сотых — это шаг, которым размеры
    /// заданы в <c>shapefill_levels.json</c>; более мелкая точность плодила бы ключи-двойники из-за
    /// плавающей арифметики тесселяции.
    /// </summary>
    public static class WoodPieceKey
    {
        /// <summary>Ключ по доменной форме. Габарит берётся по вершинам примитива, а НЕ по
        /// визуальному контуру: у круга и купола визуальный контур тесселируется гуще
        /// (<see cref="ShapeVisualOutline"/>), и ключ поехал бы от числа сегментов.</summary>
        public static string Of(Shape shape)
        {
            if (shape?.Vertices == null || shape.Vertices.Length == 0)
                return null;

            Bounds(shape.Vertices, out var width, out var height);
            return Format(shape.Primitive.ToString(), width, height);
        }

        /// <summary>Ключ по готовому контуру — для мешей, собранных без доменной формы.
        /// Примитив там неизвестен, поэтому в имени стоит прочерк.</summary>
        public static string Of(Vec2[] outline)
        {
            if (outline == null || outline.Length == 0)
                return null;

            Bounds(outline, out var width, out var height);
            return Format("-", width, height);
        }

        /// <summary>
        /// Ключ статики уровня (пол, стена, колонна, штырь). Отдельный префикс, а не общий с деталями:
        /// в кампании 39 статик и среди них есть блоки того же габарита, что падающие детали, — но
        /// настраивать пол и падающий кубик одним числом заведомо неверно, у них разная роль в кадре.
        /// </summary>
        public static string OfStatic(Vec2[] outline)
        {
            if (outline == null || outline.Length == 0)
                return null;

            Bounds(outline, out var width, out var height);
            return Format("Статика", width, height);
        }

        /// <summary>Короткая сторона габарита — по ней считается масштаб толщин стиля.</summary>
        public static float ShortSide(Vec2[] verts)
        {
            Bounds(verts, out var width, out var height);
            return Mathf.Max(Mathf.Min(width, height), 1e-4f);
        }

        private static string Format(string primitive, float width, float height)
            => string.Format(CultureInfo.InvariantCulture, "{0} {1:0.00}x{2:0.00}", primitive, width, height);

        private static void Bounds(Vec2[] verts, out float width, out float height)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in verts)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            width = maxX - minX;
            height = maxY - minY;
        }
    }
}
