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
            _level.text = SGS.Services.GlobalService.Localization.GetLocalizedString(_keyLevel, Data.Meta.CurrentLevel.Value);
        }
    }
}
