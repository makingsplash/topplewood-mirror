using System.IO;
using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-528: the validator of the REAL pool list — <c>Assets/StreamingAssets/shapefill_pool.json</c> against the
    /// real line-up manifest, read through the same <see cref="JsonLevelSource"/> and <see cref="LevelPool"/> the game
    /// uses. The runtime never throws on a bad pool (it skips and falls back); this gate is where the strictness lives:
    /// every id exists, no duplicates, no fallback. Proven by mutation: an unknown id written into the file turns
    /// <see cref="RealPool_HasNoProblems"/> red with that id in the message (04-dev-implementer.md §Мутации).
    /// Checks presence/uniqueness only — the circle rules (exclusions, T=4 spacing, main feature across the seam) are
    /// tech-debt TASK-529.
    /// </summary>
    [TestFixture]
    public sealed class LevelPoolDataTests
    {
        private const string StreamingAssetsDir = "Assets/StreamingAssets";

        private JsonLevelSource _lineup;
        private LevelPool _pool;

        [OneTimeSetUp]
        public void Load()
        {
            var store = new FileStore();
            _lineup = new JsonLevelSource(store, new LevelDataValidator());
            _pool = new LevelPool(store, _lineup);
        }

        [Test]
        public void RealPool_HasNoProblems()
        {
            Assert.That(_pool.Problems, Is.Empty, string.Join("\n", _pool.Problems));
            Assert.IsFalse(_pool.IsFallback);
        }

        [Test]
        public void RealPool_SizeIsWithinTheLineup()
        {
            Assert.That(_pool.Count, Is.InRange(1, _lineup.Count));
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
