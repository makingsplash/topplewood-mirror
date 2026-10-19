using Gameplay.Core;
using Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-631: активная деталь рисуется поверх ждущей. Тесты держат правило слоёв (любая не-Idle ближе к камере любой
    /// Idle при любых индексах), устойчивый порядок внутри слоя, цепочку глубин с пунктиром и тенью, и что живой
    /// <see cref="PieceView"/> переставляет глубину на смене стейта сам — без следующего Sync.
    /// </summary>
    public sealed class PieceDepthTests
    {
        private static readonly PieceLifecycle[] Active =
            { PieceLifecycle.Falling, PieceLifecycle.Placed, PieceLifecycle.Lost };

        // Камера смотрит вдоль +Z: меньше z — ближе, то есть «выше» по слою.
        [Test]
        public void AnyActivePiece_IsNearerThanAnyIdlePiece_ForEveryOrdinalPair()
        {
            for (var active = 0; active < PieceDepth.Slots + 10; active++)
            for (var idle = 0; idle < PieceDepth.Slots + 10; idle++)
                foreach (var state in Active)
                    Assert.That(PieceDepth.For(state, active), Is.LessThan(PieceDepth.For(PieceLifecycle.Idle, idle)),
                        $"{state}#{active} не ближе Idle#{idle}");
        }

        [Test]
        public void InsideALayer_LowerOrdinalIsNearer_AndOrderIsStateIndependentAmongActives()
        {
            for (var i = 0; i < PieceDepth.Slots - 1; i++)
            {
                Assert.That(PieceDepth.For(PieceLifecycle.Idle, i),
                    Is.LessThan(PieceDepth.For(PieceLifecycle.Idle, i + 1)));
                Assert.That(PieceDepth.For(PieceLifecycle.Falling, i),
                    Is.LessThan(PieceDepth.For(PieceLifecycle.Placed, i + 1)),
                    "падающая и лежащая — один слой, порядок между ними тоже по индексу");
                Assert.That(PieceDepth.For(PieceLifecycle.Falling, i), Is.EqualTo(PieceDepth.For(PieceLifecycle.Placed, i)),
                    "Falling→Placed не должен двигать деталь по глубине");
            }
        }

        [Test]
        public void DepthChain_IdleDashAndShadowsStayBehindEveryPiece()
        {
            var deepestPiece = PieceDepth.For(PieceLifecycle.Idle, PieceDepth.Slots - 1);
            var nearestDash = PieceDepth.For(PieceLifecycle.Idle, 0) + IdleDashOutline.ZBehind;
            Assert.That(nearestDash, Is.GreaterThan(deepestPiece), "пунктир ждущей лёг бы на соседнюю деталь");

            // Тень — локальный сдвиг ≥ 0.15 за своей деталью (PieceShadow.ZBehind): у ближайшей активной она обязана
            // остаться за самой дальней ждущей, иначе тень падающей легла бы на висящую.
            var nearestShadow = PieceDepth.For(PieceLifecycle.Falling, 0) + PieceShadow.LocalOffset(0f, null).z;
            Assert.That(nearestShadow, Is.GreaterThan(deepestPiece));
        }

        [Test]
        public void PieceView_SetState_MovesDepthWithoutNextSync()
        {
            var go = new GameObject("piece");
            try
            {
                var view = go.AddComponent<PieceView>();
                view.Initialize(Shape.LTetromino(1f), "#40C040", "l", 3);
                view.SetState(PieceLifecycle.Idle);
                view.Sync(new Vec2(1f, 2f), 0f);
                Assert.That(go.transform.position.z, Is.EqualTo(PieceDepth.For(PieceLifecycle.Idle, 3)));

                view.SetState(PieceLifecycle.Falling);
                Assert.That(go.transform.position.z, Is.EqualTo(PieceDepth.For(PieceLifecycle.Falling, 3)));
                Assert.That(go.transform.position.x, Is.EqualTo(1f), "смена слоя не трогает позу");

                view.Sync(new Vec2(1f, 1.5f), 0.3f);
                Assert.That(go.transform.position.z, Is.EqualTo(PieceDepth.For(PieceLifecycle.Falling, 3)));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
