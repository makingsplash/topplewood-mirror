using Gameplay.Core;
using Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-557: две выборки, которые заменили арифметику в пикселе <c>Game/Wood Block</c>. Поле силуэта обязано
    /// повторять правило шейдерного <c>SilhouetteEdge</c> (знак добавлен ради билинейной выборки через кромку), а
    /// запечённый шум — тайлиться без шва и держать диапазон процедурного fbm, на который опираются числа досок.
    /// </summary>
    public sealed class WoodPixelCostTests
    {
        private static Vec2[] Square() => new[]
        {
            new Vec2(-1f, -1f), new Vec2(1f, -1f), new Vec2(1f, 1f), new Vec2(-1f, 1f),
        };

        // Тот же шеврон, что в SeamlessSilhouetteTests: (0,0) — вогнутая вершина.
        private static Vec2[] Chevron() => new[]
        {
            new Vec2(-1f, -1f), new Vec2(1f, -1f), new Vec2(0f, 0f), new Vec2(1f, 1f), new Vec2(-1f, 1f),
        };

        [Test]
        public void Field_InsidePositive_OutsideNegative_NormalOfNearestEdge()
        {
            SilhouetteField.Evaluate(Square(), 0.9f, 0f, out var dIn, out var nxIn, out var nyIn);
            Assert.That(dIn, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(nxIn, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(nyIn, Is.EqualTo(0f).Within(1e-5f));

            SilhouetteField.Evaluate(Square(), 1.1f, 0f, out var dOut, out var nxOut, out _);
            Assert.That(dOut, Is.EqualTo(-0.1f).Within(1e-5f));
            Assert.That(nxOut, Is.EqualTo(1f).Within(1e-5f), "снаружи нормаль та же — иначе выборка через кромку её перевернёт");
        }

        [Test]
        public void Field_ConvexCornerOutside_NormalPointsAwayFromVertex()
        {
            SilhouetteField.Evaluate(Square(), 1.1f, 1.1f, out var d, out var nx, out var ny);
            Assert.That(d, Is.EqualTo(-0.1f * Mathf.Sqrt(2f)).Within(1e-5f));
            Assert.That(nx, Is.EqualTo(Mathf.Sqrt(0.5f)).Within(1e-5f));
            Assert.That(ny, Is.EqualTo(Mathf.Sqrt(0.5f)).Within(1e-5f));
        }

        [Test]
        public void Field_ReflexWedge_NormalIsGradientTowardVertex()
        {
            // Слева от вогнутой вершины (0,0), внутри шеврона: ближайшая точка — сама вершина, нормаль — на неё
            // (правило TASK-368: в клине градиент поля, а не нормаль одного из рёбер).
            SilhouetteField.Evaluate(Chevron(), -0.2f, 0f, out var d, out var nx, out var ny);
            Assert.That(d, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(nx, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(ny, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Noise_StaysInProceduralFbmRange()
        {
            var v = WoodGrainNoise.Generate(64, 4, WoodGrainNoise.Octaves);
            foreach (var x in v)
                Assert.That(x, Is.InRange(0f, 0.875f));
        }

        [Test]
        public void Noise_TilesWithoutSeam()
        {
            const int size = 64;
            var v = WoodGrainNoise.Generate(size, 4, WoodGrainNoise.Octaves);

            // Самый крупный перепад между соседями внутри тайла — мерило гладкости. Шов (последний столбец → первый,
            // последняя строка → первая) обязан быть не грубее: иначе на детали шире периода шума виден прямой рубец.
            var interior = 0f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x + 1 < size; x++)
            {
                interior = Mathf.Max(interior, Mathf.Abs(v[y * size + x + 1] - v[y * size + x]));
                interior = Mathf.Max(interior, Mathf.Abs(v[x * size + y] - v[(x + 1) * size + y]));
            }

            for (var i = 0; i < size; i++)
            {
                Assert.That(Mathf.Abs(v[i * size] - v[i * size + size - 1]), Is.LessThanOrEqualTo(interior + 1e-6f), $"шов по x, строка {i}");
                Assert.That(Mathf.Abs(v[i] - v[(size - 1) * size + i]), Is.LessThanOrEqualTo(interior + 1e-6f), $"шов по y, столбец {i}");
            }
        }
    }
}
