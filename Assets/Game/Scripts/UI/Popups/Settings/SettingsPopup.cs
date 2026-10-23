using DataStorage;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
using Services;
using TMPro;
using Ui.Components.Settings;
using Ui.Screens;
using UnityEngine;
using UnityEngine.UI;
using Toggle = Ui.Components.SliderToggle.SliderToggle;

namespace Ui.Popups
{
    /// <summary>
    /// Settings popup, owner's variant V2 (TASK-566): three tiles (sound, music, vibration) + link plates (Privacy
    /// Policy, Privacy Settings, Support) + version. The RestartPopup family: ✕, a tap on the dim and system back close it.
    /// Opened by the HUD gear (<c>SettingsGearBinder</c>); while it is up the game is paused and gameplay input is off
    /// (<c>GameplayInputScreenBlocker</c>, keyed on <see cref="ScreenNames.SettingsScreen"/>).
    ///
    /// «Sound» and «Music» write their flag AND apply it to the mixer at once (TASK-667): AudioConfig.asset references
    /// AudioMixer.mixer with exposed SoundsVolume/MusicVolume, loaded by LoadingGame. The saved value is applied at the
    /// session start by SoundSettingsApplier. «Vibration» still only writes its flag (haptics are parked, TASK-568).
    ///
    /// State is decided in <see cref="Init"/>, not later: the intro (ScreenReveal) measures the card on the first
    /// Update, so the consent row must be on/off before that, or the card group would re-layout mid-intro.
    /// </summary>
    public sealed class SettingsPopup : BaseScreen, IBackButtonHandler
    {
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Button _dismissArea;

        [SerializeField] private Toggle _soundToggle;
        [SerializeField] private Toggle _musicToggle;
        [SerializeField] private Toggle _hapticToggle;

        [SerializeField] private Button _privacyPolicyButton;
        [SerializeField] private Button _privacySettingsButton;
        [SerializeField] private Button _supportButton;

        [SerializeField] private TMP_Text _versionLabel;

        public override void Init()
        {
            base.Init();
            _cancelButton.onClick.AddListener(Close);
            _dismissArea.onClick.AddListener(Close);
            _privacyPolicyButton.onClick.AddListener(OnPrivacyPolicy);
            _privacySettingsButton.onClick.AddListener(OnPrivacySettings);
            _supportButton.onClick.AddListener(OnSupport);
            _soundToggle.OnChangedValue += OnSoundChanged;
            _musicToggle.OnChangedValue += OnMusicChanged;
            _hapticToggle.OnChangedValue += OnHapticChanged;

            _soundToggle.SetValue(Data.Settings.SoundEnabled.Value, triggerCallback: false);
            _musicToggle.SetValue(Data.Settings.MusicEnabled.Value, triggerCallback: false);
            _hapticToggle.SetValue(Data.Settings.HapticEnabled.Value, triggerCallback: false);

            // Re-read on every show (the popup is instantiated anew each time): consent applicability is the SDK's call.
            _privacySettingsButton.gameObject.SetActive(GlobalService.SayKit.IsGdprApplicable());
            _versionLabel.text = FormatVersion();
        }

        // "v1.0.0 (100) [abc1234]" as on the approved mock. Empty parts are dropped: in the editor the SDK and Data.App
        // report no version, build or hash, and a bare "()[]" reads as two broken glyphs at caption size.
        private static string FormatVersion()
        {
            VersionLabelComponent.ReadVersion(out var version, out var buildNum, out var shortHash);
            if (string.IsNullOrEmpty(version))
                version = Application.version;

            var text = "v" + version;
            if (!string.IsNullOrEmpty(buildNum))
                text += " (" + buildNum + ")";
            if (!string.IsNullOrEmpty(shortHash))
                text += " [" + shortHash + "]";
            return text;
        }

        public override void DeInit()
        {
            base.DeInit();
            _cancelButton.onClick.RemoveListener(Close);
            _dismissArea.onClick.RemoveListener(Close);
            _privacyPolicyButton.onClick.RemoveListener(OnPrivacyPolicy);
            _privacySettingsButton.onClick.RemoveListener(OnPrivacySettings);
            _supportButton.onClick.RemoveListener(OnSupport);
            _soundToggle.OnChangedValue -= OnSoundChanged;
            _musicToggle.OnChangedValue -= OnMusicChanged;
            _hapticToggle.OnChangedValue -= OnHapticChanged;
        }

        public void OnBackButton() => Close();

        private static void OnSoundChanged(bool value)
        {
            WriteFlag(Data.Settings.SoundEnabled, value);
            var audio = GlobalService.AudioService;
            if (audio != null)
                audio.EnableSounds(value);
        }

        // No music in the game yet; the flag is still applied honestly, so music added later obeys it from day one.
        private static void OnMusicChanged(bool value)
        {
            WriteFlag(Data.Settings.MusicEnabled, value);
            var audio = GlobalService.AudioService;
            if (audio != null)
                audio.EnableMusic(value);
        }

        // The vibration tile writes HapticEnabled; VibrationEnabled is a separate legacy flag left alone (designer §4).
        private static void OnHapticChanged(bool value) => WriteFlag(Data.Settings.HapticEnabled, value);

        /// <summary>
        /// Writes a tile's flag and flushes it to disk at once. The setter only hands the value to the PlayerPrefs backend;
        /// the prod scope flushes on pause/focus/quit, so a kill while the popup has focus (crash, OOM, force-stop) lost
        /// the flag (TASK-566 QA S3(б)). Same explicit flush as the hint/campaign writes (TASK-537).
        /// </summary>
        public static void WriteFlag(PlayerPrefsBool flag, bool value)
        {
            flag.Value = value;
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();
        }

        private static void OnPrivacyPolicy()
        {
            // The plate is visible even when the SDK has no link (editor bridge returns "", smoke builds) — decided at
            // the gate; an empty URL is logged, not opened.
            var url = GlobalService.SayKit.GetPrivacyPolicyLink();
            if (string.IsNullOrEmpty(url))
            {
                Debug.unityLogger.Log(LogType.Warning,
                    "[settings] SDK вернул пустую ссылку на политику конфиденциальности (редактор/smoke) — не открываю");
                return;
            }
            Application.OpenURL(url);
        }

        private static void OnPrivacySettings() => GlobalService.SayKit.RevokeGdpr();

        private static void OnSupport() => GlobalService.SayKit.OpenSupportPage();
    }
}
