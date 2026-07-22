namespace Gameplay.Core
{
    /// <summary>
    /// Raw-text source seam for authored level content (TASK-009). Keeps <see cref="JsonLevelSource"/>
    /// Unity-free (domain discipline, overview §Слои): the concrete reader (StreamingAssets file / cloud)
    /// is a Unity-side adapter behind this interface, mirroring the <see cref="IShapeFillSaveStore"/> pattern.
    /// </summary>
    public interface ILevelStore
    {
        /// <summary>The stored text for <paramref name="name"/>, or null if it does not exist.</summary>
        string ReadText(string name);
    }
}
