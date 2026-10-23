using System;
using System.Collections.Generic;
using Iliai.UiBuild;
using SGS.Services.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Editor.UiBuild
{
    /// <summary>
    /// `behaviour: Sound` — the click sound of a button (TASK-667): <c>{ "type": "Sound", "id": "Button" }</c> puts a
    /// <see cref="ButtonSound"/> on the node; it plays on pointer-down and only while the button is interactable.
    /// Project handler (SayKit-stack AudioService), not core. List it AFTER <c>Button</c> in the node's behaviours:
    /// ButtonSound requires a Selectable, and the handler refuses to let Unity add a bare one silently.
    /// The id must name an existing SoundEffect asset — a typo is a red build, not a silent button.
    /// </summary>
    [UiHandler("Sound")]
    public sealed class SoundHandler : IUiBehaviourHandler
    {
        /// <summary>Where the game's SoundEffect assets live — the same folder the Addressables entry
        /// <c>SoundEffects</c> and AudioCodeGenerator point at.</summary>
        public const string SoundEffectsFolder = "Assets/Game/Audio/SoundEffects";

        public IEnumerable<Type> ManagedTypes => new[] { typeof(ButtonSound) };

        public void Apply(UiBuildContext ctx, GameObject target, UiBehaviourSpec spec)
        {
            UiReader r = spec.Reader(ctx.Report);
            string id = r.Str("id");
            r.Finish();

            if (string.IsNullOrEmpty(id))
            {
                ctx.Report.Error(spec.Where, "Sound без 'id' — имя ассета SoundEffect (Button, Hint, …)");
                return;
            }
            if (!RequireSoundAsset(ctx.Report, spec.Where, id))
                return;
            if (target.GetComponent<Selectable>() == null)
            {
                ctx.Report.Error(spec.Where, "Sound на узле без кнопки: поставь behaviour 'Button' раньше 'Sound' в списке");
                return;
            }

            var sound = target.GetComponent<ButtonSound>() ?? target.AddComponent<ButtonSound>();
            var so = new SerializedObject(sound);
            SerializedProperty idProp = so.FindProperty("_soundId");
            if (idProp == null)
            {
                ctx.Report.Error(spec.Where, "ButtonSound изменился: не нашёл поле '_soundId' — хэндлер надо обновить");
                return;
            }
            idProp.stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Error (and false) unless <c>SoundEffectsFolder/&lt;id&gt;.asset</c> is a SoundEffect.</summary>
        public static bool RequireSoundAsset(UiBuildReport report, string where, string id)
        {
            string path = $"{SoundEffectsFolder}/{id}.asset";
            if (AssetDatabase.LoadAssetAtPath<SoundEffect>(path) != null)
                return true;
            report.Error(where, $"звук '{id}': нет ассета SoundEffect {path}");
            return false;
        }
    }
}
