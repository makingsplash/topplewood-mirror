using System;
using System.Collections.Generic;
using DG.Tweening;
using Gameplay.Core;
using Gameplay.PlayScreen;
using R3;
using Ui.Components.Reveal;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.View
{
    /// <summary>
    /// The win beat «В рамку» (TASK-634, plan 02 §2.3): on every win the assembled figure — exactly the pieces the judge
    /// paired with slots (<see cref="GameModel.WinFigure"/>) — bounces in place (a uniform grow → shrink, TASK-639), flies
    /// up and lands on the target card,
    /// which answers with a punch, a flash of its window and sparks off its frame. Then the resolution flow shows the win
    /// screen.
    ///
    /// The LIVE <see cref="PieceView"/>s fly, not a snapshot: after Won nobody drives them (the mechanic's Tick leaves on
    /// the phase, Sync is never called again) and the bodies live apart in the physics scene, so moving the views touches
    /// neither physics nor poseHash. Frame 0 is therefore identical to the frame before Won, and there is no swap at the
    /// landing either: the flight interpolates the MAP "field → card" (the TASK-613 ghost trick, reversed), so in the
    /// landing frame every piece sits on exactly the numbers the card lays its slot out with
    /// (<c>root + (slot − FigureCenter)·scale</c>, <see cref="TargetPreviewView"/>), just in front of it; the pieces then stay
    /// on the card as its picture until the next level load destroys the card.
    ///
    /// Hierarchy while the beat runs: <c>WinBeatFlight</c> (the map: FigureCenter → card origin, scale 1 → card scale) →
    /// <c>BouncePivot</c> (fixed, at <see cref="WinBeatConfig.PivotHeightFraction"/> of the figure's bbox — centre by
    /// default: uniform bounce scale) → the piece views (rest pose → slot pose, angle
    /// → the symmetric-nearest slot angle). The flash, the sparks and the tap catcher live on a code-built Screen Space
    /// Overlay (sorting 32200: above the tour 32100, below the HUD 32765+). ALL visual state is a function of the beat time
    /// in ONE setter (<see cref="Beat.Apply"/>), so <c>DOTween.Goto("WinBeat", t)</c> poses any frame for acceptance
    /// (<c>docs/platform/testing.md</c>). A tap fast-forwards to the landing and plays the rest in
    /// <see cref="WinBeatConfig.SkipTailSeconds"/>.
    ///
    /// Known deviation from design 01 §5.11: the pieces fly UNDER the HUD (a Screen Space Overlay always draws over the
    /// world; lifting a world mesh above it would mean moving the figure into UI).
    /// </summary>
    public sealed class WinBeatView : IWinBeat, IDisposable
    {
        /// <summary>DOTween id of the running beat — <c>DOTween.Goto(TweenId, t)</c> poses a frame for acceptance.</summary>
        public const string TweenId = "WinBeat";

        private const string SparkResource = "WinBeat/spark";

        private readonly GameModel _model;
        private readonly PieceViewRegistry _registry;
        private readonly TargetCardLocator _card;
        private readonly TargetPreviewConfig _previewConfig;
        private readonly WinBeatConfig _config;
        private readonly IDisposable _levelLoaded;

        private Overlay _overlay;
        private Beat _beat;

        public WinBeatView(GameModel model, PieceViewRegistry registry, TargetCardLocator card,
            TargetPreviewConfig previewConfig, WinBeatConfig config)
        {
            _model = model;
            _registry = registry;
            _card = card;
            _previewConfig = previewConfig;
            _config = config;
            // A (re)load mid-beat — DEV jump, scope swap — must not leave tweens on transforms the reload destroys.
            _levelLoaded = _model.LevelLoaded.Subscribe(_ => Reset());
        }

        public bool TryBegin(out Observable<Unit> beat)
        {
            beat = null;
            var camera = UnityEngine.Camera.main; // fully-qualified: the Gameplay.Camera namespace shadows the bare name
            var reason = Validate(camera, out var pieces);
            if (reason != null)
            {
                Debug.unityLogger.Log(LogType.Warning, $"[PlayScreen] win beat skipped: {reason}");
                return false;
            }

            beat = Observable.Create<Unit>(observer =>
            {
                Reset();
                _beat = new Beat(this, camera, pieces, () =>
                {
                    observer.OnNext(Unit.Default);
                    observer.OnCompleted();
                });
                var current = _beat;
                return Disposable.Create(() => current.Kill());
            });
            return true;
        }

        private string Validate(UnityEngine.Camera camera, out List<Flyer> pieces)
        {
            pieces = null;
            if (!_card.Current.Valid)
                return "no target card pose";
            if (_card.CardRoot == null)
                return "no target card root";
            if (_card.Target?.Slots == null)
                return "no target slots";
            if (camera == null)
                return "no Camera.main";
            var figure = _model.WinFigure;
            var level = _model.CurrentLevel;
            if (figure.Count == 0 || level == null)
                return "empty win figure";

            pieces = new List<Flyer>(figure.Count);
            foreach (var p in figure)
            {
                if (!_registry.TryGet(p.PlacementIndex, out var view))
                    return $"no live view for placement {p.PlacementIndex}";
                if (p.SlotIndex < 0 || p.SlotIndex >= _card.Target.Slots.Count)
                    return $"slot {p.SlotIndex} out of range";
                pieces.Add(new Flyer(view, p, level.Pieces[p.PlacementIndex].Type, _card.Target.Slots[p.SlotIndex]));
            }
            return null;
        }

        private Overlay GetOverlay()
        {
            if (_overlay == null)
                _overlay = Overlay.Create(_config);
            return _overlay;
        }

        /// <summary>Kill a running beat (frame stays as it is) and drop a flight root the card did not adopt.</summary>
        private void Reset()
        {
            _beat?.Kill();
            _beat?.DestroyFlight();
            _beat = null;
        }

        public void Dispose()
        {
            _levelLoaded?.Dispose();
            Reset();
            if (_overlay != null)
                UnityEngine.Object.Destroy(_overlay.gameObject);
            _overlay = null;
        }

        /// <summary>One won piece: its live view, the judge's record, its type and its slot.</summary>
        private readonly struct Flyer
        {
            public readonly PieceView View;
            public readonly WinFigurePiece Piece;
            public readonly PieceType Type;
            public readonly TargetSlot Slot;

            public Flyer(PieceView view, WinFigurePiece piece, PieceType type, TargetSlot slot)
            {
                View = view;
                Piece = piece;
                Type = type;
                Slot = slot;
            }
        }

        /// <summary>One played beat: the flight hierarchy, the timeline and the overlay state for it.</summary>
        private sealed class Beat
        {
            private readonly WinBeatConfig _c;
            private readonly UnityEngine.Camera _camera;
            private readonly Overlay _overlay;
            private readonly Transform _cardRoot;
            private readonly Vector3 _cardBaseScale;
            private readonly float _cardScale;
            private readonly Vector3 _cardOrigin;
            private readonly Vector3[] _frameCorners; // world, at the card's base scale
            private readonly float _windowShrink;     // frame → window (canvas behind the wooden border)
            private readonly Action _done;

            private readonly Transform _flight;
            private readonly Transform _pivot;
            private readonly PieceView[] _views;
            private readonly Vector3[] _restLocal; // relative to the bounce pivot, z = the piece's own depth
            private readonly Vector3[] _slotLocal;
            private readonly float[] _restAngle;
            private readonly float[] _landAngle;
            private readonly Vector3 _start;      // FigureCenter at z = 0
            private readonly Vector3 _control;    // Bézier control (xy)
            private readonly float _zFly;

            private readonly Spark[] _sparks;
            private readonly Vector2[] _flashPoints = new Vector2[4];
            private readonly Vector2[][] _flashPolygons;
            private static readonly int[][] QuadTriangles = { new[] { 0, 1, 2, 0, 2, 3 } };

            private Tween _tween;
            private bool _skipped;
            private bool _finished;

            public Beat(WinBeatView owner, UnityEngine.Camera camera, List<Flyer> pieces, Action done)
            {
                _c = owner._config;
                _camera = camera;
                _done = done;
                _flashPolygons = new[] { _flashPoints };
                _overlay = owner.GetOverlay();
                _cardRoot = owner._card.CardRoot;
                _cardBaseScale = _cardRoot.localScale;
                _cardScale = _cardBaseScale.x;
                _cardOrigin = _cardRoot.position;
                var pose = owner._card.Current;
                _frameCorners = pose.FrameWorldCorners ?? Array.Empty<Vector3>();
                _windowShrink = owner._previewConfig.BackdropEnabled && WoodArt.Frame != null
                    ? _c.FlashWindowFraction
                    : 1f;

                // Pivot = bbox of the figure's world outline at rest: centre x, PivotHeightFraction up (0.5 = centre).
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                foreach (var f in pieces)
                    foreach (var v in Geometry.WorldVertices(f.Type.Shape, f.Piece.RestPosition, f.Piece.RestRotation))
                    {
                        min = Vector2.Min(min, new Vector2(v.X, v.Y));
                        max = Vector2.Max(max, new Vector2(v.X, v.Y));
                    }
                var pivot = new Vector3((min.x + max.x) * 0.5f,
                    Mathf.LerpUnclamped(min.y, max.y, _c.PivotHeightFraction), 0f);

                var centre = new Vector3(pose.FigureCenter.X, pose.FigureCenter.Y, 0f);
                _start = centre;
                var pivotLocal = pivot - centre;
                _zFly = _cardOrigin.z - _c.FlightZAhead;
                _control = ArcControl(camera, centre, _cardOrigin);

                _flight = new GameObject("WinBeatFlight").transform;
                _flight.position = _start;
                _pivot = new GameObject("BouncePivot").transform;
                _pivot.SetParent(_flight, false);
                _pivot.localPosition = pivotLocal; // fixed: only the scale of the pivot animates

                var n = pieces.Count;
                _views = new PieceView[n];
                _restLocal = new Vector3[n];
                _slotLocal = new Vector3[n];
                _restAngle = new float[n];
                _landAngle = new float[n];
                var colours = new List<Color>(n);
                for (var i = 0; i < n; i++)
                {
                    var f = pieces[i];
                    _views[i] = f.View;
                    // The view's world pose IS the rest pose (last MirrorPose before the verdict); its z is the TASK-631
                    // depth rank, kept as the local z so the pieces' relative order survives the flight.
                    var z = f.View.transform.position.z;
                    _restLocal[i] = new Vector3(f.Piece.RestPosition.X - pivot.x, f.Piece.RestPosition.Y - pivot.y, z);
                    _slotLocal[i] = new Vector3(f.Slot.Position.X - pivot.x, f.Slot.Position.Y - pivot.y, z);
                    _restAngle[i] = f.Piece.RestRotation * Mathf.Rad2Deg;
                    _landAngle[i] = f.Piece.LandRotation * Mathf.Rad2Deg;
                    f.View.transform.SetParent(_pivot, true);
                    colours.Add(PieceColour(f.Type));
                }

                _sparks = _overlay.PrepareSparks(_c.SparkCount, colours, Seed(owner._model.CurrentLevel?.LevelId));
                _overlay.Show(Skip);

                _tween = DOVirtual.Float(0f, _c.BeatEnd, _c.BeatEnd, Apply)
                    .SetEase(Ease.Linear)
                    .SetUpdate(true)
                    .SetId(TweenId)
                    .SetLink(_overlay.gameObject)
                    .OnComplete(Finish);
                Apply(0f);
            }

            /// <summary>The whole frame as a function of beat time <paramref name="t"/> (s).</summary>
            private void Apply(float t)
            {
                if (_flight == null)
                    return;
                var c = _c;
                var u = Mathf.Clamp01((t - c.FlightStart) / Mathf.Max(c.FlightEnd - c.FlightStart, 1e-4f));
                var e = t >= c.FlightEnd ? 1f : DOVirtual.EasedValue(0f, 1f, u, c.FlightEase);
                var cardAlive = _cardRoot != null;

                // Landing: adopt the figure into the card once (and give it back on a backward Goto). The landing pose is
                // written at the card's BASE scale, so a jump straight into the punch still lands on the slots.
                if (cardAlive)
                {
                    var adopted = _flight.parent == _cardRoot;
                    if (t >= c.FlightEnd && !adopted)
                    {
                        _cardRoot.localScale = _cardBaseScale;
                        PlaceFlight(1f);
                        _flight.SetParent(_cardRoot, true);
                    }
                    else if (t < c.FlightEnd && adopted)
                    {
                        _cardRoot.localScale = _cardBaseScale;
                        _flight.SetParent(null, true);
                    }
                    _cardRoot.localScale = _cardBaseScale * Punch(t);
                }
                if (!cardAlive || _flight.parent != _cardRoot)
                    PlaceFlight(t < c.FlightStart ? -1f : e);

                // Bounce pivot (never moves): uniform grow → shrink → 1, then a light stretch along the flight.
                var bounce = Bounce(t);
                var flightStretch = u > 0f && u < 1f ? 1f + (c.FlightStretchMaxY - 1f) * Mathf.Sin(Mathf.PI * u) : 1f;
                _pivot.localScale = new Vector3(bounce, bounce * flightStretch, 1f);

                var shadow = 1f - Mathf.Clamp01(u / Mathf.Max(c.ShadowFadeFraction, 1e-4f));
                for (var i = 0; i < _views.Length; i++)
                {
                    var view = _views[i];
                    if (view == null)
                        continue;
                    var tr = view.transform;
                    tr.localPosition = Vector3.LerpUnclamped(_restLocal[i], _slotLocal[i], e);
                    tr.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(_restAngle[i], _landAngle[i], e));
                    tr.localScale = Vector3.one;
                    view.SetShadowWeight(shadow);
                }

                ApplyOverlay(t);
            }

            /// <param name="e">Flight progress 0..1; −1 = before the flight (on the field plane, z = 0).</param>
            private void PlaceFlight(float e)
            {
                if (e < 0f)
                {
                    _flight.position = _start;
                    _flight.localScale = Vector3.one;
                    return;
                }
                var p = Bezier(_start, _control, _cardOrigin, e);
                _flight.position = new Vector3(p.x, p.y, _zFly); // z jumps ahead of the card at the flight start (ortho)
                var s = Mathf.LerpUnclamped(1f, _cardScale, e);
                _flight.localScale = new Vector3(s, s, 1f);
            }

            // Uniform scale: 1 → GrowScale (at GrowPeak) → ShrinkScale (at ShrinkPeak) → 1 (at BounceEnd), smoothstep legs.
            private float Bounce(float t)
            {
                var c = _c;
                if (t <= 0f || t >= c.BounceEnd)
                    return 1f;
                if (t < c.GrowPeak)
                    return Mathf.Lerp(1f, c.GrowScale, Smooth(t / c.GrowPeak));
                if (t < c.ShrinkPeak)
                    return Mathf.Lerp(c.GrowScale, c.ShrinkScale,
                        Smooth((t - c.GrowPeak) / Mathf.Max(c.ShrinkPeak - c.GrowPeak, 1e-4f)));
                return Mathf.Lerp(c.ShrinkScale, 1f,
                    Smooth((t - c.ShrinkPeak) / Mathf.Max(c.BounceEnd - c.ShrinkPeak, 1e-4f)));
            }

            private float Punch(float t)
            {
                var r = Receive(t);
                return r <= 0f || r >= 1f ? 1f : 1f + (_c.ReceivePunch - 1f) * Mathf.Sin(Mathf.PI * DOVirtual.EasedValue(0f, 1f, r, Ease.OutQuad));
            }

            private float Receive(float t) =>
                (t - _c.ReceiveStart) / Mathf.Max(_c.ReceiveEnd - _c.ReceiveStart, 1e-4f);

            private void ApplyOverlay(float t)
            {
                var c = _c;
                var r = Receive(t);
                var k = Punch(t);

                // Window flash: the frame's corners scaled to the window and by the punch, around the card's origin.
                var flashAlpha = 0f;
                if (r > 0f && r < 1f)
                {
                    var attack = Mathf.Max(c.FlashAttackFraction, 1e-4f);
                    flashAlpha = c.FlashAlpha * (r < attack ? r / attack : 1f - (r - attack) / (1f - attack));
                }
                _overlay.SetFlash(flashAlpha, flashAlpha > 0f && ProjectWindow(k));

                // Sparks: from the receive start to the beat end, off the frame perimeter along their outward direction.
                var life = (t - c.ReceiveStart) / Mathf.Max(c.BeatEnd - c.ReceiveStart, 1e-4f);
                var frameSide = FrameSideOnCanvas();
                foreach (var s in _sparks)
                    s.Apply(life, frameSide * c.SparkDistance, this);
            }

            private bool ProjectWindow(float k)
            {
                if (_frameCorners.Length < 4)
                    return false;
                for (var i = 0; i < 4; i++)
                {
                    var world = _cardOrigin + (_frameCorners[i] - _cardOrigin) * (k * _windowShrink);
                    if (!OverlayProjection.TryToCanvas(_camera, _overlay.CanvasRect, world, out _flashPoints[i]))
                        return false;
                }
                _overlay.Flash.SetPolygons(_flashPolygons, QuadTriangles);
                return true;
            }

            private float FrameSideOnCanvas()
            {
                if (_frameCorners.Length < 4
                    || !OverlayProjection.TryToCanvas(_camera, _overlay.CanvasRect, _frameCorners[0], out var a)
                    || !OverlayProjection.TryToCanvas(_camera, _overlay.CanvasRect, _frameCorners[1], out var b))
                    return 0f;
                return (b - a).magnitude;
            }

            /// <summary>A point on the frame's perimeter (0..1 along its length) and its outward normal, on the canvas.</summary>
            public bool PerimeterPoint(float along, out Vector2 point, out Vector2 normal)
            {
                point = normal = default;
                if (_frameCorners.Length < 4)
                    return false;
                var side = Mathf.Repeat(along, 1f) * 4f;
                var i = Mathf.Min((int)side, 3);
                var a = _frameCorners[i];
                var b = _frameCorners[(i + 1) % 4];
                var world = Vector3.Lerp(a, b, side - i);
                if (!OverlayProjection.TryToCanvas(_camera, _overlay.CanvasRect, world, out point)
                    || !OverlayProjection.TryToCanvas(_camera, _overlay.CanvasRect, _cardOrigin, out var centre))
                    return false;
                // Corners run counter-clockwise from bottom-left, so the outward normal is the edge turned clockwise.
                OverlayProjection.TryToCanvas(_camera, _overlay.CanvasRect, a, out var ca);
                OverlayProjection.TryToCanvas(_camera, _overlay.CanvasRect, b, out var cb);
                var edge = (cb - ca).normalized;
                normal = new Vector2(edge.y, -edge.x);
                if (Vector2.Dot(normal, point - centre) < 0f)
                    normal = -normal;
                return true;
            }

            /// <summary>Tap: jump to the landing and play the rest (receive + settle) in SkipTailSeconds. Once.</summary>
            private void Skip()
            {
                if (_skipped || _finished || _tween == null || !_tween.IsActive())
                    return;
                _skipped = true;
                var from = Mathf.Max(_tween.Elapsed(false), _c.FlightEnd);
                if (_tween.Elapsed(false) < _c.FlightEnd)
                    _tween.Goto(_c.FlightEnd, true);
                var rest = _c.BeatEnd - from;
                if (rest > 0f && _c.SkipTailSeconds > 0f)
                    _tween.timeScale = rest / _c.SkipTailSeconds;
            }

            private void Finish()
            {
                if (_finished)
                    return;
                _finished = true;
                _tween = null;
                Settle();
                _done?.Invoke();
            }

            /// <summary>Kill (flow ceiling, teardown, reload): the pieces stay where the kill caught them.</summary>
            public void Kill()
            {
                if (_finished)
                    return;
                _finished = true;
                _tween?.Kill();
                _tween = null;
                Settle();
            }

            private void Settle()
            {
                if (_cardRoot != null)
                    _cardRoot.localScale = _cardBaseScale;
                if (_overlay != null)
                    _overlay.Hide();
            }

            /// <summary>Destroy the flight root unless the card adopted it (then the card's rebuild takes it along).</summary>
            public void DestroyFlight()
            {
                if (_flight != null && (_cardRoot == null || _flight.parent != _cardRoot))
                    UnityEngine.Object.Destroy(_flight.gameObject);
            }

            private static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t)
            {
                var ab = Vector3.LerpUnclamped(a, control, t);
                var bc = Vector3.LerpUnclamped(control, b, t);
                return Vector3.LerpUnclamped(ab, bc, t);
            }

            // Control point: the midpoint pushed ArcFraction of the view width off the straight line, toward the side
            // with more room (the camera's centre); a vertical flight from the centre bends to +x.
            private Vector3 ArcControl(UnityEngine.Camera camera, Vector3 from, Vector3 to)
            {
                var mid = (from + to) * 0.5f;
                var dir = (Vector2)(to - from);
                if (dir.sqrMagnitude < 1e-8f)
                    return mid;
                dir.Normalize();
                var perp = new Vector2(-dir.y, dir.x);
                var viewWidth = 2f * camera.orthographicSize * Mathf.Max(camera.aspect, 1e-3f);
                var toCentre = (Vector2)camera.transform.position - (Vector2)mid;
                var sign = Vector2.Dot(perp, toCentre);
                if (Mathf.Abs(sign) < 1e-4f)
                    sign = perp.x >= 0f ? 1f : -1f;
                var offset = perp * (Mathf.Sign(sign) * _c.ArcFraction * viewWidth);
                return mid + new Vector3(offset.x, offset.y, 0f);
            }

            private static float Smooth(float x) => x * x * (3f - 2f * x);

            private static Color PieceColour(PieceType type)
            {
                var colour = Color.white;
                if (!string.IsNullOrEmpty(type.Color) && ColorUtility.TryParseHtmlString(type.Color, out var parsed))
                    colour = parsed;
                colour = WoodTuning.Current.ResolveLevelColor(colour, type.Kind);
                colour.a = 1f;
                return colour;
            }

            // Stable FNV-1a of the level id: the sparks land on the same spots in every acceptance capture.
            private static int Seed(string levelId)
            {
                unchecked
                {
                    var h = (int)2166136261;
                    if (levelId != null)
                        foreach (var ch in levelId)
                            h = (h ^ ch) * 16777619;
                    return h;
                }
            }
        }

        /// <summary>One spark: where on the frame perimeter it starts, which way it flies, its colour.</summary>
        private sealed class Spark
        {
            public RectTransform Rect;
            public Image Image;
            public float Along;
            public float AngleOffset; // radians off the outward normal
            public Color Colour;
            public Vector2 Stretch = Vector2.one; // the code-built fallback is a streak along its flight

            public void Apply(float life, float distance, Beat beat)
            {
                if (life <= 0f || life >= 1f || !beat.PerimeterPoint(Along, out var start, out var normal))
                {
                    Image.enabled = false;
                    return;
                }
                var cos = Mathf.Cos(AngleOffset);
                var sin = Mathf.Sin(AngleOffset);
                var dir = new Vector2(normal.x * cos - normal.y * sin, normal.x * sin + normal.y * cos);
                var travel = DOVirtual.EasedValue(0f, 1f, life, Ease.OutQuad);
                Rect.anchoredPosition = start + dir * (distance * travel);
                var k = Mathf.Lerp(1f, 0.4f, life);
                Rect.localScale = new Vector3(Stretch.x * k, Stretch.y * k, 1f);
                Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                var c = Colour;
                c.a = 1f - life;
                Image.color = c;
                Image.enabled = true;
            }
        }

        /// <summary>Code-built overlay (idiom of the tour overlay): the window flash, the spark pool and the tap catcher.
        /// Active only while a beat runs.</summary>
        private sealed class Overlay : MonoBehaviour
        {
            private const int SortingOrder = 32200;
            private static readonly Vector2 ReferenceResolution = new(1080f, 1920f);
            private static bool _sparkWarned;

            private WinBeatConfig _config;
            private Sprite _sparkSprite;
            private RevealTapCatcher _catcher;
            private RectTransform _sparkRoot;
            private readonly List<Spark> _pool = new();
            private Action _onTap;

            public RectTransform CanvasRect { get; private set; }
            public UiPolygonFill Flash { get; private set; }

            public static Overlay Create(WinBeatConfig config)
            {
                var go = new GameObject("WinBeatOverlay");
                DontDestroyOnLoad(go);

                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;
                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = ReferenceResolution;
                scaler.matchWidthOrHeight = 0f;
                go.AddComponent<GraphicRaycaster>(); // the tap catcher needs it; nothing else here is raycastable

                var overlay = go.AddComponent<Overlay>();
                overlay.Build(config);
                go.SetActive(false);
                return overlay;
            }

            private void Build(WinBeatConfig config)
            {
                _config = config;
                CanvasRect = (RectTransform)transform;

                var flash = NewNode("WindowFlash", CanvasRect);
                flash.gameObject.AddComponent<CanvasRenderer>();
                Flash = flash.gameObject.AddComponent<UiPolygonFill>();
                Flash.raycastTarget = false;
                Flash.enabled = false;

                _sparkRoot = NewNode("Sparks", CanvasRect);

                // Full-screen catcher LAST so it is on top of the overlay; it draws nothing.
                var catcherRt = NewNode("TapCatcher", CanvasRect);
                catcherRt.anchorMin = Vector2.zero;
                catcherRt.anchorMax = Vector2.one;
                catcherRt.sizeDelta = Vector2.zero;
                catcherRt.gameObject.AddComponent<CanvasRenderer>();
                _catcher = catcherRt.gameObject.AddComponent<RevealTapCatcher>();
                _catcher.Tapped += () => _onTap?.Invoke();

                _sparkSprite = Resources.Load<Sprite>(SparkResource);
                if (_sparkSprite == null && !_sparkWarned)
                {
                    _sparkWarned = true;
                    Debug.unityLogger.Log(LogType.Warning,
                        $"[PlayScreen] no Resources/{SparkResource} sprite — win-beat sparks fall back to code-built diamonds");
                }
            }

            public Spark[] PrepareSparks(int count, IReadOnlyList<Color> colours, int seed)
            {
                count = Mathf.Max(count, 0);
                while (_pool.Count < count)
                    _pool.Add(NewSpark(_pool.Count));
                var rng = new System.Random(seed);
                var spread = _config.SparkSpreadDegrees * Mathf.Deg2Rad;
                var sparks = new Spark[count];
                for (var i = 0; i < _pool.Count; i++)
                    _pool[i].Image.enabled = false;
                for (var i = 0; i < count; i++)
                {
                    var s = _pool[i];
                    s.Along = (i + (float)rng.NextDouble()) / count;
                    s.AngleOffset = ((float)rng.NextDouble() * 2f - 1f) * spread;
                    s.Colour = colours.Count > 0 ? colours[i % colours.Count] : Color.white;
                    sparks[i] = s;
                }
                return sparks;
            }

            private Spark NewSpark(int index)
            {
                var rt = NewNode($"Spark{index}", _sparkRoot);
                rt.sizeDelta = Vector2.one * _config.SparkSize;
                rt.gameObject.AddComponent<CanvasRenderer>();
                var image = rt.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                image.sprite = _sparkSprite;
                image.enabled = false;
                return new Spark
                {
                    Rect = rt,
                    Image = image,
                    Stretch = _sparkSprite == null
                        ? new Vector2(_config.FallbackSparkLength, _config.FallbackSparkThickness)
                        : Vector2.one,
                };
            }

            public void Show(Action onTap)
            {
                _onTap = onTap;
                gameObject.SetActive(true);
                _catcher.raycastTarget = true;
            }

            public void Hide()
            {
                _onTap = null;
                _catcher.raycastTarget = false;
                Flash.enabled = false;
                Flash.ClearPolygons();
                foreach (var s in _pool)
                    s.Image.enabled = false;
                gameObject.SetActive(false);
            }

            public void SetFlash(float alpha, bool projected)
            {
                Flash.color = new Color(1f, 1f, 1f, alpha);
                Flash.enabled = alpha > 0.001f && projected;
            }

            // Centre-anchored, zero-size node at the canvas origin (the TASK-078 / TASK-380 trap — see TourView).
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
    }
}
