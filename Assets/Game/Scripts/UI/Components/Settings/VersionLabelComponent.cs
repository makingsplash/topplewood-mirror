using DataStorage;
using Services;
using TMPro;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Settings
{
    [RequireComponent(typeof(TMP_Text))]
    [MovedFrom(sourceNamespace: "UI.Components.Settings")]
    internal sealed class VersionLabelComponent : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private string _prefix;
        

        private void OnEnable()
        {
            if (!Data.IsReady())
            {
                if (_label != null)
                {
                    _label.text = _prefix + Application.version;
                }
                return;
            }
            
            string appVersion = Data.App.Version;
#if SAYKIT
            if (GlobalService.SayKit != null)
            {
                appVersion = GlobalService.SayKit.GetAppVersion();
            }
#endif
            
            SetupLabel(appVersion, Data.App.BuildNumber, Data.App.GitCommit);
        }

        private void SetupLabel(string version, string buildNum, string gitCommit)
        {
            if (_label != null)
            {
                var shortHash = gitCommit.Length > 7 ? gitCommit.Substring(0, 7) : gitCommit;
                _label.text = _prefix + string.Format("{0}({1})[{2}]", version, buildNum, shortHash);
            }
        }

        private void OnValidate()
        {
            if (_label == null)
            {
                _label = GetComponent<TMP_Text>();
            }
        }
    }
}
