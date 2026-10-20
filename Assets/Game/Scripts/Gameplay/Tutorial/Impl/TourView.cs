using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Services.LogService;
using Gameplay.Core;
using Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Real <see cref="ITourView"/> (TASK-613; replaces the tour half of the former <c>TutorialView</c>). A code-built
    /// Screen Space Overlay at sorting order 32100 — above the hint highlight (32000), below the HUD (32764+), DEV 32767
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
    /// Draw order (children of the canvas): <see cref="SpotlightDim"/> (dim with cut-outs: the piece and the card) →
    /// miss flash → contours → finger rig (<c>TourFinger</c> component, from <see cref="TourAssets"/>; missing →
    /// code-built fallback with one warning). The finger rig carries its own nested canvas (overrideSorting,
    /// <see cref="TourConfig.FingerSortingOrder"/>) so it draws ABOVE the HUD buttons while the dim stays under them.
    /// TASK-635 HUD beat (<see cref="ShowHudTap"/>): the cut-out is a halo ellipse around a HUD element's screen quad,
    /// re-read every frame from the target in screen space (no camera), and the finger taps its centre, turned 180°
    /// near the bottom edge. The TASK-613 "Совпало" beat (ghost, ✓, match dim) is gone since TASK-634:
    /// the win is drawn by the common win beat «В рамку» (<c>WinBeatView</c>) on every level.
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

        public void ShowHudTap(HudTapCue cue)
        {
            Debug.unityLogger.Log(LogType.Log,
                $"[{LogCategory.Tutorial}] ShowHudTap target={cue.Target?.Id ?? "<null>"}");
            if (cue.Target == null)
                return;
            Get().ShowHudTap(cue);
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

        public void Clear()
        {
            if (_overlay == null)
                return;
            _overlay.HideTap(false);
        }

        public void Dispose()
        {
            if (_overlay != null)
                UnityEngine.Object.Destroy(_overlay.gameObject); // SetLink kills the tweens with the objects
            _overlay = null;
        }

        /// <summary>MonoBehaviour driver on the overlay GameObject: owns the dim, contours and finger rig, their tweens,
        /// and the per-frame re-projection in <see cref="LateUpdate"/>.</summary>
        private sealed class Overlay : MonoBehaviour
        {
            private const int SortingOrder = 32100;
            private static readonly Vector2 ReferenceResolution = new(1080f, 1920f);
            // Re-send the dim's cut-outs only when a projected point moved more than this (cu): the dim re-triangulates
            // on SetCutouts, the projection runs every frame.
            private const float CutoutMoveEpsilon = 0.5f;

            private TourConfig _config;
            private TargetCardLocator _card;
            private RectTransform _canvasRect;
            private UnityEngine.Camera _camera; // fully-qualified: Gameplay.Camera namespace shadows the bare name

            private SpotlightDim _dim;
            private UiPolygonFill _flash;
            private TourFingerRig _rig;
            private RectTransform _rigRt;
            private RectTransform _contourRoot;

            private Tracked _piece;
            private readonly List<Tracked> _slots = new();
            private readonly List<CutSource> _cuts = new();
            private readonly List<SpotlightCutout> _cutoutScratch = new();
            private readonly List<Vector2[]> _fillPoints = new();
            private readonly List<int[]> _fillTris = new();
            private FillSource _flashPoly;

            // HUD beat (TASK-635): the target's screen quad → a canvas-local halo ellipse, rebuilt every frame (the
            // element can move: the unlock pop scales it, the safe area can change).
            private IHudTourTarget _hud;
            private readonly Vector2[] _hudScreen = new Vector2[4];
            private Vector2[] _hudRing;
            private Vector2[] _hudSent;
            private int[] _hudTris;
            private bool _hudEverSent;

            private bool _fingerShown;
            private Vec2? _aim; // TASK-645: the cue's visible-part aim (a cover overlaps the piece), else null
            private float _halfGapWorld = float.PositiveInfinity;
            private Tween _delay;
            private Tween _dimTween;
            private Tween _flashTween;
            private Tween _missTween;

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

                _dim = SpotlightDim.Create(_canvasRect, "Dim");
                _dim.Tone = config.DimTone;
                _dim.Alpha = 0f;

                _flash = NewFill("MissFlash");
                _contourRoot = NewNode("Contours", _canvasRect);
                _piece = NewContour("PieceContour", config.FieldOutlineColor, config.FieldOutlineWidth);

                BuildRig(TourAssets.Load());
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
                // TASK-635: the finger on its own sorting layer ABOVE the HUD buttons (which draw over this overlay), so
                // a HUD beat's fingertip is not hidden under the button it taps. No GraphicRaycaster: it never eats input.
                var fingerCanvas = _rig.gameObject.GetComponent<Canvas>();
                if (fingerCanvas == null)
                    fingerCanvas = _rig.gameObject.AddComponent<Canvas>();
                fingerCanvas.overrideSorting = true;
                fingerCanvas.sortingOrder = _config.FingerSortingOrder;
                _rig.Setup(_config);
                _rig.Contact += FlashContours;
                _rig.gameObject.SetActive(false);
            }

            // ── Tap beat ─────────────────────────────────────────────────────────────────────────────────────────
            public void ShowTap(TapCue cue)
            {
                HideTap(false);
                gameObject.SetActive(true);

                var outline = Geometry.WorldVertices(ShapeVisualOutline.Build(cue.Shape), cue.Origin, cue.Rotation);
                _piece.Set(outline, PointSpace.Field);
                _aim = cue.Aim;
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

            // ── HUD beat (TASK-635) ──────────────────────────────────────────────────────────────────────────────
            /// <summary>Dim with a halo cut-out around the HUD target (screen space, no camera involved) and the finger's
            /// tap cycle on its centre, at once. No contours: the element itself shows through the hole — it draws above
            /// this overlay, the native "raise" of the highlight pattern.</summary>
            public void ShowHudTap(HudTapCue cue)
            {
                HideTap(false);
                gameObject.SetActive(true);

                var n = Mathf.Max(8, _config.HudCutoutSegments);
                _hud = cue.Target;
                _hudRing = new Vector2[n];
                _hudSent = new Vector2[n];
                _hudEverSent = false;
                _hudTris = new int[(n - 2) * 3];
                for (var i = 0; i < n - 2; i++)
                {
                    _hudTris[i * 3] = 0;
                    _hudTris[i * 3 + 1] = i + 1;
                    _hudTris[i * 3 + 2] = i + 2;
                }

                RevealTap();
                Reposition();
            }

            /// <summary>Re-project the HUD beat: the target's screen quad → canvas-local → the circumscribed ellipse
            /// (half-axes = half-size × √2, so the quad's corners sit on it), sent to the dim when it moved; the miss
            /// flash fills the same ellipse; the finger's tip goes on its centre.</summary>
            private void RepositionHud()
            {
                if (!_hud.TryGetScreenCorners(_hudScreen))
                    return;
                var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                foreach (var s in _hudScreen)
                {
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, s, null, out var p))
                        return;
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
                var centre = (min + max) * 0.5f;
                var axes = (max - min) * (0.5f * Mathf.Sqrt(2f));
                var changed = !_hudEverSent;
                for (var i = 0; i < _hudRing.Length; i++)
                {
                    var a = 2f * Mathf.PI * i / _hudRing.Length;
                    var p = centre + new Vector2(Mathf.Cos(a) * axes.x, Mathf.Sin(a) * axes.y);
                    if ((p - _hudSent[i]).sqrMagnitude > CutoutMoveEpsilon * CutoutMoveEpsilon)
                        changed = true;
                    _hudRing[i] = p;
                }

                if (changed)
                {
                    Array.Copy(_hudRing, _hudSent, _hudRing.Length);
                    _hudEverSent = true;
                    _cutoutScratch.Clear();
                    _cutoutScratch.Add(new SpotlightCutout((Vector2[])_hudSent.Clone(),
                        _config.HudCutoutPad, _config.HudCutoutFeather));
                    _dim.SetCutouts(_cutoutScratch);
                }

                if (_flash.enabled)
                {
                    _fillPoints.Clear();
                    _fillTris.Clear();
                    _fillPoints.Add(_hudRing);
                    _fillTris.Add(_hudTris);
                    _flash.SetPolygons(_fillPoints, _fillTris);
                }

                if (_fingerShown)
                    PlaceFingerAt(centre, Vector2.zero, flipY: true);
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
                if (!_piece.Active && _hud == null) // no beat shown (piece or HUD) → nothing to answer with
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
                _hud = null;
                _hudRing = null;
                _hudSent = null;
                _hudTris = null;
                if (fade)
                {
                    FadeDim(0f, _config.DimOutSeconds, () => _cuts.Clear());
                }
                else
                {
                    _dimTween?.Kill();
                    _dim.Alpha = 0f;
                    _cuts.Clear();
                    _dim.ClearCutouts();
                }
            }

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

            private void FadeDim(float to, float seconds, Action onDone = null)
            {
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
                if (_hud != null)
                {
                    RepositionHud(); // screen space: no camera needed
                    return;
                }
                if (_camera == null)
                    _camera = UnityEngine.Camera.main; // same seam CameraFramingPresenter uses; re-fetch until one exists
                if (_camera == null)
                    return;

                var pose = _card.Current;
                Project(_piece, pose);
                foreach (var s in _slots)
                    Project(s, pose);

                ProjectFills(pose);
                ProjectCuts(pose);

                if (_fingerShown && _piece.Active && _piece.Projected)
                    PlaceFinger();
            }

            private Vector3 ToWorld(Vec2 p, PointSpace space, TargetCardPose pose) =>
                space == PointSpace.Card ? pose.ToWorld(p) : new Vector3(p.X, p.Y, 0f);

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
            /// stays put — the pivot is on it) when the hand would cross the safe area's right edge (spec 02 §3).
            /// TASK-645: a piece partly under another carries an aim — the tip goes exactly there (no bias): the biased
            /// bbox point could sit on the cover, where the gate refuses the tap the finger shows.</summary>
            private void PlaceFinger()
            {
                if (_aim.HasValue)
                {
                    var a = _aim.Value;
                    if (OverlayProjection.TryToCanvas(_camera, _canvasRect, new Vector3(a.X, a.Y, 0f), out var tip))
                    {
                        PlaceFingerAt(tip, Vector2.zero, flipY: false);
                        return;
                    }
                }

                var pts = _piece.Scratch;
                var min = pts[0];
                var max = pts[0];
                for (var i = 1; i < pts.Length; i++)
                {
                    min = Vector2.Min(min, pts[i]);
                    max = Vector2.Max(max, pts[i]);
                }
                PlaceFingerAt((min + max) * 0.5f, (max - min) * 0.5f, flipY: false);
            }

            /// <param name="half">Half-size of the target's bbox; the tip is biased by <see cref="TourConfig.TouchBiasFraction"/>
            /// of it (zero = the tip on the centre, the HUD beat).</param>
            /// <param name="flipY">TASK-635 (HUD beat only — the field tours keep their look): when the hand would cross the
            /// safe area's BOTTOM edge, flip it up too (scale.y = −k). With the X mirror that is a 180° turn: the hand up-left,
            /// the finger pointing down-right — the Hint button sits at the bottom-right, so there it always turns.</param>
            private void PlaceFingerAt(Vector2 centre, Vector2 half, bool flipY)
            {
                var bias = _config.TouchBiasFraction;
                var size = _config.FingerSize;
                var safe = SafeRect();

                var tip = centre + new Vector2(-bias * half.x, bias * half.y);
                var mirror = tip.x + _config.HandReachRight * size > safe.xMax - _config.MirrorEdgeMargin;
                if (mirror)
                    tip = centre + new Vector2(bias * half.x, bias * half.y);
                var flip = flipY && tip.y - _config.HandReachDown * size < safe.yMin + _config.MirrorEdgeMargin;

                _rigRt.anchoredPosition = tip;
                var k = _rigRt.rect.width > 0f ? size / _rigRt.rect.width : 1f; // FingerSize stays the one size lever
                _rigRt.localScale = new Vector3(mirror ? -k : k, flip ? -k : k, 1f);
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
