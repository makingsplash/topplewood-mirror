using System.Collections.Generic;
using Gameplay.Core;
using Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-559: раздача досок волокна (<see cref="WoodGrainAssignment"/>) — правило из
    /// <c>TASK-559/01-art-director.md</c> §3. Фикстура — «жираф» в коде: тело, две ноги одного вида, шея, голова; в
    /// собранной фигуре тело касается ног и шеи, шея — головы. Граф — дерево, значит трёх досок хватает, чтобы
    /// ни одна пара соседей не совпала.
    /// </summary>
    [TestFixture]
    public sealed class WoodGrainAssignmentTests
    {
        private const int Variants = 5;
        private const int PerLevel = 3;
        private const int StaticRow = 0;
        private const float PhaseRange = 10f;

        private static readonly MaterialProfile Wood = new MaterialProfile(1.0f, 0.6f, 0.05f);

        [Test]
        public void SameLevel_SameAssignment()
        {
            var a = Build(Giraffe("giraffe"));
            var b = Build(Giraffe("giraffe"));

            CollectionAssert.AreEqual(a.LevelSet, b.LevelSet);
            foreach (var kind in new[] { "body", "leg", "neck", "head" })
            {
                Assert.That(b.VariantByKind[kind], Is.EqualTo(a.VariantByKind[kind]), kind);
                Assert.That(b.PickFor(kind, 2), Is.EqualTo(a.PickFor(kind, 2)), kind);
            }
        }

        [Test]
        public void Neighbours_GetDifferentBoards()
        {
            var g = Build(Giraffe("giraffe"));

            Assert.That(g.SameVariantNeighbourPairs, Is.Zero);
            Assert.That(g.VariantByKind["leg"], Is.Not.EqualTo(g.VariantByKind["body"]));
            Assert.That(g.VariantByKind["neck"], Is.Not.EqualTo(g.VariantByKind["body"]));
            Assert.That(g.VariantByKind["head"], Is.Not.EqualTo(g.VariantByKind["neck"]));
        }

        /// <summary>
        /// Фикстура, на которой «реже занятая доска» БЕЗ графа соседей гарантированно ошибается: центр A касается B, C, D,
        /// а E — только B. Обход A, B, C, D, E; без графа D получает первую доску набора — ту же, что у A. Жираф этого не
        /// ловил: там распределение по занятости случайно разводит соседей (проверено поломкой реализации).
        /// </summary>
        [Test]
        public void Star_GraphPreventsCollisionThatUsageAloneMakes()
        {
            var g = Build(Star("star"));

            Assert.That(g.SameVariantNeighbourPairs, Is.Zero);
            foreach (var leaf in new[] { "b", "c", "d" })
                Assert.That(g.VariantByKind[leaf], Is.Not.EqualTo(g.VariantByKind["a"]), leaf);
            Assert.That(g.VariantByKind["e"], Is.Not.EqualTo(g.VariantByKind["b"]));
        }

        [Test]
        public void LevelSet_IsBoundedAndCoversEveryKind()
        {
            var g = Build(Giraffe("giraffe"));

            Assert.That(g.LevelSet.Count, Is.EqualTo(PerLevel));
            foreach (var row in g.VariantByKind.Values)
                Assert.That(g.LevelSet, Does.Contain(row));
        }

        [Test]
        public void SameKind_SameBoard_DifferentPhase()
        {
            // Две ноги — один вид: доска одна (пара остаётся парой), а рисунок обязан различаться фазой.
            var g = Build(Giraffe("giraffe"));
            var left = g.PickFor("leg", 1);
            var right = g.PickFor("leg", 2);

            Assert.That(right.x, Is.EqualTo(left.x));
            Assert.That(right.w, Is.EqualTo(left.w), "знак угла — от вида, не от экземпляра");
            Assert.That(new Vector2(right.y, right.z), Is.Not.EqualTo(new Vector2(left.y, left.z)));
            Assert.That(left.y, Is.InRange(0f, PhaseRange));
            Assert.That(left.z, Is.InRange(0f, PhaseRange));
        }

        [Test]
        public void Pick_EncodesRowPlusOne()
        {
            var g = Build(Giraffe("giraffe"));
            Assert.That(g.PickFor("body", 0).x, Is.EqualTo(g.VariantByKind["body"] + 1));
        }

        [Test]
        public void Static_AlwaysCalmBoard()
        {
            var pick = Build(Giraffe("giraffe")).PickForStatic("floor");

            Assert.That(pick.x, Is.EqualTo(StaticRow + 1));
            Assert.That(pick.w, Is.EqualTo(1f));
        }

        [Test]
        public void NoVariants_FallsBackToMaterialNumbers()
        {
            var g = WoodGrainAssignment.Build(Giraffe("giraffe"), 0, PerLevel, StaticRow, PhaseRange);

            Assert.That(g.PickFor("body", 0), Is.EqualTo(Vector4.zero));
            Assert.That(g.PickForStatic("floor"), Is.EqualTo(Vector4.zero));
        }

        [Test]
        public void UnknownKind_StillGetsBoardFromLevelSet()
        {
            var g = Build(Giraffe("giraffe"));
            var pick = g.PickFor("not_in_level", 0);

            Assert.That(g.LevelSet, Does.Contain((int)pick.x - 1));
        }

        // ------------------------------------------------------------------ фикстура

        private static WoodGrainAssignment Build(LevelData level)
            => WoodGrainAssignment.Build(level, Variants, PerLevel, StaticRow, PhaseRange);

        /// <summary>Позиции подобраны так, что касания выполняются при любом прочтении размеров Box (полные или
        /// половинные): пары-соседи перекрываются или стоят в пределах TouchEpsilon, не-соседи разнесены.</summary>
        private static LevelData Giraffe(string id)
        {
            var body = new PieceType("body", Shape.Box(2f, 0.5f), Wood);
            var leg = new PieceType("leg", Shape.Box(0.2f, 1f), Wood);
            var neck = new PieceType("neck", Shape.Box(0.2f, 0.8f), Wood);
            var head = new PieceType("head", Shape.Box(0.5f, 0.3f), Wood);

            var pieces = new List<PiecePlacement>
            {
                new PiecePlacement(body, new Vec2(-4f, 0f)),
                new PiecePlacement(leg, new Vec2(-3f, 0f)),
                new PiecePlacement(leg, new Vec2(-2f, 0f)),
                new PiecePlacement(neck, new Vec2(-1f, 0f)),
                new PiecePlacement(head, new Vec2(0f, 0f)),
            };

            var slots = new List<TargetSlot>
            {
                new TargetSlot("body", new Vec2(0f, 1f), 0f),
                new TargetSlot("leg", new Vec2(-0.8f, 0.3f), 0f),
                new TargetSlot("leg", new Vec2(0.8f, 0.3f), 0f),
                new TargetSlot("neck", new Vec2(0.9f, 1.6f), 0f),
                new TargetSlot("head", new Vec2(0.9f, 2.2f), 0f),
            };

            return new LevelData { LevelId = id, Pieces = pieces, Target = new TargetFigure(null, slots) };
        }

        /// <summary>Центр A (1×1) и листья 0.5×0.5: B, C, D вплотную к A с трёх сторон, E вплотную к B снаружи.
        /// Листья между собой разнесены на 0.25 (больше TouchEpsilon).</summary>
        private static LevelData Star(string id)
        {
            var center = Shape.Box(1f, 1f);
            var leaf = Shape.Box(0.5f, 0.5f);
            var kinds = new[] { ("a", center, 0f, 0f), ("b", leaf, 0.75f, 0f), ("c", leaf, -0.75f, 0f),
                                ("d", leaf, 0f, 0.75f), ("e", leaf, 1.25f, 0f) };

            var pieces = new List<PiecePlacement>();
            var slots = new List<TargetSlot>();
            foreach (var (kind, shape, x, y) in kinds)
            {
                pieces.Add(new PiecePlacement(new PieceType(kind, shape, Wood), new Vec2(x, y + 5f)));
                slots.Add(new TargetSlot(kind, new Vec2(x, y), 0f));
            }

            return new LevelData { LevelId = id, Pieces = pieces, Target = new TargetFigure(null, slots) };
        }
    }
}
