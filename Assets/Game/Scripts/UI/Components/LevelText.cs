using DataStorage;
using TMPro;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public class LevelText : MonoBehaviour
    {
        [SerializeField] private TMP_Text _level;
        [SerializeField] private string _keyLevel = "Level.LevelNo";

        private void Start()
        {
            // Track = subscribe + immediate first call: the label follows ANY source that moves the level — the
            // one-time file→prefs migration, a WIN advancing the campaign, a debug CurrentLevel reset — not just
            // whatever value happened to be set the frame Start ran. Mirrors RewardedStateService's OnChanged wiring.
            Data.Meta.CurrentLevel.Track(OnLevelChanged);
        }

        private void OnDestroy()
        {
            Data.Meta.CurrentLevel.Untrack(OnLevelChanged);
        }

        private void OnLevelChanged(int level)
        {
            _level.text = SGS.Services.GlobalService.Localization.GetLocalizedString(_keyLevel, level);
        }
    }
}
