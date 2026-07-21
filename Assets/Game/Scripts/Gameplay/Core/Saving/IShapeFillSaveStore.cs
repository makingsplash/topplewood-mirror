namespace Gameplay.Core
{
    /// <summary>
    /// Persistence sink seam for the shape_fill snapshot JSON (RFC §4.1). The domain writes/reads an
    /// opaque string; the concrete store (file / PlayerPrefs / cloud) is a Unity-side adapter, swappable
    /// behind this interface (TECH_DEBT: file → PlayerPrefs/cloud).
    /// </summary>
    public interface IShapeFillSaveStore
    {
        void Write(string json);
        string Read();
        bool Exists();
        void Clear();
    }
}
