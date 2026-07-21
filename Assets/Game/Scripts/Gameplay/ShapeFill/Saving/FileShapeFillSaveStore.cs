using System.IO;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.Adapters
{
    /// <summary>
    /// File-backed <see cref="IShapeFillSaveStore"/> (RFC §4.1): a single JSON file under
    /// <c>Application.persistentDataPath</c>. Self-contained (no DataStorage/PlayerPrefs) so it works in
    /// the isolated bringup scope and in production alike. Swap to PlayerPrefs/cloud later behind the seam.
    /// </summary>
    public sealed class FileShapeFillSaveStore : IShapeFillSaveStore
    {
        private const string FileName = "shapefill_save.json";

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public void Write(string json) => File.WriteAllText(FilePath, json);

        public string Read() => File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;

        public bool Exists() => File.Exists(FilePath);

        public void Clear()
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
    }
}
