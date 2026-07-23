#if GPIE_DEVELOPMENT
using System;
using System.Collections.Generic;
using Gameplay.Adapters;
using Gameplay.Core;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.Dev
{
    /// <summary>
    /// Dev/QA level-switch cheat (TASK-011): spawns an on-screen overlay to jump between campaign levels
    /// (◀ prev / ▶ next / direct jump) and shows the current level id, so a level can be verified without
    /// replaying the campaign or resetting saved progress. Editor/dev builds only (GPIE_DEVELOPMENT) — never
    /// compiled into player builds; the whole file is guarded. Reads gameplay state read-only (level ids,
    /// current level, campaign index) and drives every switch through <see cref="CampaignProgressionController"/>
    /// so the campaign pointer stays in sync (no resume snap-back). This is the reachable dev tool for the
    /// isolated <c>ShapeFillBringup</c> scene, which deliberately runs without SayKit/GlobalService and so
    /// never raises the SayKit DynamicDebugMenu (GameDebugMenu) path.
    /// </summary>
    public sealed class LevelCheatController : IStartable, IDisposable
    {
        private readonly ILevelSource _levelSource;
        private readonly GameModel _model;
        private readonly CampaignProgressionController _campaign;
        private readonly List<string> _levelIds = new();

        private LevelCheatOverlay _overlay;

        public LevelCheatController(ILevelSource levelSource, GameModel model, CampaignProgressionController campaign)
        {
            _levelSource = levelSource;
            _model = model;
            _campaign = campaign;
        }

        public int LevelCount => _levelIds.Count;

        public int CurrentIndex => _campaign.DevCampaignIndex;

        public string CurrentLevelId => _model.CurrentLevel == null ? "<none>" : _model.CurrentLevel.LevelId;

        public void Start()
        {
            // Cache the ordered level ids once (Load returns the pre-parsed cached level — cheap, no re-parse),
            // so the IMGUI overlay never touches the level source per repaint.
            for (var i = 0; i < _levelSource.Count; i++)
                _levelIds.Add(_levelSource.Load(i)?.LevelId ?? $"#{i}");

            var go = new GameObject("[Dev] LevelCheatOverlay");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _overlay = go.AddComponent<LevelCheatOverlay>();
            _overlay.Bind(this);
        }

        public string LevelIdAt(int index) =>
            index >= 0 && index < _levelIds.Count ? _levelIds[index] : $"#{index}";

        /// <summary>Jump to an absolute campaign index; out-of-range is ignored (clamped by the campaign hook).</summary>
        public void JumpTo(int index)
        {
            if (index < 0 || index >= _levelSource.Count)
                return;
            _campaign.DevJumpToLevel(index);
        }

        public void Dispose()
        {
            if (_overlay != null)
                UnityEngine.Object.Destroy(_overlay.gameObject);
            _overlay = null;
        }
    }
}
#endif
