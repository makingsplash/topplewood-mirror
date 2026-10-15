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
            if (_label == null)
            {
                return;
            }

            if (!ReadVersion(out var version, out var buildNum, out var shortHash))
            {
                _label.text = _prefix + Application.version;
                return;
            }

            _label.text = _prefix + string.Format("{0}({1})[{2}]", version, buildNum, shortHash);
        }

        /// <summary>
        /// The parts of the version line: SayKit's version when the SDK is compiled in (else <c>Data.App.Version</c>),
        /// build number and 7-char commit hash. False when Data is not ready yet — then only
        /// <see cref="Application.version"/> is known. Shared with the settings popup (TASK-566), which formats the same
        /// parts its own way (its label is wired by the UI compiler and cannot carry this component).
        /// </summary>
        internal static bool ReadVersion(out string version, out string buildNum, out string shortHash)
        {
            if (!Data.IsReady())
            {
                version = Application.version;
                buildNum = string.Empty;
                shortHash = string.Empty;
                return false;
            }

            version = Data.App.Version;
#if SAYKIT
            if (GlobalService.SayKit != null)
            {
                version = GlobalService.SayKit.GetAppVersion();
            }
#endif

            buildNum = Data.App.BuildNumber;
            var gitCommit = Data.App.GitCommit ?? string.Empty;
            shortHash = gitCommit.Length > 7 ? gitCommit.Substring(0, 7) : gitCommit;
            return true;
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
