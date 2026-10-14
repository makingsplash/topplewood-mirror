#if GPIE_DEVELOPMENT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Gameplay.Core;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.Dev
{
    /// <summary>
    /// Dev/QA per-fixed-step pose logger (TASK-070): captures machine-diffable numbers for every piece on
    /// each physics step so the SAME build run in the Editor and on device can be compared line-by-line and
    /// the first step where trajectories diverge can be pinned (ADR-0001 M0.2, the deferred cross-device gap
    /// that first fired on <c>video_launcher</c>). Editor/dev builds only — the whole file is guarded by
    /// <c>GPIE_DEVELOPMENT</c> (defined for Android/iPhone/Standalone) and never compiles into a shipped
    /// player build, exactly like <see cref="BringupDebugMenuHost"/>.
    ///
    /// READ-ONLY: it reads the domain model's already-mirrored poses (<see cref="GameModel.Pieces"/>) and
    /// writes only to the Unity log — it never touches the simulation or the model, so physics, the pose
    /// sequence and <c>poseHash</c> are byte-for-byte identical whether or not this logger is attached. It
    /// hooks in as the (otherwise null) <see cref="GameController.StepObserver"/>. Registered as an entry point
    /// in both the bringup and the prod (dev-build) scopes; the headless sim attaches its own instance.
    ///
    /// Output is a single line per step tagged <c>[STEPLOG]</c> so it isolates with
    /// <c>adb logcat | grep STEPLOG</c> on Android (Xcode console on iOS, the Console in the Editor). All
    /// numbers are formatted with <see cref="CultureInfo.InvariantCulture"/> so a comma-locale device and a
    /// dot-locale editor produce identical, diffable text.
    ///
    /// SESSION = the WHOLE match, load → verdict (TASK-070 return, owner 10:30): the logger is armed at level
    /// load, emits <c>BEGIN</c> + a <c>READY</c> snapshot of every piece's start pose (so idle tap targets are
    /// in the log — see the world→screen note below), then logs each step where at least one piece is
    /// <c>Falling</c>, all the way to the level's verdict. A previous version closed the session on the FIRST
    /// global rest — which lands right after the preplaced plank settles, BEFORE any player tap — so it only
    /// ever captured the initial static settling and nothing of the actual launch. Intermediate rests (the
    /// pauses between taps) NO LONGER close the session; only the level verdict does.
    ///
    /// VOLUME is bounded two ways (the owner's constraint — a prior task drowned in 74 logs/s): a per-step line
    /// is emitted ONLY while something is Falling (the pre-tap idle and the seconds-long gaps between taps
    /// advance the step clock but emit nothing), and a hard <see cref="MaxLoggedSteps"/> floodgate closes any
    /// pathological hang that never resolves. The session ends on <see cref="GameModel.LevelResolved"/> with
    /// the outcome (<c>END … reason=won|lost</c>) and re-arms on every <see cref="GameModel.LevelLoaded"/>
    /// (covers DevJumpToLevel switches AND same-index LOSS→retry reloads).
    ///
    /// ALIGNMENT: <c>s=</c> is the fixed-step index since level load (a true physics-step clock; gaps in it are
    /// the un-logged idle spans between taps), and each token carries <c>age=</c> = steps since THAT piece
    /// first started moving. Human/injected tap timing is not frame-exact, so absolute wall-clock is not
    /// comparable across runs; anchor the editor↔device diff on the launched piece's <c>age</c> — its
    /// post-impulse flight is deterministic once released, so the first <c>age</c> at which Δx/Δy grows is the
    /// divergence step. World coordinates are platform-invariant; convert to device tap pixels outside the log
    /// (the mapping is screen-resolution-specific — see the READY line and the artifact, §3).
    /// </summary>
    public sealed class StepPoseLogger : IStartable, IStepObserver, IDisposable
    {
        // Hard floodgate: close the session after this many EMITTED (Falling) lines, so a level that never
        // reaches a verdict cannot flood the log. Idle steps do not count — this bounds real motion, not
        // patient waiting — so a full match stays well under it (~50 s of continuous motion @ 1/60).
        private const int MaxLoggedSteps = 3000;

        private readonly GameController _controller;
        private readonly GameModel _model;
        private readonly ShapeFillTuning _tuning;

        private readonly StringBuilder _line = new();
        // Per instance: last logged position (for the derived velocity) and the step it first moved.
        private readonly Dictionary<int, Vec2> _prevPos = new();
        private readonly Dictionary<int, int> _firstMoveStep = new();

        private IDisposable _levelLoadedSub;
        private IDisposable _levelResolvedSub;
        private bool _begun;      // BEGIN/READY emitted for the current level load
        private bool _closed;     // session finished (verdict or cap) — stop logging until the next LevelLoaded
        private int _stepIndex;   // fixed steps since level load (true physics-step clock)
        private int _loggedCount; // emitted (Falling) lines this session — drives the floodgate

        public StepPoseLogger(GameController controller, GameModel model, ShapeFillTuning tuning)
        {
            _controller = controller;
            _model = model;
            _tuning = tuning;
        }

        public void Start()
        {
            // Attach as the diagnostic step hook. GameController is a scope singleton resolvable by concrete
            // type (GameplayCoreInstaller Register<GameController>), and the production/headless paths never
            // set this — so we are the only writer.
            _controller.StepObserver = this;

            // Re-arm on every level (re)load — DevJumpToLevel and same-index LOSS→retry both re-fire this, and
            // a retry reuses the LevelId so a plain id-change check would miss it.
            _levelLoadedSub = _model.LevelLoaded.Subscribe(_ => ReArm());
            // Close the session on the verdict, NOT on rest: video_launcher is a catapult whose beam rests
            // several times before the launch, so rest is not terminal — the level's Won/Lost outcome is.
            _levelResolvedSub = _model.LevelResolved.Subscribe(_ => OnResolved());
        }

        public void OnStep()
        {
            var level = _model.CurrentLevel;
            if (level == null || _closed)
                return;

            var pieces = _model.Pieces;

            if (!_begun)
            {
                // Lazy BEGIN so the auto-loaded first level (campaign resume runs before this IStartable) is
                // still captured; ReArm re-clears this for every subsequent DevJumpToLevel / retry.
                LogBegin(level);
                LogReady(level.LevelId, pieces);
                _begun = true;
                _stepIndex = 0;
            }
            else
            {
                _stepIndex++;
            }

            var anyFalling = false;
            for (var i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].State == PieceLifecycle.Falling)
                {
                    anyFalling = true;
                    break;
                }
            }

            // Emit a per-step line ONLY while something is Falling — this bounds volume; the pre-tap idle and
            // the gaps between taps just advance _stepIndex (visible as jumps in s=) and never close the
            // session. The session ends on the verdict (OnResolved), not here.
            if (!anyFalling)
                return;

            LogStep(level.LevelId, pieces);

            if (++_loggedCount >= MaxLoggedSteps)
            {
                LogEnd(level.LevelId, "cap");
                _closed = true;
            }
        }

        private void OnResolved()
        {
            if (_closed || !_begun)
                return;
            var level = _model.CurrentLevel;
            if (level == null)
                return;
            LogEnd(level.LevelId, _model.Outcome.CurrentValue == GameOutcome.Won ? "won" : "lost");
            _closed = true;
        }

        private void ReArm()
        {
            _begun = false;
            _closed = false;
            _stepIndex = 0;
            _loggedCount = 0;
            _prevPos.Clear();
            _firstMoveStep.Clear();
        }

        private void LogBegin(LevelData level)
        {
            Debug.Log(string.Concat(
                "[STEPLOG] BEGIN level=", level.LevelId,
                " seed=", level.Seed.ToString(CultureInfo.InvariantCulture),
                " fixedStep=", _tuning.FixedStep.ToString("0.0000000", CultureInfo.InvariantCulture),
                " platform=", Application.platform.ToString(),
                " editor=", Application.isEditor ? "1" : "0",
                " targetFPS=", Application.targetFrameRate.ToString(CultureInfo.InvariantCulture)));
        }

        // Snapshot of every piece's start pose BEFORE any motion — the Idle pieces here are the tap targets, so
        // the owner can compute device tap pixels from world x/y (mapping is resolution-specific; artifact §3).
        private void LogReady(string levelId, IReadOnlyList<PieceInstance> pieces)
        {
            _line.Clear();
            _line.Append("[STEPLOG] READY ").Append(levelId);
            for (var i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                _line.Append(" | ")
                    .Append(p.InstanceId.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(p.Kind).Append(':').Append(p.State.ToString())
                    .Append(" x=").Append(F5(p.Position.X))
                    .Append(" y=").Append(F5(p.Position.Y))
                    .Append(" r=").Append(F5(p.Rotation));
            }
            Debug.Log(_line.ToString());
        }

        private void LogStep(string levelId, IReadOnlyList<PieceInstance> pieces)
        {
            _line.Clear();
            _line.Append("[STEPLOG] ").Append(levelId)
                .Append(" s=").Append(_stepIndex.ToString("0000", CultureInfo.InvariantCulture));

            for (var i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];

                // First step this piece is in motion → anchor its per-piece age and seed the velocity baseline.
                var moving = p.State != PieceLifecycle.Idle;
                if (moving && !_firstMoveStep.ContainsKey(p.InstanceId))
                    _firstMoveStep[p.InstanceId] = _stepIndex;
                var age = _firstMoveStep.TryGetValue(p.InstanceId, out var fm) ? _stepIndex - fm : -1;

                // Derived velocity = Δposition / FixedStep (domain units/s). NOT the solver's linearVelocity
                // (that needs the mechanic's private body-id map); with a fixed dt this closely tracks it and
                // is exactly the per-step displacement the divergence question is about. Zero on a piece's
                // first logged step (no previous sample). Idle pieces do not move between logged steps, so the
                // gap between two logged steps that straddle an idle span still yields a correct one-step Δ.
                float vx = 0f, vy = 0f;
                if (_prevPos.TryGetValue(p.InstanceId, out var prev))
                {
                    vx = (p.Position.X - prev.X) / _tuning.FixedStep;
                    vy = (p.Position.Y - prev.Y) / _tuning.FixedStep;
                }
                _prevPos[p.InstanceId] = p.Position;

                _line.Append(" | ")
                    .Append(p.InstanceId.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(p.Kind).Append(':').Append(p.State.ToString())
                    .Append(" age=").Append(age.ToString(CultureInfo.InvariantCulture))
                    .Append(" x=").Append(F5(p.Position.X))
                    .Append(" y=").Append(F5(p.Position.Y))
                    .Append(" r=").Append(F5(p.Rotation))
                    .Append(" vx=").Append(F3(vx))
                    .Append(" vy=").Append(F3(vy));
            }

            Debug.Log(_line.ToString());
        }

        private void LogEnd(string levelId, string reason)
        {
            Debug.Log(string.Concat(
                "[STEPLOG] END level=", levelId,
                " steps=", _stepIndex.ToString("0000", CultureInfo.InvariantCulture),
                " logged=", _loggedCount.ToString("0000", CultureInfo.InvariantCulture),
                " reason=", reason));
        }

        // Signed fixed-width numbers so columns line up and a text diff aligns cleanly.
        private static string F5(float v) => v.ToString("+0.00000;-0.00000", CultureInfo.InvariantCulture);
        private static string F3(float v) => v.ToString("+0.000;-0.000", CultureInfo.InvariantCulture);

        public void Dispose()
        {
            // Detach so a torn-down logger cannot keep a dangling reference alive on a surviving controller
            // (both are scope singletons, but detaching is cheap and correct).
            if (_controller != null && ReferenceEquals(_controller.StepObserver, this))
                _controller.StepObserver = null;
            _levelLoadedSub?.Dispose();
            _levelLoadedSub = null;
            _levelResolvedSub?.Dispose();
            _levelResolvedSub = null;
        }
    }
}
#endif
