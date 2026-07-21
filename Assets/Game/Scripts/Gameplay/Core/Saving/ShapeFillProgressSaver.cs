using Newtonsoft.Json;

namespace Gameplay.Core
{
    /// <summary>
    /// Domain implementation of <see cref="IProgressSaver"/> (RFC §4.1): bridges <see cref="GameModel"/>
    /// capture/restore with the opaque <see cref="IShapeFillSaveStore"/>. Newtonsoft with stable field
    /// names (schema v2). Restore returns false for an empty/foreign save so callers start fresh.
    /// </summary>
    public sealed class ShapeFillProgressSaver : IProgressSaver
    {
        private readonly GameModel _model;
        private readonly IShapeFillSaveStore _store;

        public ShapeFillProgressSaver(GameModel model, IShapeFillSaveStore store)
        {
            _model = model;
            _store = store;
        }

        public void Save()
        {
            var snapshot = _model.CaptureState();
            _store.Write(JsonConvert.SerializeObject(snapshot));
        }

        public bool TryRestore()
        {
            if (!_store.Exists())
                return false;

            var json = _store.Read();
            if (string.IsNullOrEmpty(json))
                return false;

            var snapshot = JsonConvert.DeserializeObject<GameSnapshot>(json);
            return snapshot != null && _model.RestoreState(snapshot);
        }
    }
}
