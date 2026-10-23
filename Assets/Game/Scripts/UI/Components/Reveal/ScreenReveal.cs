using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using SGS.Services.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ui.Components.Reveal
{
    /// <summary>
    /// Show-animation of a UiService screen, driven by DATA (TASK-425, closes the IWindowAnimator gap of TASK-121):
    /// every node that carries a <see cref="RevealTrack"/> tweens from an offset state INTO its prefab values, so the
    /// last intro frame is the approved static layout by construction. Screen controllers need no code: UiService
    /// already routes <c>Show</c> through an <see cref="IWindowAnimator"/> on the root.
    ///
    /// Order of events (why the intro starts one Update late): UiService calls <see cref="Show"/> and then returns the
    /// controller, whose caller runs <c>Setup</c> in the same call stack — and Setup decides the branches (a hidden
    /// reward row, a missing ×2). So <see cref="Show"/> only hides the nodes and raises the tap catcher; the timeline
    /// is resolved on the first Update, when the branches are final, and t = 0 is that frame.
    ///
    /// Positions are read AFTER <c>Canvas.ForceUpdateCanvases</c>, so children of a layout group tween towards the
    /// position the group gave them. Nothing in a reveal may re-dirty that group mid-intro (no size/text change of
    /// a layout child): the group would snap the child back to its slot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScreenReveal : MonoBehaviour, IWindowAnimator
    {
        [SerializeField] private int _inputLagMs;
        [SerializeField] private bool _tapToComplete = true;

        private readonly List<Tween> _loops = new();
        private Node[] _nodes = Array.Empty<Node>();
        private RevealTapCatcher _catcher;
        private Sequence _intro;
        private bool _pending;
        // TASK-667: sound cues of the steps that carry one (RevealStep.SoundId). Held during CompleteIntro so a tap does
        // not fire every remaining cue in one frame.
        private RevealSoundCues _cues;

        /// <summary>True from <see cref="Show"/> until the buttons accept input.</summary>
        public bool IsRevealing => _catcher != null && _catcher.gameObject.activeSelf;

        /// <summary>Compiler entry point (`behaviour: ScreenReveal` on the screen root).</summary>
        public void Configure(int inputLagMs, bool tapToComplete)
        {
            _inputLagMs = inputLagMs;
            _tapToComplete = tapToComplete;
        }

        public void Show(Action onFinished)
        {
            var tracks = GetComponentsInChildren<RevealTrack>(true);
            _nodes = new Node[tracks.Length];
            for (var i = 0; i < tracks.Length; i++)
            {
                _nodes[i] = new Node(tracks[i], tracks[i].gameObject == gameObject);
                _nodes[i].ApplyStartState();
            }

            EnsureCatcher();
            _catcher.gameObject.SetActive(true);
            _pending = true;

            // Init right away, not at the end of the intro: the caller's Setup follows in the same stack and binds
            // the button callbacks; input is held by the catcher until the timeline unlocks it.
            onFinished?.Invoke();
        }

        public void Hide(Action onFinished)
        {
            // Hide animation is out of scope (TASK-425 decision); release immediately.
            KillAll();
            onFinished?.Invoke();
        }

        /// <summary>Fast-forward the intro to its final frame and unlock input (what a tap during the intro does).</summary>
        public void CompleteIntro()
        {
            if (_pending)
                Begin();
            // Complete(true) runs every pending callback in this frame — the sound cues among them. Hold them and play
            // only the latest one afterwards (RevealSoundCues).
            _cues?.BeginComplete();
            _intro?.Complete(true);
            _cues?.EndComplete();
        }

        private static void PlayCue(string id, float semitones)
        {
            // No service (a scene without the LoadingGame bootstrap) → silent.
            var audio = SGS.Services.GlobalService.AudioService;
            if (audio != null)
                audio.Play(id, 1f, semitones).Forget();
        }

        private void Update()
        {
            if (!_pending)
                return;
            Begin();
        }

        private void OnDestroy()
        {
            KillAll();
        }

        private void Begin()
        {
            _pending = false;
            Canvas.ForceUpdateCanvases();

            var timings = new RevealTrackTiming[_nodes.Length];
            for (var i = 0; i < _nodes.Length; i++)
                timings[i] = _nodes[i].Timing;
            var shifts = new int[_nodes.Length];
            var unlockMs = RevealTimeline.Resolve(timings, _inputLagMs, shifts);

            _intro = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            _cues ??= new RevealSoundCues(PlayCue);
            _cues.Clear();
            for (var i = 0; i < _nodes.Length; i++)
            {
                if (!_nodes[i].Active)
                {
                    // A branch Setup switched off: leave it in its prefab state, in case it is shown later.
                    _nodes[i].RestoreFinalState();
                    continue;
                }

                _nodes[i].Build(_intro, _loops, shifts[i], gameObject, _cues);
            }

            _intro.InsertCallback(unlockMs / 1000f, Unlock);
        }

        private void Unlock()
        {
            if (_catcher != null)
                _catcher.gameObject.SetActive(false);
        }

        private void OnCatcherTapped()
        {
            if (_tapToComplete)
                CompleteIntro();
        }

        private void EnsureCatcher()
        {
            if (_catcher != null)
                return;

            var go = new GameObject(nameof(RevealTapCatcher), typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            _catcher = go.AddComponent<RevealTapCatcher>();
            _catcher.raycastTarget = true;
            _catcher.Tapped += OnCatcherTapped;
        }

        private void KillAll()
        {
            _pending = false;
            _intro?.Kill();
            _intro = null;
            foreach (var loop in _loops)
                loop?.Kill();
            _loops.Clear();
            if (_catcher != null)
                _catcher.Tapped -= OnCatcherTapped;
        }

        /// <summary>One animated node: its track plus the prefab values every tween returns to.</summary>
        private sealed class Node
        {
            private readonly RevealTrack _track;
            private readonly RectTransform _rect;
            private readonly Vector3 _finalScale;
            private readonly CanvasGroup _group;
            private readonly Graphic _ownGraphic;
            private readonly float _finalAlpha;
            private readonly TMP_Text _text;
            private Vector2 _finalPosition;

            public Node(RevealTrack track, bool isRoot)
            {
                _track = track;
                _rect = (RectTransform)track.transform;
                _finalScale = _rect.localScale;
                _text = track.GetComponent<TMP_Text>();

                if (!UsesAlpha(track))
                    return;

                if (isRoot)
                {
                    // Root alpha = the background only; a CanvasGroup here would fade the whole screen.
                    _ownGraphic = track.GetComponent<Graphic>();
                    _finalAlpha = _ownGraphic != null ? _ownGraphic.color.a : 1f;
                }
                else
                {
                    _group = track.GetComponent<CanvasGroup>();
                    if (_group == null)
                        _group = track.gameObject.AddComponent<CanvasGroup>();
                    _finalAlpha = _group.alpha;
                }
            }

            public bool Active => _track.gameObject.activeInHierarchy;

            public RevealTrackTiming Timing => new(_track.FirstAtMs, _track.IntroEndMs, _track.SkipShiftMs, Active,
                _track.UnlocksInput);

            private float Alpha
            {
                get
                {
                    if (_group != null)
                        return _group.alpha;
                    return _ownGraphic != null ? _ownGraphic.color.a : 1f;
                }
                set
                {
                    if (_group != null)
                    {
                        _group.alpha = value;
                        return;
                    }

                    if (_ownGraphic == null)
                        return;
                    var color = _ownGraphic.color;
                    color.a = value;
                    _ownGraphic.color = color;
                }
            }

            /// <summary>Hide/shrink by the EARLIEST In step, so nothing flashes in its final state before t = 0.
            /// Position is not touched here: a layout group may still move the node before the intro begins.</summary>
            public void ApplyStartState()
            {
                RevealStep first = null;
                foreach (var step in _track.Steps)
                    if (step.Kind == RevealKind.In && (first == null || step.AtMs < first.AtMs))
                        first = step;
                if (first == null)
                    return;

                if (first.HasScale)
                    _rect.localScale = _finalScale * first.Scale;
                if (first.HasAlpha)
                    Alpha = _finalAlpha * first.Alpha;
            }

            public void RestoreFinalState()
            {
                _rect.localScale = _finalScale;
                if (_group != null || _ownGraphic != null)
                    Alpha = _finalAlpha;
            }

            public void Build(Sequence intro, List<Tween> loops, int shiftMs, GameObject link, RevealSoundCues cues)
            {
                _finalPosition = _rect.anchoredPosition;

                foreach (var step in _track.Steps)
                {
                    var at = Mathf.Max(0, step.AtMs - shiftMs) / 1000f;
                    var duration = step.DurationMs / 1000f;
                    if (!string.IsNullOrEmpty(step.SoundId))
                    {
                        var cue = cues.Add(at, step.SoundId, step.SoundSemitones);
                        intro.InsertCallback(at, () => cues.Fire(cue));
                    }
                    switch (step.Kind)
                    {
                        case RevealKind.In:
                            BuildIn(intro, step, at, duration);
                            break;
                        case RevealKind.Count:
                            BuildCount(intro, step, at, duration);
                            break;
                        case RevealKind.Punch:
                            var peak = _finalScale * step.Scale;
                            intro.Insert(at, _rect.DOScale(peak, duration / 2f).From(_finalScale, false)
                                .SetEase(Ease.OutQuad));
                            intro.Insert(at + duration / 2f, _rect.DOScale(_finalScale, duration / 2f)
                                .From(peak, false).SetEase(Ease.InQuad));
                            break;
                        case RevealKind.Loop:
                            BuildLoop(loops, step, at, duration, link);
                            break;
                    }
                }
            }

            // Every tween gets an EXPLICIT start (From, not set immediately): a nested tween left to capture its
            // start lazily would read whatever another step left on the same property (a punch starting from the
            // pop's 0.7), and Goto-backwards would rewind to that instead of the authored from-state.
            private void BuildIn(Sequence intro, RevealStep step, float at, float duration)
            {
                if (step.HasScale)
                {
                    var from = _finalScale * step.Scale;
                    _rect.localScale = from;
                    intro.Insert(at, _rect.DOScale(_finalScale, duration).From(from, false)
                        .SetEase(step.Ease, step.Overshoot));
                }

                if (step.HasOffset)
                {
                    var from = _finalPosition + step.Offset;
                    _rect.anchoredPosition = from;
                    intro.Insert(at, DOTween.To(() => _rect.anchoredPosition, v => _rect.anchoredPosition = v,
                        _finalPosition, duration).From(from, false).SetEase(step.Ease, step.Overshoot));
                }

                if (step.HasAlpha)
                {
                    // Alpha never takes the step's (possibly overshooting) curve: alpha > 1 is meaningless.
                    var alphaDuration = step.AlphaDurationMs > 0 ? step.AlphaDurationMs / 1000f : duration;
                    var from = _finalAlpha * step.Alpha;
                    Alpha = from;
                    intro.Insert(at, DOTween.To(() => Alpha, v => Alpha = v, _finalAlpha, alphaDuration)
                        .From(from, false).SetEase(Ease.OutQuad));
                }
            }

            private void BuildCount(Sequence intro, RevealStep step, float at, float duration)
            {
                if (_text == null || !TrySplitNumber(_text.text, out var prefix, out var target, out var suffix))
                {
                    Debug.LogWarning($"[ScreenReveal] Count step on '{_track.name}': no integer in its text — skipped.");
                    return;
                }

                var from = Mathf.RoundToInt(step.CountFrom);
                var shown = from;
                _text.text = prefix + from + suffix;
                intro.Insert(at, DOTween.To(() => (float)from, v =>
                {
                    var value = Mathf.RoundToInt(v);
                    if (value == shown)
                        return;
                    shown = value;
                    _text.text = prefix + value + suffix;
                }, target, duration).From((float)from, false).SetEase(step.Ease));
                // Land on the exact value the controller wrote, whatever the rounding did on the way.
                intro.InsertCallback(at + duration, () => _text.text = prefix + target + suffix);
            }

            private void BuildLoop(List<Tween> loops, RevealStep step, float at, float period, GameObject link)
            {
                var leg = period / 2f;
                if (step.HasScale)
                    loops.Add(_rect.DOScale(_finalScale * step.Scale, leg).From(_finalScale, false).SetEase(step.Ease)
                        .SetLoops(-1, LoopType.Yoyo).SetDelay(at).SetUpdate(true).SetLink(link));
                if (step.HasAlpha && (_group != null || _ownGraphic != null))
                    loops.Add(DOTween.To(() => Alpha, v => Alpha = v, _finalAlpha * step.Alpha, leg)
                        .From(_finalAlpha, false).SetEase(step.Ease)
                        .SetLoops(-1, LoopType.Yoyo).SetDelay(at).SetUpdate(true).SetLink(link));
            }

            private static bool UsesAlpha(RevealTrack track)
            {
                foreach (var step in track.Steps)
                    if (step.HasAlpha && (step.Kind == RevealKind.In || step.Kind == RevealKind.Loop))
                        return true;
                return false;
            }

            /// <summary>"+70" → ("+", 70, ""); "x 1 200"-style grouping is not supported (first digit run only).</summary>
            private static bool TrySplitNumber(string text, out string prefix, out int value, out string suffix)
            {
                prefix = suffix = string.Empty;
                value = 0;
                if (string.IsNullOrEmpty(text))
                    return false;

                var start = -1;
                for (var i = 0; i < text.Length; i++)
                    if (char.IsDigit(text[i]))
                    {
                        start = i;
                        break;
                    }
                if (start < 0)
                    return false;

                var end = start;
                while (end < text.Length && char.IsDigit(text[end]))
                    end++;
                if (!int.TryParse(text.Substring(start, end - start), out value))
                    return false;

                prefix = text.Substring(0, start);
                suffix = text.Substring(end);
                return true;
            }
        }
    }
}
