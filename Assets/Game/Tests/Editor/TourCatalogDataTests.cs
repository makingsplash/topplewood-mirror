using System.Collections.Generic;
using System.IO;
using Gameplay.Core;
using Gameplay.DI;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-613 plan §5/§8 T6: every level the REAL tour catalog (<see cref="GameplayCoreInstaller.BuildTutorialCatalog"/>)
    /// names exists in the REAL line-up manifest and carries a non-empty winningOrder — the beats are built from it, and
    /// a catalog level without one would silently play no tour. The catalog lives in code, not in level data, so the
    /// level validator cannot hold this; this test does. Green today; it turns red the day a tour level loses its order
    /// or a tour is registered for a level id that is not in the manifest.
    /// </summary>
    [TestFixture]
    public sealed class TourCatalogDataTests
    {
        private const string StreamingAssetsDir = "Assets/StreamingAssets";

        [Test]
        public void EveryCatalogLevel_ExistsInManifest_WithWinningOrder()
        {
            var lineup = new JsonLevelSource(new FileStore(), new LevelDataValidator());
            var byId = new Dictionary<string, LevelData>();
            for (var i = 0; i < lineup.Count; i++)
            {
                var level = lineup.Load(i);
                if (level?.LevelId != null)
                    byId[level.LevelId] = level;
            }

            var catalog = GameplayCoreInstaller.BuildTutorialCatalog();
            var checkedAny = false;
            foreach (var id in catalog.LevelIds)
            {
                checkedAny = true;
                Assert.That(byId.TryGetValue(id, out var level), Is.True, $"tour level '{id}' is not in the manifest");
                Assert.That(level.WinningOrder, Is.Not.Null.And.Not.Empty, $"tour level '{id}' has no winningOrder");
                Assert.That(catalog.Resolve(id).Build(level), Is.Not.Null, $"tour for '{id}' did not build");
            }
            Assert.That(checkedAny, Is.True, "0 catalog levels checked is not green");
        }

        /// <summary>TASK-635 plan §5 T3: the hint tour sits inside the owner's "4–6" frame (line-up index 3..5) on a level
        /// where the Hint can actually fire — else the button in the HUD beat is inactive and the beat skips itself.</summary>
        [Test]
        public void HintTour_IsInTheOwnersFrame_OnAHintEligibleLevel()
        {
            var lineup = new JsonLevelSource(new FileStore(), new LevelDataValidator());
            var catalog = GameplayCoreInstaller.BuildTutorialCatalog();
            var levelId = catalog.LevelIdOf(Gameplay.Tutorial.BoosterTourRecipe.HintTourId);
            Assert.That(levelId, Is.Not.Null, "no level carries the hint tour");

            var index = -1;
            for (var i = 0; i < lineup.Count; i++)
                if (lineup.Load(i).LevelId == levelId)
                    index = i;
            Assert.That(index, Is.InRange(3, 5), $"hint tour level '{levelId}' is at line-up index {index}");

            var level = lineup.Load(index);
            Assert.That(Gameplay.Core.Mechanic.NextHintResolver.TapablePieceCount(level),
                Is.GreaterThanOrEqualTo(new Gameplay.Configs.BoostersConfig.HintData().MinTapablePieces));
            Assert.That(catalog.Resolve(levelId).Build(level), Is.Not.Null);
        }

        private sealed class FileStore : ILevelStore
        {
            public string ReadText(string name)
            {
                var path = Path.Combine(StreamingAssetsDir, name);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
        }
    }
}
