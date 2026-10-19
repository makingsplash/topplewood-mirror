using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Services.LogService;
using Gameplay.Core;
using Gameplay.PlayScreen;
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
    /// tessellation as the piece mesh (<see cref="ShapeVisualOutline"/>, TASK-074/075); everything on the target card
    /// goes through the card's pose (<see cref="TargetCardLocator"/>), read each frame, so the presenters' start order
    /// does not matter.
    ///
    /// Draw order (children of the canvas): <see cref="SpotlightDim"/> (dim with cut-outs: the piece and the card on a
    /// tap beat; the assembled figure and the card on "Совпало") → miss flash → ghost fill → contours → finger rig
    /// (<c>TourFinger</c> component) → ✓ (<c>TourCheck</c> component). The two components come from
    /// <see cref="TourAssets"/>; missing → code-built fallback with one warning.
    /// </summary>
    public sealed class TourView : ITourView, IDisposable
    {
        private readonly TourConfig _config;
        private readonly TargetCardLocator _card;
        private readonly PlayScreenInteraction _interaction;
        private Overlay _overlay;

        public TourView(TourConfig config, TargetCardLocator card, PlayScreenInteraction interaction)
        {
            _config = config;
            _card = card;
            _interaction = interaction;
        }

        private Overlay Get()
        {
            if (_overlay == null)
                _overlay = Overlay.Create(_config, _card, _interaction);
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
                _overlay.HideTap(true);
        }

        public Observable<Unit> PlayMatch(MatchCue cue)
        {
            return Observable.Create<Unit>(observer =>
            {
                var overlay = Get();
                var beat = overlay.PlayMatch(cue, () =>
                {
                    observer.OnNext(Unit.Default);
                    observer.OnCompleted();
                });
                return Disposable.Create(() =>
                {
                    beat?.Kill();
                    if (overlay != null)
                        overlay.HideMatch(true);
                });
            });
        }

        public void Clear()
        {
            if (_overlay == null)
                return;
            _overlay.HideTap(false);
            _overlay.HideMatch(false);
        }

        public void Dispose()
        {
            if (_overlay != null)
                UnityEngine.Object.Destroy(_overlay.gameObject); // SetLink kills the tweens with the objects
            _overlay = null;
        }

        /// <summary>MonoBehaviour driver on the overlay GameObject: owns the dim, contours, ghost, finger rig and ✓, their
        /// tweens, and the per-frame re-projection in <see cref="LateUpdate"/>.</summary>
        private sealed class Overlay : MonoBehaviour
        {
            private const int SortingOrder = 32100;
            private static readonly Vector2 ReferenceResolution = new(1080f, 1920f);
            // Re-send the dim's cut-outs only when a projected point moved more than this (cu): the dim re-triangulates
            // on SetCutouts, the projection runs every frame.
            private const float CutoutMoveEpsilon = 0.5f;

            private TourConfig _config;
            private TargetCardLocator _card;
            private PlayScreenInteraction _interaction;
            private IDisposable _hold;
            private Tween _holdCeiling;
            private RectTransform _canvasRect;
            private UnityEngine.Camera _camera; // fully-qualified: Gameplay.Camera namespace shadows the bare name

            private SpotlightDim _dim;
            private UiPolygonFill _flash;
            private UiPolygonFill _ghostFill;
            private TourFingerRig _rig;
            private RectTransform _rigRt;
            private RectTransform _check;
            private RectTransform _contourRoot;

            private Tracked _piece;
            private readonly List<Tracked> _slots = new();
            private readonly List<Tracked> _matchField = new();
            private readonly List<Tracked> _matchCard = new();
            private readonly List<Tracked> _ghostContours = new();
            private readonly List<FillSource> _ghostPolys = new();
            private readonly List<CutSource> _cuts = new();
            private readonly List<SpotlightCutout> _cutoutScratch = new();
            private readonly List<Vector2[]> _fillPoints = new();
            private readonly List<int[]> _fillTris = new();
            private FillSource _flashPoly;

            private bool _fingerShown;
            private float _ghostT;
            private float _halfGapWorld = float.PositiveInfinity;
            private Tween _delay;
            private Tween _dimTween;
            private Tween _flashTween;
            private Tween _missTween;
            private DG.Tweening.Sequence _match; // qualified: the Gameplay.Sequence namespace shadows the bare name

            public static Overlay Create(TourConfig config, TargetCardLocator card, PlayScreenInteraction interaction)
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
                overlay._interaction = interaction;
                overlay.Build(go, config, card);
                return overlay;
            }

            private void Build(GameObject root, TourConfig config, TargetCardLocator card)
            {
                _config = config;
                _card = card;
                _canvasRect = (RectTransform)root.transform;

                _dim = SpotlightDim.Create(_canvasRect, "Dim");
                _dim.Tone = config.DimTone;
                _dim.Alpha = 0f;

                _flash = NewFill("MissFlash");
                _ghostFill = NewFill("GhostFill");
                _contourRoot = NewNode("Contours", _canvasRect);
                _piece = NewContour("PieceContour", config.FieldOutlineColor, config.FieldOutlineWidth);

                var assets = TourAssets.Load();
                BuildRig(assets);
                BuildCheck(assets);
            }

            private void BuildRig(TourAssets assets)
            {
                var prefab = assets == null ? null : assets.Finger;
                if (prefab != null && Instantiate(prefab, _canvasRect, false).TryGetComponent<TourFingerRig>(out var rig))
                {
                    _rig = rig;
                }
                else
                {
                    if (assets != null)
                        Debug.unityLogger.Log(LogType.Warning,
                            $"[{LogCategory.Tutorial}] TourAssets has no TourFinger prefab with a TourFingerRig — code-built finger");
                    _rig = TourFingerRig.BuildFallback(_canvasRect, _config,
                        Resources.Load<Sprite>("Tutorial/tutorial_finger"), null);
                }
                _rigRt = (RectTransform)_rig.transform;
                _rigRt.anchorMin = _rigRt.anchorMax = new Vector2(0.5f, 0.5f); // anchoredPosition = canvas-local point
                _rig.Setup(_config);
                _rig.Contact += FlashContours;
                _rig.gameObject.SetActive(false);
            }

            private void BuildCheck(TourAssets assets)
            {
                var prefab = assets == null ? null : assets.Check;
                if (prefab != null)
                {
                    _check = (RectTransform)Instantiate(prefab, _canvasRect, false).transform;
                }
                else
                {
                    if (assets != null)
                        Debug.unityLogger.Log(LogType.Warning,
                            $"[{LogCategory.Tutorial}] TourAssets has no TourCheck prefab — code-built ✓ (white square)");
                    _check = NewNode("TourCheck(fallback)", _canvasRect);
                    _check.sizeDelta = Vector2.one * _config.CheckSize;
                    _check.gameObject.AddComponent<CanvasRenderer>();
                    _check.gameObject.AddComponent<Image>().raycastTarget = false;
                }
                _check.anchorMin = _check.anchorMax = new Vector2(0.5f, 0.5f);
                _check.pivot = new Vector2(0.5f, 0.5f);
                _check.gameObject.SetActive(false);
            }

            // ── Tap beat ─────────────────────────────────────────────────────────────────────────────────────────
            public void ShowTap(TapCue cue)
            {
                HideMatch(false);
                HideTap(false);
                gameObject.SetActive(true);

                var outline = Geometry.WorldVertices(ShapeVisualOutline.Build(cue.Shape), cue.Origin, cue.Rotation);
                _piece.Set(outline, PointSpace.Field);
                _halfGapWorld = HalfGap(outline, cue.Others);
                _flashPoly = FillSource.Build(outline);

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
                        var contour = Pooled(_slots, "SlotContour", _config.CardOutlineColor, _config.CardOutlineWidth);
                        contour.Set(Geometry.WorldVertices(local, slot.Position, slot.Rotation), PointSpace.Card);
                    }
                }

                // Cut-outs: the piece (pad/feather by the gap rule, resolved per frame in cu) and the card frame.
                _cuts.Clear();
                _cuts.Add(new CutSource(outline, PointSpace.Field, _config.CutoutPad, _config.CutoutFeather, true));
                AddCardCut();

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
                FadeDim(_config.TapDimAlpha, _config.DimInSeconds);
                _rig.Play(); // the approach (0.25 s) runs with the dim's fade-in: the finger "arrives" with the beat
            }

            private void SetTapVisible(bool visible)
            {
                _piece.Graphic.enabled = visible && _piece.Active;
                foreach (var s in _slots)
                    s.Graphic.enabled = visible && s.Active;
                _fingerShown = visible;
                _rig.gameObject.SetActive(visible);
            }

            public void PulseMiss()
            {
                if (!_piece.Active)
                    return;
                if (!_fingerShown)
                    RevealTap(); // a miss during the between-taps pause answers at once
                _rig.RestartFromPress();
                // "вырез вспыхивает": a white fill of the cut-out, fading out.
                _missTween?.Kill();
                _flash.color = new Color(1f, 1f, 1f, _config.MissFlashAlpha);
                _flash.enabled = true;
                _missTween = DOTween.ToAlpha(() => _flash.color, c => _flash.color = c, 0f, _config.MissFlashSeconds)
                    .SetEase(Ease.OutQuad).SetUpdate(true).SetLink(gameObject)
                    .OnComplete(() => _flash.enabled = false);
            }

            /// <param name="fade">The player tapped: finger and contours out at once (≤ 0.1 s), the dim over
            /// <see cref="TourConfig.DimOutSeconds"/> so the fall stays visible. False = everything now.</param>
            public void HideTap(bool fade)
            {
                _delay?.Kill();
                _delay = null;
                _missTween?.Kill();
                _flashTween?.Kill();
                SetFlashScale(1f); // a flash killed mid-way must not leave the contours swollen
                _rig.Stop();
                _rig.gameObject.SetActive(false);
                _fingerShown = false;
                _piece.Clear();
                foreach (var s in _slots)
                    s.Clear();
                _flash.enabled = false;
                _flash.ClearPolygons();
                _flashPoly = null;
                if (fade)
                {
                    FadeDim(0f, _config.DimOutSeconds, () => _cuts.Clear());
                }
                else
                {
                    CancelHold();
                    _dimTween?.Kill();
                    _dim.Alpha = 0f;
                    _cuts.Clear();
                    _dim.ClearCutouts();
                }
            }

            private void OnDestroy() => CancelHold();

            /// <summary>On each contact of the finger: the piece and slot contours flash together (spec 02 §3 — one
            /// synchronous flash links "tap here" with "it goes there").</summary>
            private void FlashContours()
            {
                _flashTween?.Kill();
                var half = _config.ContactFlashSeconds * 0.5f;
                _flashTween = DOVirtual.Float(1f, _config.ContactFlashWidthScale, half, SetFlashScale)
                    .SetLoops(2, LoopType.Yoyo).SetEase(Ease.OutQuad).SetUpdate(true).SetLink(gameObject);
            }

            private void SetFlashScale(float k)
            {
                _piece.Graphic.Width = _config.FieldOutlineWidth * k;
                foreach (var s in _slots)
                    s.Graphic.Width = _config.CardOutlineWidth * k;
            }

            // ── "Совпало" beat ───────────────────────────────────────────────────────────────────────────────────
            /// <summary>Scenario 01 §3 «Такт 3» on one unscaled timeline: dim (lighter), the same contours draw on the
            /// figure and on the card, a ghost of the target flies from the card onto the figure and lands, a shared
            /// flash, the ✓ on the card's corner, a hold. <paramref name="done"/> fires at <see cref="TourConfig.MatchSeconds"/>.</summary>
            public Tween PlayMatch(MatchCue cue, Action done)
            {
                HideTap(false);
                HideMatch(false);
                gameObject.SetActive(true);

                // The figure is drawn as its SLOTS — each piece's silhouette at its slot pose — not as
                // target.Outline: the manifest's union outline can be a coarse box (sailboat's is its bounding
                // rectangle, seen on the live frame), which would outline "a box" instead of "your figure". The target
                // is authored in field world coordinates (PoseMatchEvaluator compares slot and piece poses directly),
                // so on the field the slots ARE the assembled figure, and mapped by the card pose they are the picture.
                _cuts.Clear();
                var drawn = 0;
                if (cue.Target?.Slots != null && cue.ShapesByKind != null)
                    foreach (var slot in cue.Target.Slots)
                    {
                        if (!cue.ShapesByKind.TryGetValue(slot.Kind, out var shape) || shape == null)
                            continue;
                        var points = Geometry.WorldVertices(ShapeVisualOutline.Build(shape), slot.Position, slot.Rotation);
                        Pooled(_matchField, "MatchFieldContour", _config.FieldOutlineColor, _config.MatchFieldOutlineWidth)
                            .Set(points, PointSpace.Field);
                        Pooled(_matchCard, "MatchCardContour", _config.CardOutlineColor, _config.MatchCardOutlineWidth)
                            .Set(points, PointSpace.Card);
                        Pooled(_ghostContours, "GhostContour", _config.FieldOutlineColor, _config.GhostOutlineWidth)
                            .Set(points, PointSpace.Ghost);
                        var fill = FillSource.Build(points);
                        if (fill != null)
                            _ghostPolys.Add(fill);
                        _cuts.Add(new CutSource(points, PointSpace.Field, _config.CutoutPad, _config.CutoutFeather, false));
                        drawn++;
                    }
                AddCardCut();

                if (drawn == 0)
                    Debug.unityLogger.Log(LogType.Warning,
                        $"[{LogCategory.Tutorial}] Match beat: target has no drawable slots — only the dim and the hold play");

                var c = _config;
                SetMatchReveal(0f);
                SetGhost(0f, 0f);
                _check.gameObject.SetActive(false);

                _match = DOTween.Sequence()
                    .Insert(0f, DOVirtual.Float(0f, c.MatchDimAlpha, c.DimInSeconds, a => _dim.Alpha = a).SetEase(Ease.OutQuad))
                    .Insert(c.MatchContourStart, DOVirtual.Float(0f, 1f, c.MatchContourSeconds, SetMatchReveal).SetEase(Ease.OutQuad))
                    // Visual state lives in the tweens' setters, not in inserted callbacks: a Goto (scrubbing the
                    // timeline for a frame, docs/platform/unity-gotchas.md) lands every element where it belongs.
                    .Insert(c.GhostStart, DOVirtual.Float(0f, 1f, c.GhostEnd - c.GhostStart, t => SetGhost(t, c.GhostAlpha))
                        .SetEase(Ease.InOutSine))
                    // Shared flash: the landed ghost brightens and dissolves, the figure's contours swell with it.
                    .Insert(c.GhostEnd, DOVirtual.Float(0f, 1f, c.MatchFlashEnd - c.GhostEnd, MatchFlash).SetEase(Ease.Linear))
                    .Insert(c.CheckStart, DOVirtual.Float(0f, 1f, c.CheckEnd - c.CheckStart, CheckPop).SetEase(Ease.Linear))
                    .InsertCallback(c.MatchSeconds, () => done?.Invoke())
                    .SetUpdate(true)
                    .SetLink(gameObject);
                Reposition();
                return _match;
            }

            private void SetMatchReveal(float r)
            {
                foreach (var t in _matchField)
                    if (t.Active)
                        t.Graphic.Reveal = r;
                foreach (var t in _matchCard)
                    if (t.Active)
                        t.Graphic.Reveal = r;
            }

            private void SetGhost(float t, float alpha)
            {
                _ghostT = t;
                _ghostFill.color = new Color(1f, 1f, 1f, alpha);
                _ghostFill.enabled = alpha > 0.001f && _ghostPolys.Count > 0;
                foreach (var g in _ghostContours)
                    if (g.Active)
                    {
                        g.Graphic.enabled = _ghostFill.enabled;
                        g.SetAlpha(Mathf.Clamp01(alpha / Mathf.Max(_config.GhostAlpha, 0.001f)));
                    }
            }

            // f: 0 → 1 over the flash window. Ghost alpha rises to 0.85 at the middle, then to 0; field contours swell.
            private void MatchFlash(float f)
            {
                var bell = 1f - Mathf.Abs(f * 2f - 1f);
                var alpha = f < 0.5f ? Mathf.Lerp(_config.GhostAlpha, 0.85f, bell) : 0.85f * bell;
                SetGhost(1f, alpha);
                foreach (var t in _matchField)
                    if (t.Active)
                        t.Graphic.Width = _config.MatchFieldOutlineWidth * (1f + 0.6f * bell);
            }

            // f: 0 → 1 over CheckStart..CheckEnd: scale 0.2 → 1.1 (first 60 %) → 1.0.
            private void CheckPop(float f)
            {
                var s = f < 0.6f
                    ? Mathf.Lerp(0.2f, 1.1f, DOVirtual.EasedValue(0f, 1f, f / 0.6f, Ease.OutQuad))
                    : Mathf.Lerp(1.1f, 1f, (f - 0.6f) / 0.4f);
                _check.localScale = Vector3.one * s;
                if (!_check.gameObject.activeSelf)
                    _check.gameObject.SetActive(true);
            }

            /// <param name="fade">True when the beat ends into the win screen: the dim fades over
            /// <see cref="TourConfig.MatchDimOutSeconds"/> while the screen's own reveal plays — no un-dimmed frame.</param>
            public void HideMatch(bool fade)
            {
                _match?.Kill();
                _match = null;
                foreach (var t in _matchField)
                    t.Clear();
                foreach (var t in _matchCard)
                    t.Clear();
                foreach (var t in _ghostContours)
                    t.Clear();
                _ghostPolys.Clear();
                _ghostFill.enabled = false;
                _ghostFill.ClearPolygons();
                _check.gameObject.SetActive(false);
                if (fade)
                {
                    HoldDimUntilCovered();
                }
                else if (!_piece.Active)
                {
                    CancelHold();
                    _dimTween?.Kill();
                    _dim.Alpha = 0f;
                    _cuts.Clear();
                    _dim.ClearCutouts();
                }
            }

            /// <summary>The beat is over and the win screen is on its way (<c>Service.UI.Show</c> loads it). The dim stays
            /// until a screen covers the field (<see cref="PlayScreenInteraction.IsCoveredByScreen"/>), then fades while
            /// the screen's own reveal plays — so there is no un-dimmed frame between the beat and the screen even when
            /// the load takes a few frames. No screen within <see cref="TourConfig.MatchDimHoldMaxSeconds"/> (bringup
            /// shows a banner, not a UiService screen) → fade anyway.</summary>
            private void HoldDimUntilCovered()
            {
                CancelHold();
                void Release()
                {
                    CancelHold();
                    FadeDim(0f, _config.MatchDimOutSeconds, () => _cuts.Clear());
                }
                if (_interaction == null || _interaction.IsCoveredByScreen.Value)
                {
                    Release();
                    return;
                }
                _hold = _interaction.IsCoveredByScreen.Where(covered => covered).Take(1).Subscribe(_ => Release());
                _holdCeiling = DOVirtual.DelayedCall(_config.MatchDimHoldMaxSeconds, Release, ignoreTimeScale: true)
                    .SetLink(gameObject);
            }

            private void CancelHold()
            {
                _hold?.Dispose();
                _hold = null;
                _holdCeiling?.Kill();
                _holdCeiling = null;
            }

            private void FadeDim(float to, float seconds, Action onDone = null)
            {
                CancelHold();
                _dimTween?.Kill();
                _dimTween = DOVirtual.Float(_dim.Alpha, to, seconds, a => _dim.Alpha = a)
                    .SetEase(Ease.OutQuad).SetUpdate(true).SetLink(gameObject);
                if (onDone != null)
                    _dimTween.OnComplete(() => onDone());
            }

            private void AddCardCut()
            {
                var pose = _card.Current;
                if (!pose.Valid || pose.FrameWorldCorners == null || pose.FrameWorldCorners.Length < 3)
                    return;
                var corners = new Vec2[pose.FrameWorldCorners.Length];
                for (var i = 0; i < corners.Length; i++)
                    corners[i] = new Vec2(pose.FrameWorldCorners[i].x, pose.FrameWorldCorners[i].y);
                _cuts.Add(new CutSource(corners, PointSpace.Field, _config.CardCutoutPad, _config.CardCutoutFeather, false));
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
                foreach (var t in _ghostContours)
                    Project(t, pose);

                ProjectFills(pose);
                ProjectCuts(pose);

                if (_fingerShown && _piece.Active && _piece.Projected)
                    PlaceFinger();

                if (_check.gameObject.activeSelf && pose.Valid && pose.FrameWorldCorners is { Length: >= 3 }
                    && OverlayProjection.TryToCanvas(_camera, _canvasRect, pose.FrameWorldCorners[2], out var corner))
                    _check.anchoredPosition = PlaceCheck(corner);
            }

            /// <summary>✓ centre = the frame's top-right corner moved inward by <see cref="TourConfig.CheckInset"/> (spec
            /// 02 §3), then kept whole inside the safe area: the card hangs right under the safe top, so on sailboat the
            /// spec rule alone put the badge's top 8 px off-screen (frame K4, TASK-613 run 2).</summary>
            private Vector2 PlaceCheck(Vector2 frameTopRight)
            {
                var p = frameTopRight + new Vector2(-_config.CheckInset, -_config.CheckInset);
                var half = _config.CheckSize * 0.5f + _config.CheckEdgeMargin;
                var safe = SafeRect();
                p.x = Mathf.Min(p.x, safe.xMax - half);
                p.y = Mathf.Min(p.y, safe.yMax - half);
                return p;
            }

            private Vector3 ToWorld(Vec2 p, PointSpace space, TargetCardPose pose)
            {
                switch (space)
                {
                    case PointSpace.Card:
                        return pose.ToWorld(p);
                    case PointSpace.Ghost:
                        // Interpolate the MAP, not the vertices (plan §3.5): at t = 1 the ghost is the target in field
                        // coordinates, i.e. exactly the assembled figure.
                        var e = _ghostT;
                        var centre = new Vector3(pose.FigureCenter.X, pose.FigureCenter.Y, pose.WorldOrigin.z);
                        var origin = Vector3.Lerp(pose.WorldOrigin, centre, e);
                        var scale = Mathf.Lerp(pose.WorldScale, 1f, e);
                        return origin + new Vector3((p.X - pose.FigureCenter.X) * scale, (p.Y - pose.FigureCenter.Y) * scale, 0f);
                    default:
                        return new Vector3(p.X, p.Y, 0f);
                }
            }

            private void Project(Tracked t, TargetCardPose pose)
            {
                if (!t.Active)
                    return;
                if (t.Space != PointSpace.Field && !pose.Valid)
                {
                    t.Graphic.SetPoints(null); // no card on screen → nothing to outline there
                    t.Projected = false;
                    return;
                }
                for (var i = 0; i < t.Points.Length; i++)
                    if (!OverlayProjection.TryToCanvas(_camera, _canvasRect, ToWorld(t.Points[i], t.Space, pose), out t.Scratch[i]))
                        return; // behind the camera — keep last frame's contour rather than a broken one
                t.Graphic.SetPoints(t.Scratch);
                t.Projected = true;
            }

            private void ProjectFills(TargetCardPose pose)
            {
                if (_ghostFill.enabled && pose.Valid)
                {
                    _fillPoints.Clear();
                    _fillTris.Clear();
                    foreach (var f in _ghostPolys)
                        if (f.Project(this, PointSpace.Ghost, pose))
                        {
                            _fillPoints.Add(f.Scratch);
                            _fillTris.Add(f.Triangles);
                        }
                    _ghostFill.SetPolygons(_fillPoints, _fillTris);
                }
                if (_flash.enabled && _flashPoly != null && _flashPoly.Project(this, PointSpace.Field, pose))
                {
                    _fillPoints.Clear();
                    _fillTris.Clear();
                    _fillPoints.Add(_flashPoly.Scratch);
                    _fillTris.Add(_flashPoly.Triangles);
                    _flash.SetPolygons(_fillPoints, _fillTris);
                }
            }

            private void ProjectCuts(TargetCardPose pose)
            {
                if (_cuts.Count == 0)
                    return;
                var cuPerWorld = CanvasUnitsPerWorld();
                var changed = false;
                foreach (var cut in _cuts)
                {
                    for (var i = 0; i < cut.Points.Length; i++)
                    {
                        if (!OverlayProjection.TryToCanvas(_camera, _canvasRect, ToWorld(cut.Points[i], cut.Space, pose), out var p))
                            return;
                        if ((p - cut.Sent[i]).sqrMagnitude > CutoutMoveEpsilon * CutoutMoveEpsilon)
                            changed = true;
                        cut.Scratch[i] = p;
                    }
                    var (pad, feather) = cut.GapRule ? GapLimited(cut.Pad, cut.Feather, cuPerWorld) : (cut.Pad, cut.Feather);
                    if (!Mathf.Approximately(pad, cut.SentPad) || !Mathf.Approximately(feather, cut.SentFeather) || !cut.EverSent)
                        changed = true;
                    cut.SentPad = pad;
                    cut.SentFeather = feather;
                }
                if (!changed)
                    return;
                _cutoutScratch.Clear();
                foreach (var cut in _cuts)
                {
                    Array.Copy(cut.Scratch, cut.Sent, cut.Scratch.Length);
                    cut.EverSent = true;
                    _cutoutScratch.Add(new SpotlightCutout((Vector2[])cut.Sent.Clone(), cut.SentPad, cut.SentFeather));
                }
                _dim.SetCutouts(_cutoutScratch);
            }

            /// <summary>Spec 02 §3 gap rule: pad+feather ≤ half the gap to the nearest OTHER piece, else the cut-out
            /// shows that piece too and the pointing becomes ambiguous. Shrink the feather first, then the pad, to no
            /// less than <see cref="TourConfig.MinCutoutPad"/>.</summary>
            private (float pad, float feather) GapLimited(float pad, float feather, float cuPerWorld)
            {
                if (float.IsInfinity(_halfGapWorld) || cuPerWorld <= 0f)
                    return (pad, feather);
                var allowed = _halfGapWorld * cuPerWorld;
                if (pad + feather <= allowed)
                    return (pad, feather);
                if (pad <= allowed)
                    return (pad, allowed - pad);
                return (Mathf.Max(_config.MinCutoutPad, allowed), 0f);
            }

            private float CanvasUnitsPerWorld()
            {
                if (OverlayProjection.TryToCanvas(_camera, _canvasRect, Vector3.zero, out var a)
                    && OverlayProjection.TryToCanvas(_camera, _canvasRect, Vector3.right, out var b))
                    return (b - a).magnitude;
                return 0f;
            }

            /// <summary>Tip on the piece: its projected bbox centre moved <see cref="TourConfig.TouchBiasFraction"/> of
            /// the half-size AWAY from the hand (up-left; up-right when mirrored). The rig mirrors (scale.x = −1, the tip
            /// stays put — the pivot is on it) when the hand would cross the safe area's right edge (spec 02 §3).</summary>
            private void PlaceFinger()
            {
                var pts = _piece.Scratch;
                var min = pts[0];
                var max = pts[0];
                for (var i = 1; i < pts.Length; i++)
                {
                    min = Vector2.Min(min, pts[i]);
                    max = Vector2.Max(max, pts[i]);
                }
                var centre = (min + max) * 0.5f;
                var half = (max - min) * 0.5f;
                var bias = _config.TouchBiasFraction;
                var size = _config.FingerSize;

                var tip = centre + new Vector2(-bias * half.x, bias * half.y);
                var mirror = tip.x + _config.HandReachRight * size > SafeRect().xMax - _config.MirrorEdgeMargin;
                if (mirror)
                    tip = centre + new Vector2(bias * half.x, bias * half.y);

                _rigRt.anchoredPosition = tip;
                var k = _rigRt.rect.width > 0f ? size / _rigRt.rect.width : 1f; // FingerSize stays the one size lever
                _rigRt.localScale = new Vector3(mirror ? -k : k, k, 1f);
            }

            /// <summary><c>Screen.safeArea</c> in canvas-local units (the whole canvas if the conversion fails).</summary>
            private UnityEngine.Rect SafeRect()
            {
                var safe = Screen.safeArea;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, safe.min, null, out var min)
                    && RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, safe.max, null, out var max))
                    return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                return _canvasRect.rect;
            }

            // ── Building blocks ──────────────────────────────────────────────────────────────────────────────────
            /// <summary>Half the smallest distance between <paramref name="piece"/> and any of <paramref name="others"/>
            /// (vertex ↔ edge both ways — the polygons do not overlap at rest). +∞ when alone.</summary>
            private static float HalfGap(Vec2[] piece, IReadOnlyList<Vec2[]> others)
            {
                if (others == null || others.Count == 0)
                    return float.PositiveInfinity;
                var best = float.PositiveInfinity;
                foreach (var o in others)
                {
                    if (o == null || o.Length < 2)
                        continue;
                    best = Mathf.Min(best, VertexToEdges(piece, o));
                    best = Mathf.Min(best, VertexToEdges(o, piece));
                }
                return best * 0.5f;
            }

            private static float VertexToEdges(Vec2[] vertices, Vec2[] ring)
            {
                var best = float.PositiveInfinity;
                foreach (var v in vertices)
                    for (var i = 0; i < ring.Length; i++)
                    {
                        var a = ring[i];
                        var b = ring[(i + 1) % ring.Length];
                        var ab = b - a;
                        var len2 = Vec2.Dot(ab, ab);
                        var t = len2 > 1e-12f ? Mathf.Clamp01(Vec2.Dot(v - a, ab) / len2) : 0f;
                        var d = v - (a + ab * t);
                        best = Mathf.Min(best, Mathf.Sqrt(Vec2.Dot(d, d)));
                    }
                return best;
            }

            private Tracked Pooled(List<Tracked> pool, string name, Color color, float width)
            {
                foreach (var t in pool)
                    if (!t.Active)
                    {
                        t.Graphic.color = color;
                        t.Graphic.Width = width;
                        return t;
                    }
                var created = NewContour($"{name}{pool.Count}", color, width);
                pool.Add(created);
                return created;
            }

            private Tracked NewContour(string name, Color color, float width)
            {
                var rt = NewNode(name, _contourRoot);
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

            private UiPolygonFill NewFill(string name)
            {
                var rt = NewNode(name, _canvasRect);
                rt.gameObject.AddComponent<CanvasRenderer>();
                var fill = rt.gameObject.AddComponent<UiPolygonFill>();
                fill.raycastTarget = false;
                fill.enabled = false;
                return fill;
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

            /// <summary>A filled polygon triangulated ONCE in its source space; per frame only its vertices are
            /// projected (the camera is orthographic, so the projection is affine and the triangulation stays valid).</summary>
            private sealed class FillSource
            {
                public Vec2[] Verts;
                public int[] Triangles;
                public Vector2[] Scratch;

                public static FillSource Build(Vec2[] outline)
                {
                    if (!Geometry.TriangulateConcave(outline, out var verts, out var tris))
                        return null;
                    return new FillSource { Verts = verts, Triangles = tris, Scratch = new Vector2[verts.Length] };
                }

                public bool Project(Overlay o, PointSpace space, TargetCardPose pose)
                {
                    for (var i = 0; i < Verts.Length; i++)
                        if (!OverlayProjection.TryToCanvas(o._camera, o._canvasRect, o.ToWorld(Verts[i], space, pose), out Scratch[i]))
                            return false;
                    return true;
                }
            }
        }

        private enum PointSpace
        {
            /// <summary>Field world coordinates.</summary>
            Field,
            /// <summary>Target space drawn on the card (<see cref="TargetCardPose.ToWorld"/>).</summary>
            Card,
            /// <summary>Target space on the ghost's flight from the card to the field.</summary>
            Ghost,
        }

        /// <summary>One dim cut-out: source points, their per-frame projection, and what was last sent to the dim.</summary>
        private sealed class CutSource
        {
            public readonly Vec2[] Points;
            public readonly PointSpace Space;
            public readonly float Pad;
            public readonly float Feather;
            public readonly bool GapRule;
            public readonly Vector2[] Scratch;
            public readonly Vector2[] Sent;
            public float SentPad;
            public float SentFeather;
            public bool EverSent;

            public CutSource(Vec2[] points, PointSpace space, float pad, float feather, bool gapRule)
            {
                Points = points;
                Space = space;
                Pad = pad;
                Feather = feather;
                GapRule = gapRule;
                Scratch = new Vector2[points.Length];
                Sent = new Vector2[points.Length];
            }
        }

        /// <summary>One contour the overlay re-projects each frame, in the space its points are authored in.</summary>
        private sealed class Tracked
        {
            public readonly UiContour Graphic;
            public Vec2[] Points;
            public Vector2[] Scratch;
            public PointSpace Space;
            public bool Active;
            public bool Projected;

            public Tracked(UiContour graphic) => Graphic = graphic;

            public void Set(Vec2[] points, PointSpace space)
            {
                Points = points;
                Scratch = new Vector2[points.Length];
                Space = space;
                Active = true;
                Projected = false;
                Graphic.enabled = true;
                Graphic.Reveal = 1f;
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
                Projected = false;
                Points = null;
                Graphic.SetPoints(null);
                Graphic.enabled = false;
            }
        }
    }
}
