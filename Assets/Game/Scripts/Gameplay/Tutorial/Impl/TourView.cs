using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Services.LogService;
using Gameplay.Core;
using Gameplay.View;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Real <see cref="ITourView"/> (TASK-613; replaces the tour half of the former <c>TutorialView</c>). A code-built
    /// Screen Space Overlay at sorting order 32100 — above the hint highlight (32000), below the HUD (32765+), DEV 32767
    /// untouched — that never eats input (no GraphicRaycaster, raycastTarget off), so the tap still reaches gameplay.
    /// The canvas scales like the HUD (ScaleWithScreenSize, reference 1080×1920, match-width — plan §3.9), so the
    /// sizes in <see cref="TourConfig"/> are a fraction of the screen WIDTH on every phone; every point is projected
    /// through <see cref="OverlayProjection"/>, which stays exact at any scale factor.
    ///
    /// Everything it draws is re-projected EVERY frame (owner requirement 2026-08-04: the camera is framed to the level
    /// on LevelLoaded, so a once-only projection can miss the first frame). Contours on the field use the SAME
    /// tessellation as the piece mesh (<see cref="ShapeVisualOutline"/>, TASK-074/075); contours on the target card go
    /// through the card's pose (<see cref="TargetCardLocator"/>), read each frame, so the presenters' start order does
    /// not matter.
    ///
    /// What this pass draws (TASK-613 run 1 of 2, plan §9): the piece contour, the card slot contour(s) of its kind,
    /// and a finger that TAPS — approach, press (scale about the tip), contact hold, lift, pause — on the existing
    /// <c>Resources/Tutorial/tutorial_finger</c> sprite; for "Совпало", the same slot contours on the assembled figure
    /// and on the card, pulsing together, for <see cref="TourConfig.MatchSeconds"/>. The dim with cut-outs
    /// (<c>SpotlightDim</c>), the ripple/✓ components and the ghost flight are run 2.
    /// </summary>
    public sealed class TourView : ITourView, IDisposable
    {
        private readonly TourConfig _config;
        private readonly TargetCardLocator _card;
        private Overlay _overlay;

        public TourView(TourConfig config, TargetCardLocator card)
        {
            _config = config;
            _card = card;
        }

        private Overlay Get()
        {
            if (_overlay == null)
                _overlay = Overlay.Create(_config, _card);
            return _overlay;
        }

        public void ShowTap(TapCue cue)
        {
            Debug.unityLogger.Log(LogType.Log,
                $"[{LogCategory.Tutorial}] ShowTap origin=({cue.Origin.X:0.##},{cue.Origin.Y:0.##}) kind={cue.Kind} " +
                $"delay={cue.Delay:0.##} card={_card.Current.Valid} camMain={(UnityEngine.Camera.main != null)}");
            Get().ShowTap(cue);
        }

        public void PulseMiss()
        {
            if (_overlay != null)
                _overlay.PulseMiss();
        }

        public void HideTap()
        {
            if (_overlay != null)
                _overlay.HideTap();
        }

        public Observable<Unit> PlayMatch(MatchCue cue)
        {
            return Observable.Create<Unit>(observer =>
            {
                var overlay = Get();
                overlay.ShowMatch(cue);
                var timer = DOVirtual.DelayedCall(_config.MatchSeconds, () =>
                {
                    observer.OnNext(Unit.Default);
                    observer.OnCompleted();
                }, ignoreTimeScale: true).SetLink(overlay.gameObject);
                return Disposable.Create(() =>
                {
                    timer.Kill();
                    if (overlay != null)
                        overlay.HideMatch();
                });
            });
        }

        public void Clear()
        {
            if (_overlay == null)
                return;
            _overlay.HideTap();
            _overlay.HideMatch();
        }

        public void Dispose()
        {
            if (_overlay != null)
                UnityEngine.Object.Destroy(_overlay.gameObject); // SetLink kills the tweens with the objects
            _overlay = null;
        }

        /// <summary>MonoBehaviour driver on the overlay GameObject: owns the contours, the finger and its tweens, and
        /// the per-frame re-projection in <see cref="LateUpdate"/>.</summary>
        private sealed class Overlay : MonoBehaviour
        {
            private const int SortingOrder = 32100;
            private static readonly Vector2 ReferenceResolution = new(1080f, 1920f);

            // The finger sprite's TIP is off-centre: (−0.266, +0.390) of the sprite size from its centre in UI space
            // (measured 2026-08-04; art 04 kept the tip at the same fraction in the redrawn finger). The PIVOT sits on
            // the tip (TASK-382), so the press scale and a mirror flip both happen about the tip — it never leaves the
            // piece.
            private static readonly Vector2 FingerTipPivot = new(0.5f - 0.266f, 0.5f + 0.390f);

            private TourConfig _config;
            private TargetCardLocator _card;
            private RectTransform _canvasRect;
            private UnityEngine.Camera _camera; // fully-qualified: Gameplay.Camera namespace shadows the bare name

            private Tracked _piece;
            private readonly List<Tracked> _slots = new();
            private readonly List<Tracked> _matchField = new();
            private readonly List<Tracked> _matchCard = new();

            private RectTransform _fingerPin;   // zero-size node placed on the tip target each frame
            private Image _finger;              // pivot on the tip; the tap cycle tweens its offset and scale
            private Vector3 _fingerWorld;
            private bool _fingerShown;
            private DG.Tweening.Sequence _cycle; // qualified: the Gameplay.Sequence namespace shadows the bare name
            private Tween _delay;
            private Tween _matchPulse;

            public static Overlay Create(TourConfig config, TargetCardLocator card)
            {
                var go = new GameObject("TourOverlay");
                DontDestroyOnLoad(go);

                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;

                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = ReferenceResolution;
                scaler.matchWidthOrHeight = 0f; // match-width: the design-space of the UI compiler
                // Deliberately NO GraphicRaycaster: the overlay must never intercept the gameplay tap.

                var overlay = go.AddComponent<Overlay>();
                overlay.Build(go, config, card);
                return overlay;
            }

            private void Build(GameObject root, TourConfig config, TargetCardLocator card)
            {
                _config = config;
                _card = card;
                _canvasRect = (RectTransform)root.transform;

                _piece = NewContour("PieceContour", config.FieldOutlineColor, config.FieldOutlineWidth);

                _fingerPin = NewNode("FingerPin", _canvasRect);
                var fingerRt = NewNode("Finger", _fingerPin);
                fingerRt.sizeDelta = Vector2.one * config.FingerSize;
                fingerRt.pivot = FingerTipPivot;
                _finger = fingerRt.gameObject.AddComponent<Image>();
                _finger.sprite = FingerSpriteCache.Get();
                _finger.raycastTarget = false; // never intercept the gameplay tap
                _fingerPin.gameObject.SetActive(false);
            }

            // ── Tap beat ─────────────────────────────────────────────────────────────────────────────────────────
            public void ShowTap(TapCue cue)
            {
                HideTap();
                gameObject.SetActive(true);

                _fingerWorld = new Vector3(cue.Origin.X, cue.Origin.Y, 0f);
                _piece.Set(Geometry.WorldVertices(ShapeVisualOutline.Build(cue.Shape), cue.Origin, cue.Rotation), false);

                // Every card slot of the piece's kind (several slots of one kind: the piece lands in one of them, so
                // pointing at a single one would lie — plan §1). No slot of that kind → no slot contour, the rest stays.
                var target = _card.Current.Valid ? _card.Target : null;
                if (target?.Slots != null && cue.Shape != null)
                {
                    var local = ShapeVisualOutline.Build(cue.Shape);
                    foreach (var slot in target.Slots)
                    {
                        if (slot.Kind != cue.Kind)
                            continue;
                        var contour = SlotContour(_slots.FindIndex(s => !s.Active));
                        contour.Set(Geometry.WorldVertices(local, slot.Position, slot.Rotation), true);
                    }
                }

                if (cue.Delay > 0f)
                {
                    SetTapVisible(false);
                    _delay = DOVirtual.DelayedCall(cue.Delay, RevealTap, ignoreTimeScale: true).SetLink(gameObject);
                }
                else
                {
                    RevealTap();
                }
                Reposition();
            }

            private void RevealTap()
            {
                _delay?.Kill();
                _delay = null;
                SetTapVisible(true);
                StartCycle();
            }

            private void SetTapVisible(bool visible)
            {
                _piece.Graphic.enabled = visible && _piece.Active;
                foreach (var s in _slots)
                    s.Graphic.enabled = visible && s.Active;
                _fingerShown = visible;
                _fingerPin.gameObject.SetActive(visible);
            }

            private void StartCycle()
            {
                _cycle?.Kill();
                var rt = _finger.rectTransform;
                var offset = Vector2.one * (_config.ApproachOffsetFraction * _config.FingerSize);
                rt.anchoredPosition = offset;
                rt.localScale = Vector3.one;
                _cycle = DOTween.Sequence()
                    .AppendCallback(() =>
                    {
                        rt.anchoredPosition = offset;
                        rt.localScale = Vector3.one;
                    })
                    .Append(rt.DOAnchorPos(Vector2.zero, _config.ApproachSeconds).SetEase(Ease.OutQuad))
                    .Append(rt.DOScale(_config.PressScale, _config.PressSeconds).SetEase(Ease.InQuad))
                    .AppendInterval(_config.ContactSeconds)
                    .Append(rt.DOScale(1f, _config.LiftSeconds).SetEase(Ease.OutQuad))
                    .Join(rt.DOAnchorPos(offset, _config.LiftSeconds).SetEase(Ease.OutQuad))
                    .AppendInterval(_config.PauseSeconds)
                    .SetLoops(-1)
                    .SetUpdate(true)
                    .SetLink(rt.gameObject);
            }

            public void PulseMiss()
            {
                if (!_piece.Active)
                    return;
                if (!_fingerShown)
                    RevealTap(); // a miss during the between-taps pause answers at once
                // An out-of-turn cycle starting at the press: the finger is already on the piece at that time.
                _cycle?.Goto(_config.ApproachSeconds, true);
            }

            public void HideTap()
            {
                _delay?.Kill();
                _delay = null;
                _cycle?.Kill();
                _cycle = null;
                _piece.Clear();
                foreach (var s in _slots)
                    s.Clear();
                _fingerShown = false;
                if (_fingerPin != null)
                    _fingerPin.gameObject.SetActive(false);
            }

            // ── "Совпало" beat ───────────────────────────────────────────────────────────────────────────────────
            public void ShowMatch(MatchCue cue)
            {
                HideTap();
                HideMatch();
                gameObject.SetActive(true);
                // The figure is drawn as its SLOTS — each piece's silhouette at its slot pose — not as
                // target.Outline: the manifest's union outline can be a coarse box (sailboat's is its bounding
                // rectangle, seen on the live frame), which would outline "a box" instead of "your figure". The target
                // is authored in field world coordinates (PoseMatchEvaluator compares slot and piece poses directly),
                // so on the field the slots ARE the assembled figure, and mapped by the card pose they are the picture.
                // Same contours, two places: "this is the same thing".
                var drawn = 0;
                if (cue.Target?.Slots != null && cue.ShapesByKind != null)
                    foreach (var slot in cue.Target.Slots)
                    {
                        if (!cue.ShapesByKind.TryGetValue(slot.Kind, out var shape) || shape == null)
                            continue;
                        var points = Geometry.WorldVertices(ShapeVisualOutline.Build(shape), slot.Position, slot.Rotation);
                        MatchContour(_matchField, drawn, false).Set(points, false);
                        MatchContour(_matchCard, drawn, true).Set(points, true);
                        drawn++;
                    }
                if (drawn == 0)
                {
                    Debug.unityLogger.Log(LogType.Warning,
                        $"[{LogCategory.Tutorial}] Match beat: target has no drawable slots — nothing to draw, the beat only holds");
                    return;
                }
                SetMatchAlpha(1f);
                _matchPulse = DOTween.To(() => _matchAlpha, SetMatchAlpha, 0.25f, _config.MatchSeconds / 8f)
                    .SetLoops(4, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true).SetLink(gameObject);
                Reposition();
            }

            private float _matchAlpha = 1f;

            private void SetMatchAlpha(float a)
            {
                _matchAlpha = a;
                foreach (var t in _matchField)
                    if (t.Active)
                        t.SetAlpha(a);
                foreach (var t in _matchCard)
                    if (t.Active)
                        t.SetAlpha(a);
            }

            public void HideMatch()
            {
                _matchPulse?.Kill();
                _matchPulse = null;
                foreach (var t in _matchField)
                    t.Clear();
                foreach (var t in _matchCard)
                    t.Clear();
            }

            private Tracked MatchContour(List<Tracked> pool, int index, bool onCard)
            {
                while (pool.Count <= index)
                    pool.Add(onCard
                        ? NewContour($"MatchCardContour{pool.Count}", _config.CardOutlineColor, _config.CardOutlineWidth)
                        : NewContour($"MatchFieldContour{pool.Count}", _config.FieldOutlineColor, _config.FieldOutlineWidth));
                return pool[index];
            }

            // ── Projection ───────────────────────────────────────────────────────────────────────────────────────
            private void LateUpdate() => Reposition();

            private void Reposition()
            {
                if (_camera == null)
                    _camera = UnityEngine.Camera.main; // same seam CameraFramingPresenter uses; re-fetch until one exists
                if (_camera == null)
                    return;

                var pose = _card.Current;
                Project(_piece, pose);
                foreach (var s in _slots)
                    Project(s, pose);
                foreach (var t in _matchField)
                    Project(t, pose);
                foreach (var t in _matchCard)
                    Project(t, pose);

                if (_fingerShown && OverlayProjection.TryToCanvas(_camera, _canvasRect, _fingerWorld, out var tip))
                    _fingerPin.anchoredPosition = tip;
            }

            private void Project(Tracked t, TargetCardPose pose)
            {
                if (!t.Active)
                    return;
                if (t.OnCard && !pose.Valid)
                {
                    t.Graphic.SetPoints(null); // no card on screen → nothing to outline there
                    return;
                }
                for (var i = 0; i < t.Points.Length; i++)
                {
                    var p = t.Points[i];
                    var world = t.OnCard ? pose.ToWorld(p) : new Vector3(p.X, p.Y, 0f);
                    if (!OverlayProjection.TryToCanvas(_camera, _canvasRect, world, out var local))
                        return; // behind the camera — keep last frame's contour rather than a broken one
                    t.Scratch[i] = local;
                }
                t.Graphic.SetPoints(t.Scratch);
            }

            // ── Building blocks ──────────────────────────────────────────────────────────────────────────────────
            private Tracked SlotContour(int freeIndex)
            {
                if (freeIndex >= 0)
                    return _slots[freeIndex];
                var contour = NewContour($"SlotContour{_slots.Count}", _config.CardOutlineColor, _config.CardOutlineWidth);
                _slots.Add(contour);
                return contour;
            }

            private Tracked NewContour(string name, Color color, float width)
            {
                var rt = NewNode(name, _canvasRect);
                // CanvasRenderer explicitly FIRST too (TASK-309), so correctness never rides solely on RequireComponent.
                rt.gameObject.AddComponent<CanvasRenderer>();
                var contour = rt.gameObject.AddComponent<UiContour>();
                contour.color = color;
                contour.Width = width;
                contour.raycastTarget = false;
                var tracked = new Tracked(contour);
                tracked.Clear();
                return tracked;
            }

            // Centre-anchored, zero-size node at the canvas origin: children and mesh points are in canvas-local units,
            // exactly what OverlayProjection returns. (A default 100×100 rect shifted corner-anchored children by
            // half its size — the TASK-078 / TASK-380 trap.)
            private static RectTransform NewNode(string name, Transform parent)
            {
                var go = new GameObject(name, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = Vector2.zero;
                rt.anchoredPosition = Vector2.zero;
                return rt;
            }
        }

        /// <summary>One contour the overlay re-projects each frame: its points in field world coordinates, or in target
        /// space when it is drawn on the card (mapped by the card pose).</summary>
        private sealed class Tracked
        {
            public readonly UiContour Graphic;
            public Vec2[] Points;
            public Vector2[] Scratch;
            public bool OnCard;
            public bool Active;

            public Tracked(UiContour graphic) => Graphic = graphic;

            public void Set(Vec2[] points, bool onCard)
            {
                Points = points;
                Scratch = new Vector2[points.Length];
                OnCard = onCard;
                Active = true;
                Graphic.enabled = true;
                SetAlpha(1f);
            }

            public void SetAlpha(float a)
            {
                var c = Graphic.color;
                c.a = a;
                Graphic.color = c;
            }

            public void Clear()
            {
                Active = false;
                Points = null;
                Graphic.SetPoints(null);
                Graphic.enabled = false;
            }
        }

        /// <summary>Lazily-loaded, cached finger sprite (<c>Resources/Tutorial/tutorial_finger</c>, TASK-083). Falls
        /// back to a placeholder white square — logged ONCE — if the asset is missing, so the tour keeps working (a
        /// silently-dropped highlight is exactly the failure class this fallback guards against).</summary>
        private static class FingerSpriteCache
        {
            private static Sprite _sprite;
            private static bool _loaded;

            public static Sprite Get()
            {
                if (_loaded)
                    return _sprite;
                _loaded = true;

                _sprite = Resources.Load<Sprite>("Tutorial/tutorial_finger");
                if (_sprite == null)
                {
                    Debug.unityLogger.Log(LogType.Warning,
                        $"[{LogCategory.Tutorial}] finger sprite 'Tutorial/tutorial_finger' not found — using placeholder square");
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var pixels = new Color32[16];
                    for (var i = 0; i < pixels.Length; i++)
                        pixels[i] = new Color32(255, 255, 255, 255);
                    tex.SetPixels32(pixels);
                    tex.Apply();
                    _sprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                }
                return _sprite;
            }
        }
    }
}
