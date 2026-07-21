namespace Gameplay.Core
{
    /// <summary>
    /// Domain-agnostic save facade (RFC §4.1): captures/restores the session without <c>SaveManager</c>
    /// knowing about <see cref="GameModel"/>. Unity-free — the concrete store lives behind
    /// <see cref="IShapeFillSaveStore"/>.
    /// </summary>
    public interface IProgressSaver
    {
        /// <summary>Serialize the current session snapshot to the store.</summary>
        void Save();

        /// <summary>Restore a stored snapshot onto the already-loaded level; false if none/foreign.</summary>
        bool TryRestore();
    }
}
