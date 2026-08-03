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
    /// hooks in as the (otherwise null) <see cref="GameController.StepObserver"/>, which the production and
    /// headless paths leave unset.
    ///
    /// Output is a single line per step tagged <c>[STEPLOG]</c> so it isolates with
    /// <c>adb logcat | grep STEPLOG</c> on Android (Xcode console on iOS, the Console in the Editor). All
    /// numbers are formatted with <see cref="CultureInfo.InvariantCulture"/> so a comma-locale device and a
    /// dot-locale editor produce identical, diffable text.
    ///
    /// VOLUME is bounded three ways (the owner's constraint — a prior task drowned in 74 logs/s): nothing is
    /// logged until a launch actually starts (a piece enters Falling); the episode auto-closes after the
    /// field has been quiet for <see cref="RestTailSteps"/> steps; and a hard <see cref="MaxEpisodeSteps"/>
    /// floodgate closes any pathological hang. It re-arms on every <see cref="GameModel.LevelLoaded"/>
    /// (covers DevJumpToLevel switches AND same-index LOSS→retry reloads).
    ///
    /// ALIGNMENT: <c>s=</c> is the step index since launch start (episode step 0 = first Falling step), and
    /// each token carries <c>age=</c> = steps since THAT piece first started moving. Human tap timing is not
    /// injectable, so absolute wall-clock is not comparable across runs; anchor the editor↔device diff on the
    /// launched piece's <c>age</c> (its post-impulse flight is deterministic once released).
    /// </summary>
    public sealed class StepPoseLogger : IStartable, IStepObserver, IDisposable
    {
        // Steps of continuous "nothing Falling" after a launch before the episode auto-closes (~0.5 s @ 1/60).
        private const int RestTailSteps = 30;
        // Hard floodgate: close the episode no matter what after this many logged steps (~33 s @ 1/60).
        private const int MaxEpisodeSteps = 2000;

        private readonly GameController _controller;
        private readonly GameModel _model;
        private readonly ShapeFillTuning _tuning;

        private readonly StringBuilder _line = new();
        // Per instance: last logged position (for the derived velocity) and the episode-step it first moved.
        private readonly Dictionary<int, Vec2> _prevPos = new();
        private readonly Dictionary<int, int> _firstMoveStep = new();

        private IDisposable _levelLoadedSub;
        private bool _begun;      // BEGIN line emitted for the current level load
        private bool _active;     // a launch episode is in progress (something Falling has been seen)
        private bool _closed;     // episode finished — stop logging until the next LevelLoaded
        private int _episodeStep; // fixed steps since launch start (0 = first Falling step)
        private int _restTail;    // consecutive steps with no Falling piece since the episode went active

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
        }

        public void OnStep()
        {
            var level = _model.CurrentLevel;
            if (level == null || _closed)
                return;

            if (!_begun)
            {
                LogBegin(level);
                _begun = true;
            }

            var anyFalling = false;
            var pieces = _model.Pieces;
            for (var i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].State == PieceLifecycle.Falling)
                {
                    anyFalling = true;
                    break;
                }
            }

            // Do not spam the idle steps before the player's first tap — the episode (and s=0) begins at the
            // first Falling step, the launch anchor both platforms share.
            if (!_active)
            {
                if (!anyFalling)
                    return;
                _active = true;
                _episodeStep = 0;
            }
            else
            {
                _episodeStep++;
            }

            LogStep(level.LevelId, pieces);

            if (anyFalling)
            {
                _restTail = 0;
            }
            else if (++_restTail >= RestTailSteps)
            {
                LogEnd(level.LevelId, "rest");
                _closed = true;
                return;
            }

            if (_episodeStep >= MaxEpisodeSteps)
            {
                LogEnd(level.LevelId, "cap");
                _closed = true;
            }
        }

        private void ReArm()
        {
            _begun = false;
            _active = false;
            _closed = false;
            _episodeStep = 0;
            _restTail = 0;
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

        private void LogStep(string levelId, IReadOnlyList<PieceInstance> pieces)
        {
            _line.Clear();
            _line.Append("[STEPLOG] ").Append(levelId)
                .Append(" s=").Append(_episodeStep.ToString("0000", CultureInfo.InvariantCulture));

            for (var i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];

                // First step this piece is in motion → anchor its per-piece age and seed the velocity baseline.
                var moving = p.State != PieceLifecycle.Idle;
                if (moving && !_firstMoveStep.ContainsKey(p.InstanceId))
                    _firstMoveStep[p.InstanceId] = _episodeStep;
                var age = _firstMoveStep.TryGetValue(p.InstanceId, out var fm) ? _episodeStep - fm : -1;

                // Derived velocity = Δposition / FixedStep (domain units/s). NOT the solver's linearVelocity
                // (that needs the mechanic's private body-id map); with a fixed dt this closely tracks it and
                // is exactly the per-step displacement the divergence question is about. Zero on a piece's
                // first logged step (no previous sample).
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
                " steps=", _episodeStep.ToString("0000", CultureInfo.InvariantCulture),
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
        }
    }
}
#endif
