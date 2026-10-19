using System.Linq;
using Gameplay.Tutorial;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace Gameplay.Tests
{
    /// <summary>TASK-613 plan §8 T5: the "assemble the picture" recipe builds its beats from the level's winningOrder —
    /// a class of tours, not sailboat's hardcoded {0, 1}.</summary>
    [TestFixture]
    public sealed class TourRecipeTests
    {
        [Test]
        public void Build_TapPerWinningOrderEntry_ThenMatch()
        {
            var definition = new AssembleTourRecipe("t").Build(TourRig.TwoBoxLevel(new[] { 1, 0 }));

            Assert.That(definition.Id, Is.EqualTo("t"));
            Assert.That(definition.Steps.Select(s => s.Id), Is.EqualTo(new[] { "tap_1", "tap_2", "match" }));
            Assert.That(definition.Steps.Take(2).Cast<TapPieceStep>().Select(s => s.PlacementIndex),
                Is.EqualTo(new[] { 1, 0 }), "the order is the level's, not placement order");
        }

        [Test]
        public void Build_PrePlacedIndex_ItsBeatCompletesAtOnce()
        {
            using var rig = new TourRig();
            rig.Model.SetFieldAtRest(true);
            rig.Release(0); // the hull is no longer Idle when the tour reaches it
            var definition = new AssembleTourRecipe("t").Build(rig.Model.CurrentLevel);
            var completed = 0;

            using var _ = definition.Steps[0].Enter(rig.Context()).Subscribe(__ => completed++);

            Assert.That(completed, Is.EqualTo(1));
        }

        [TestCase(null)]
        [TestCase(new int[0])]
        public void Build_NoWinningOrder_NoTour(int[] order)
        {
            var level = TourRig.TwoBoxLevel();
            level.WinningOrder = order;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no winningOrder"));

            Assert.That(new AssembleTourRecipe("t").Build(level), Is.Null);
        }
    }
}
