using System;
using System.Collections.Generic;
using Gameplay.Core.Adapters;
using R3;

namespace Gameplay.Core.Mechanic
{
    /// <summary>
    /// Fills the <c>IMechanicController</c> seam with the pivoted shape_fill loop (GAME_RULES §2–3), all pure
    /// C# (headless-testable via ScriptedPoseSimulation): all pieces are pre-placed Idle over their target
    /// slots; a tap hit-tests which Idle piece it hit and materialises it into free fall in the SAME step —
    /// however many pieces are already falling (TASK-069: player input is sacrosanct, so a tap never waits;
    /// winnability is an AUTHORING property, not an input limit — this reverts the TASK-061 serialized release
    /// queue). TASK-374 (co-release as a CONDITION): a level may DECLARE groups of pieces
    /// (<see cref="LevelData.CoReleaseGroups"/>) that fall ONLY when tapped together — a tap on a group member
    /// alone does NOTHING (no drop, no "armed" state, no guaranteed wait); the group releases synchronously in one
    /// step once every member is tapped inside a short coincidence window (<c>ShapeFillTuning.CoReleaseWindowSteps</c>),
    /// and an incomplete group goes silent when the window expires. This REPLACES the TASK-023 balance coalescence
    /// helper (which armed a piece, waited a fixed window, then dropped even a lone piece). A piece NOT in any group
    /// releases immediately on tap, in every mode. Releasing materialises the body so it falls; poses are mirrored
    /// into PieceInstances; the Idle→Falling→Placed/Lost FSM runs; and on global rest a pose-match decides Won/Lost.
    /// Physics itself lives behind IShapeFillSimulation.
    ///
    /// TASK-043 (assist-layer removal): the whole pre-physics assist layer is gone — the in-fall snap-to-slot
    /// crystallisation, the soft-settle glide, the hybrid balance load-snap, and the TASK-042 honesty scaffolding
    /// (baseline map, snap-admissibility, win-only two-pass, support/overlap guards, reason counters) that existed
    /// only to gate those assists. A released piece now falls, hits, rotates and comes to rest under honest physics.
    ///
    /// TASK-305 (freeze-at-rest removal, owner variant A): the freeze mechanism is GONE from every mode. A rested
    /// piece is marked Placed at its physical rest pose but stays a live DYNAMIC body — nothing is ever locked
    /// kinematic (the <c>FreezeAt</c> seam is deleted). The verdict is now UNIFORM across modes: it is judged only at
    /// GLOBAL rest of the whole field (the balance path, extended to all modes), so removing the support from under a
    /// placed piece makes the construction react instead of hanging (the owner's counterweight_span defect). The
    /// pose-match JUDGES the settled result from those live physical poses (posTol/angTol + the rigidity spread in
    /// <see cref="PoseMatchEvaluator"/>). Determinism now holds ONLY for self-supporting figures; a non-self-supporting
    /// figure (cantilever/overhang/top-heavy) is metastable and shows up as a sweep victim (ADR-0005). The honesty
    /// detector remains, but sim-local (see <c>SimRun</c>), as a regression instrument.
    /// </summary>
    public sealed class ShapeFillMechanicController : IMechanicController, IDisposable
    {
        private readonly GameModel _model;
        private readonly IShapeFillSimulation _simulation;
        private readonly IMatchEvaluator _match;
        private readonly LevelDataValidator _validator;
        private readonly IPieceViewFactory _viewFactory;
        private readonly ShapeFillTuning _tuning;
        // TASK-085: screen-space tap halo. r=0 (NullTapTolerance, sim/headless) ⇒ PickIdlePieceAt is byte-for-byte
        // the old containment-only pick; a live CameraTapTolerance widens the miss into the nearest piece in radius.
        private readonly IGameplayTapTolerance _tapTolerance;

        private readonly Dictionary<int, int> _bodyByInstance = new();
        private readonly Dictionary<int, IPieceView> _viewByInstance = new();
        private readonly Dictionary<int, PieceType> _typeByInstance = new();
        private readonly List<PlacedPiece> _placedScratch = new();
        // TASK-634: the instance behind each _placedScratch entry (same index) and the judge's (slot, piece) pairs —
        // the won figure is built from these, so the win beat reuses the verdict's layout instead of re-matching.
        private readonly List<PieceInstance> _placedInstanceScratch = new();
        private readonly List<MatchPair> _pairScratch = new();
        // Reused per tap so resolving a tap allocates only the per-piece world-vertex arrays (as it did before).
        private readonly List<PieceHitResolver.HitCandidate> _hitCandidates = new();

        private IDisposable _levelLoadedSub;
        private LevelData _level;
        // Time the whole field has stayed below the rest speed (ALL modes since TASK-305) — the verdict fires only
        // once it exceeds RestDurationSeconds (every Dynamic piece has stopped moving).
        private float _globalRestTimer;

        // Monotone level-load counter (TASK-069 crash fix). Bumped once per (re)load in OnLevelLoaded so Tick can
        // detect its own model being TORN DOWN AND REBUILT under its per-piece iteration: on device a mid-tick
        // Resolve reloads the level SYNCHRONOUSLY (CampaignProgressionController subscribes LevelResolved →
        // ReloadLevel → GameModel.LoadLevel, which _pieces.Clear()s and re-adds). Neither the phase (the reload
        // flips it back to Running) nor the LevelData instance (reused on LOSS→retry) is a reliable "did we
        // reload?" sentinel — a generation counter is. The sim graph has NO reload subscriber (SimRun only does
        // _model.Outcome.Subscribe), so it never bumps mid-tick and never reproduces this — which is exactly why
        // the seeded sweep is structurally blind to the whole class of reentrancy defect.
        private int _levelGeneration;

        // ── Co-release coincidence groups (TASK-374, replaces the TASK-023 balance coalescence buffer). A level may
        //    declare groups of pieces that fall ONLY when tapped together (LevelData.CoReleaseGroups). A tap on a
        //    member opens a silent window; when every member is tapped inside it the whole group releases in ONE step
        //    (a symmetric pair enters the physics synchronously → the free beam stays level); an incomplete group goes
        //    silent when the window expires (nothing falls, nothing is shown). Transient runtime state — NOT persisted.
        //    Built per level in OnLevelLoaded; empty when the level declares no groups (→ every tap releases now). ──
        // placementIndex → the id of the group it belongs to (a piece is in at most one group; validator enforces it).
        private readonly Dictionary<int, int> _groupOfPlacement = new();
        // Per group (by id): the placement indices tapped so far this window, and how many steps the window still has
        // open (0 ⇒ closed). windowStepsLeft>0 with a non-empty tapped set is a group "in progress".
        private readonly List<HashSet<int>> _groupTapped = new();
        private readonly List<int> _groupWindowStepsLeft = new();
        // Last computed global-rest state, exposed via ReadyForNextRelease so the sim driver releases the next
        // group only once the beam + loads have settled — recomputed each tick the field is not falling.
        private bool _lastGlobalRestReached;

        public ShapeFillMechanicController(
            GameModel model,
            IShapeFillSimulation simulation,
            IMatchEvaluator match,
            LevelDataValidator validator,
            IPieceViewFactory viewFactory,
            ShapeFillTuning tuning,
            IGameplayTapTolerance tapTolerance)
        {
            _model = model;
            _simulation = simulation;
            _match = match;
            _validator = validator;
            _viewFactory = viewFactory;
            _tuning = tuning;
            _tapTolerance = tapTolerance;

            _levelLoadedSub = _model.LevelLoaded.Subscribe(_ => OnLevelLoaded());
        }

        // TASK-237 / TASK-374: exposed for the Hint booster — a co-release group "in progress" (a member tapped, its
        // window still open) means the piece is mid-gesture, so revealing it again would spend on a piece already
        // being co-released (the presenter treats it as a neutral no-op).
        public bool HasPendingRelease
        {
            get
            {
                foreach (var tapped in _groupTapped)
                    if (tapped.Count > 0)
                        return true;
                return false;
            }
        }

        private void OnLevelLoaded()
        {
            _level = _model.CurrentLevel;
            _levelGeneration++; // see field doc: lets Tick detect a reload that happened under its own iteration

            var validation = _validator.Validate(_level);
            if (!validation.Ok)
            {
                // Structural brak: refuse to run rather than present an unwinnable level.
                _model.Resolve(GameOutcome.Lost);
                return;
            }

            ClearRuntime();
            BuildCoReleaseGroups();
            _simulation.Reset();
            _simulation.LoadStatics(_level.StaticGeometry, _level.Bounds, _level.Gravity);
            _simulation.LoadFlowZones(_level.AirZones); // TASK-097: null ⇒ no flow (73 existing levels unchanged)

            _model.SetPhase(GamePhase.Running);

            // Pre-place every piece Idle (floating, non-colliding) at its authored start pose (RFC §3/§5.3).
            // A preplaced piece (TASK-031b) instead spawns Dynamic on the field right now — it is a lever/obstacle
            // in the physics from t=0, never a tap target.
            foreach (var piece in _model.Pieces)
            {
                if (_level.Pieces[piece.PlacementIndex].Preplaced)
                    SpawnPreplaced(piece);
                else
                    SpawnIdle(piece);
            }
        }

        public void OnIntent(InputIntent intent)
        {
            if (_model.CurrentPhase != GamePhase.Running)
                return;

            var hit = PickIdlePieceAt(new Vec2(intent.X, intent.Y));
            if (hit == null)
                return;

            // TASK-374: a piece NOT in a co-release group releases NOW, in this same step, in EVERY mode (assembly,
            // balance, catapult) — player input is never serialized (TASK-069), and the level stays winnable by
            // AUTHORING. This is byte-for-byte the pre-TASK-061 immediate path for every non-grouped piece.
            if (!_groupOfPlacement.TryGetValue(hit.PlacementIndex, out var groupId))
            {
                ReleasePiece(hit);
                return;
            }

            // A co-release group member: a single tap does NOTHING visible (no drop, no "armed" state). The piece
            // stays Idle (PickIdlePieceAt keeps seeing it while the window is open) and its placement index joins the
            // group's tapped set. The FIRST tap opens the silent coincidence window; when EVERY member is tapped
            // inside it the whole group releases synchronously (FlushGroup). A re-tap on an already-tapped member is
            // ignored. An incomplete group goes silent when the window expires (Tick) — nothing falls.
            var tapped = _groupTapped[groupId];
            if (!tapped.Add(hit.PlacementIndex))
                return; // re-tap on a member already in the window — ignore, not progress

            if (_groupWindowStepsLeft[groupId] <= 0)
                _groupWindowStepsLeft[groupId] = _tuning.CoReleaseWindowSteps; // first tap opens the window

            if (tapped.Count >= _level.CoReleaseGroups[groupId].Count)
                FlushGroup(groupId); // every member tapped inside the window → release the whole group in one step
        }

        /// <summary>Materialise one Idle piece into free fall (immediate-tap path and every co-release group flush).</summary>
        private void ReleasePiece(PieceInstance piece)
        {
            _simulation.Release(_bodyByInstance[piece.InstanceId]);
            piece.State = PieceLifecycle.Falling;
            piece.RestTimer = 0f;
            _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Falling);
            _model.RegisterAttempt(piece.InstanceId);
        }

        /// <summary>
        /// Release a whole completed co-release group in one shot (TASK-374): every member becomes physical NOW, so
        /// the next <c>_simulation.Step</c> integrates them together — a symmetric pair falls synchronously and the
        /// free beam stays level. Order within the flush is irrelevant (no physics step runs between the Release
        /// calls), which keeps the release deterministic — the same invariant the old TASK-023 flush relied on. The
        /// group's tapped set and window are cleared afterwards.
        /// </summary>
        private void FlushGroup(int groupId)
        {
            foreach (var placementIndex in _level.CoReleaseGroups[groupId])
            {
                var piece = FindPieceByPlacement(placementIndex);
                if (piece == null || piece.State != PieceLifecycle.Idle)
                    continue;
                ReleasePiece(piece);
            }
            _groupTapped[groupId].Clear();
            _groupWindowStepsLeft[groupId] = 0;
        }

        /// <summary>
        /// Build the per-level co-release group tables from <see cref="LevelData.CoReleaseGroups"/> (TASK-374). Maps
        /// each member's placement index → its group id and allocates the transient per-group tapped-set + window
        /// counter. No groups (null) → the tables stay empty → every tap releases immediately. Called after
        /// <see cref="ClearRuntime"/> (which empties the tables) in <see cref="OnLevelLoaded"/>; the validator has
        /// already proven the groups are structurally sound (≥2 members, in range, tapable, one group per piece).
        /// </summary>
        private void BuildCoReleaseGroups()
        {
            var groups = _level.CoReleaseGroups;
            if (groups == null)
                return;

            for (var g = 0; g < groups.Count; g++)
            {
                _groupTapped.Add(new HashSet<int>());
                _groupWindowStepsLeft.Add(0);
                foreach (var placementIndex in groups[g])
                    _groupOfPlacement[placementIndex] = g;
            }
        }

        private PieceInstance FindPiece(int instanceId)
        {
            foreach (var piece in _model.Pieces)
                if (piece.InstanceId == instanceId)
                    return piece;
            return null;
        }

        private PieceInstance FindPieceByPlacement(int placementIndex)
        {
            foreach (var piece in _model.Pieces)
                if (piece.PlacementIndex == placementIndex)
                    return piece;
            return null;
        }

        /// <summary>TASK-054: the single source of truth for "is this piece a tool that may be lost freely?".
        /// Read from <see cref="LevelData"/> via PlacementIndex — exactly as OnLevelLoaded reads Preplaced — so
        /// PieceInstance stays a clean runtime model (no new save field). Absent flag → false → essential.</summary>
        private bool IsExpendable(PieceInstance piece) => _level.Pieces[piece.PlacementIndex].Expendable;

        public void Tick(float dt)
        {
            if (_model.CurrentPhase != GamePhase.Running)
                return;

            // Co-release windows (TASK-374): count each open window down and, when it expires with an INCOMPLETE
            // group, silently reset it — nothing falls, nothing is shown (no armed state, no single-piece drop). A
            // completed group never reaches expiry: it flushes synchronously the moment its last member is tapped
            // (OnIntent). Only groups that declared members open windows, so this loop is empty on grouped-less levels.
            for (var g = 0; g < _groupWindowStepsLeft.Count; g++)
            {
                if (_groupWindowStepsLeft[g] <= 0)
                    continue;
                _groupWindowStepsLeft[g]--;
                if (_groupWindowStepsLeft[g] == 0)
                    _groupTapped[g].Clear(); // window expired incomplete → silent reset, no release
            }

            var anyFalling = false;

            // TASK-069 crash fix: iterate _model.Pieces by INDEX, not with foreach. A per-piece Resolve below
            // (LosePastBounds) can, ON DEVICE, synchronously reload the level (CampaignProgressionController →
            // ReloadLevel → GameModel.LoadLevel _pieces.Clear()s+re-adds) — structurally mutating this very list
            // mid-iteration. A foreach enumerator would throw "Collection was modified" on the next MoveNext; a
            // List indexer does not version-check, so the for is safe AND we bail before indexing anything. The
            // old phase-only guard could not protect this: MoveNext ran before it, and the reload restores the
            // Running phase — so the reliable "did a reload tear us down?" signal is the level-generation bump,
            // not the phase. (The sim never reloads → never trips this; see the _levelGeneration field doc.)
            var generation = _levelGeneration;
            var pieces = _model.Pieces;
            for (var i = 0; i < pieces.Count; i++)
            {
                // Losing a piece past the bounds resolves an ESSENTIAL one Lost immediately (TASK-054 §3) —
                // both for a Falling piece (inside UpdateFalling) and a Placed one (the check below, TASK-059).
                // Once that fires the level is over (sim: phase leaves Running; device: the whole level was torn
                // down and reloaded → generation bumped), so the remaining iterations must NOT touch the model:
                // bail out before indexing a rebuilt list, before MirrorPose can drag a resolved/replaced model,
                // and before a second simultaneous loss double-Resolves in the same tick.
                if (_levelGeneration != generation || _model.CurrentPhase != GamePhase.Running)
                    return;

                var piece = pieces[i];

                if (!_bodyByInstance.TryGetValue(piece.InstanceId, out var bodyId))
                    continue;

                if (piece.State == PieceLifecycle.Falling)
                {
                    UpdateFalling(piece, bodyId, dt);
                    if (piece.State == PieceLifecycle.Falling)
                        anyFalling = true;
                }
                else if (piece.State == PieceLifecycle.Placed && !_simulation.IsWithinBounds(bodyId))
                {
                    // TASK-059: a Placed piece is only ever bounds-checked HERE. Since TASK-305 a Placed piece stays
                    // Dynamic in EVERY mode, so a later drop (or an unstable stack) can knock it off the field;
                    // without this it keeps falling forever, GlobalRestReached never fires, and the level hangs on
                    // Timeout. Same verdict as a Falling piece (symmetry): essential → immediate Lost; expendable →
                    // silent leave. LosePastBounds despawns the body, so skip MirrorPose this iteration.
                    LosePastBounds(piece, bodyId);
                    continue;
                }

                // UpdateFalling above may have Lost this piece and (on device) reloaded the whole level under us.
                // Re-check the generation BEFORE MirrorPose so we never mirror onto a piece the reload already
                // replaced (a same-level LOSS→retry reuses InstanceIds, so the body lookup would spuriously hit).
                if (_levelGeneration != generation)
                    return;

                // The body may have been despawned (Lost) inside UpdateFalling / the Placed check above.
                if (_bodyByInstance.TryGetValue(piece.InstanceId, out bodyId))
                    MirrorPose(piece, bodyId);
            }

            // Defer the verdict while any piece is still falling.
            if (anyFalling)
            {
                _model.SetFieldAtRest(false);
                return;
            }

            // TASK-305: EVERY mode now keeps its placed pieces Dynamic (no freeze), so "nothing Falling" is NOT yet
            // rest — a placed piece can still be settling or be shoved by a later drop. Hold the verdict until the
            // whole field has been quiet for RestDurationSeconds, then judge the settled poses. _lastGlobalRestReached
            // is cached here for ReadyForNextRelease (the sim's next-group gate). A metastable figure that never
            // reaches global rest times out — the honest signal that it was not self-supporting (ADR-0005).
            _lastGlobalRestReached = GlobalRestReached(dt);
            if (!_lastGlobalRestReached)
            {
                _model.SetFieldAtRest(false);
                return;
            }

            ResolveAtRest();

            // TASK-524: publish "at rest, waiting for the next tap" only AFTER the verdict, and only if play goes on —
            // a Lost verdict reloads the level synchronously on device (generation bump), and a stale "true" must not
            // leak into the fresh level. Observe-only for the tutorial; the sim reads ReadyForNextRelease, not this.
            if (_levelGeneration == generation && _model.CurrentPhase == GamePhase.Running)
                _model.SetFieldAtRest(true);
        }

        /// <summary>
        /// Field-readiness gate for the sim driver's next release group (TASK-023 §5.2) — encapsulates every mode
        /// so the sim never re-derives it. Never ready while anything is Falling. Since TASK-305 (freeze removal)
        /// ALL modes additionally wait until the whole field has reached global rest — a placed piece now stays
        /// Dynamic, so the next drop must land on a field that has actually settled, not on one still micro-drifting
        /// (else the poseHash diverges). TASK-374: additionally waits until no co-release window is still open
        /// (a group mid-gesture) — the sim never proposes the next group while one is collecting. In the sim a group
        /// is emitted in ONE step, so its window opens and closes (flush) in that same step and never blocks here.
        /// </summary>
        public bool ReadyForNextRelease
        {
            get
            {
                foreach (var piece in _model.Pieces)
                    if (piece.State == PieceLifecycle.Falling)
                        return false;

                foreach (var stepsLeft in _groupWindowStepsLeft)
                    if (stepsLeft > 0)
                        return false; // a co-release group is still collecting its members
                return _lastGlobalRestReached; // the whole field has come to global rest
            }
        }

        /// <summary>
        /// True once EVERY live body has stayed below the rest speed for RestDurationSeconds (ALL modes since
        /// TASK-305). A placed-but-Dynamic piece keeps moving after it leaves the Falling state — a load riding a
        /// beam, or an assembly piece still nudged by a later drop; judging the figure before it settles would read
        /// a mid-motion pose (and a divergent poseHash). Any body still moving resets the timer, so the gate
        /// reflects a genuine single global rest.
        /// </summary>
        private bool GlobalRestReached(float dt)
        {
            foreach (var bodyId in _bodyByInstance.Values)
            {
                if (_simulation.GetSpeed(bodyId) >= _tuning.RestSpeedEpsilon)
                {
                    _globalRestTimer = 0f;
                    return false;
                }
            }

            _globalRestTimer += dt;
            return _globalRestTimer >= _tuning.RestDurationSeconds;
        }

        /// <summary>
        /// A piece has left the field past the bounds — the loss verdict shared by the Falling path (TASK-054 §3)
        /// and the Placed path (TASK-059). Transition to Lost, drop the view/body, and for an ESSENTIAL piece
        /// (part of the target figure) fail the level NOW: the loss is terminal, no later event can bring it back,
        /// so waiting for global rest only risks a hang. An EXPENDABLE piece (a tool) leaves silently and the
        /// verdict is still decided at rest. The caller's Tick loop early-exits once the phase leaves Running, so a
        /// second simultaneous loss in the same tick never double-Resolves. The slot the piece filled un-fills by
        /// itself: CollectPlaced re-reads only live Placed pieces, so a despawned one simply drops out of the match.
        /// </summary>
        private void LosePastBounds(PieceInstance piece, int bodyId)
        {
            piece.State = PieceLifecycle.Lost;
            _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Lost);
            _simulation.Despawn(bodyId);
            _bodyByInstance.Remove(piece.InstanceId);
            if (!IsExpendable(piece))
                _model.Resolve(GameOutcome.Lost);
        }

        private void UpdateFalling(PieceInstance piece, int bodyId, float dt)
        {
            if (!_simulation.IsWithinBounds(bodyId))
            {
                LosePastBounds(piece, bodyId);
                return;
            }

            // TASK-043: no in-fall snap. A released piece falls under honest physics and is placed the moment it
            // has stayed below the rest speed for RestDurationSeconds — frozen EXACTLY where physics left it (the
            // whole visible turn is falling-hitting-rotating-sliding; posTol/angTol only JUDGE the result).
            if (_simulation.GetSpeed(bodyId) < _tuning.RestSpeedEpsilon)
            {
                piece.RestTimer += dt;
                if (piece.RestTimer >= _tuning.RestDurationSeconds)
                    Place(piece, bodyId);
            }
            else
            {
                piece.RestTimer = 0f;
            }
        }

        /// <summary>
        /// Mark a rested piece Placed at its physical rest pose — TASK-305 (freeze removal, variant A): UNIFORM across
        /// every mode. The body stays DYNAMIC (nothing is ever locked kinematic), so it keeps living in the physics:
        /// a later drop can still shove it, MirrorPose tracks its live pose every tick, and the verdict is judged only
        /// at GLOBAL rest of the whole field (Tick). A piece must reach the silhouette by real physics (falling,
        /// hitting, rotating, launching); posTol/angTol + the rigidity spread in PoseMatchEvaluator only JUDGE the
        /// settled result. On a seesaw a single load honestly tips the free beam (Lost) while a synchronous symmetric
        /// pair keeps it level; on a catapult the free arm rests on a LOW fulcrum limited by the natural ground-stop.
        /// Determinism now holds ONLY for self-supporting figures — a cantilever/overhang/top-heavy figure is
        /// metastable and surfaces as a sweep victim (ADR-0005).
        /// </summary>
        private void Place(PieceInstance piece, int bodyId)
        {
            var pose = _simulation.GetPose(bodyId);
            MarkPlaced(piece, pose.Position, pose.Rotation);
        }

        /// <summary>Mark a piece Placed at an exact pose and mirror it into the model + view. Does NOT touch the
        /// simulation body — the body stays Dynamic (TASK-305); this is a model/view state change only.</summary>
        private void MarkPlaced(PieceInstance piece, Vec2 pos, float rot)
        {
            piece.State = PieceLifecycle.Placed;
            piece.Position = pos;
            piece.Rotation = rot;
            _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Placed);
            _viewByInstance[piece.InstanceId].Sync(pos, rot);
        }

        private void MirrorPose(PieceInstance piece, int bodyId)
        {
            var pose = _simulation.GetPose(bodyId);
            piece.Position = pose.Position;
            piece.Rotation = pose.Rotation;
            _viewByInstance[piece.InstanceId].Sync(pose.Position, pose.Rotation);
        }

        /// <summary>
        /// Verdict at global rest (RFC §4.3). Won when every slot is filled by a pose-matched piece and none
        /// is Lost. If no piece is Idle or Falling (nothing left to change) and it is not Won → Lost. Otherwise
        /// Idle pieces remain → wait for the player's next tap (no instant fail; canon §3 judges at final rest).
        /// </summary>
        private void ResolveAtRest()
        {
            // TASK-054: only an ESSENTIAL Lost piece blocks the win (an expendable tool may leave the field). After
            // §3 an essential piece never survives Lost to this point — it already resolved the level in UpdateFalling
            // — so anyEssentialLost is defensively false here; the branch keeps the win-gate self-consistent and
            // survives a hypothetical move back to a rest-time verdict. Expendable Lost pieces no longer veto the win.
            var anyEssentialLost = false;
            var anyIdle = false;
            foreach (var piece in _model.Pieces)
            {
                if (piece.State == PieceLifecycle.Lost) { if (!IsExpendable(piece)) anyEssentialLost = true; }
                else if (piece.State == PieceLifecycle.Idle) anyIdle = true;
            }

            // TASK-305 (freeze removal): nothing is frozen here anymore. Reached only at GLOBAL rest (the Tick gate),
            // so every Dynamic piece has settled; greedy-match the live rest poses directly. If pieces are still Idle
            // the player has taps left — the win check below still runs (a partial field simply won't fill every
            // slot), and the `anyIdle` guard after it waits for the next tap rather than declaring Lost.
            var result = _match.Evaluate(_level.Target, CollectPlaced(), _level.MatchTolerance, _pairScratch);

            if (result.AllSlotsFilled && !anyEssentialLost)
            {
                // TASK-634: publish the won figure BEFORE Resolve — the win flow reads it synchronously on Outcome.
                _model.SetWinFigure(BuildWinFigure());
                _model.Resolve(GameOutcome.Won);
                return;
            }

            if (anyIdle)
                return; // pieces remain on the field — wait for the player's tap.

            // Nothing Idle or Falling and the figure is not complete → the level cannot progress further.
            _model.Resolve(GameOutcome.Lost);
        }

        private IReadOnlyList<PlacedPiece> CollectPlaced()
        {
            _placedScratch.Clear();
            _placedInstanceScratch.Clear();
            foreach (var piece in _model.Pieces)
            {
                if (piece.State != PieceLifecycle.Placed)
                    continue;
                var type = _typeByInstance[piece.InstanceId];
                _placedScratch.Add(new PlacedPiece(piece.Kind, piece.Position, piece.Rotation, type.SymmetryOrder));
                _placedInstanceScratch.Add(piece);
            }
            return _placedScratch;
        }

        /// <summary>The won figure from the judge's own pairs (TASK-634): one entry per filled slot, landing on the slot
        /// angle nearest the rest pose under the piece's symmetry. A fresh list — one allocation per win.</summary>
        private List<WinFigurePiece> BuildWinFigure()
        {
            var figure = new List<WinFigurePiece>(_pairScratch.Count);
            foreach (var pair in _pairScratch)
            {
                var placed = _placedScratch[pair.PlacedIndex];
                var instance = _placedInstanceScratch[pair.PlacedIndex];
                var slot = _level.Target.Slots[pair.SlotIndex];
                var land = placed.Rotation
                           - PoseMatchEvaluator.SignedSymmetryDelta(placed.Rotation - slot.Rotation, placed.SymmetryOrder);
                figure.Add(new WinFigurePiece(instance.PlacementIndex, pair.SlotIndex, placed.Position, placed.Rotation, land));
            }
            return figure;
        }

        /// <summary>Resolve a tap to an Idle piece via the shared two-tier <see cref="PieceHitResolver"/> (TASK-085):
        /// containment first — the piece drawn on top at that point (TASK-645) — then, only with a live tap halo (r&gt;0),
        /// the nearest Idle piece whose boundary is within the halo. The sim's <c>NullTapTolerance</c> (r=0) never
        /// enters the halo tier. TASK-649: every live ACTIVE piece (Falling, Placed, preplaced — a body on the field) is a
        /// candidate too: drawn above every Idle one, it catches a tap at the point where it lies, and nothing is
        /// released — a fast second tap no longer drops the Idle piece hanging under a falling one. Its pose is the
        /// last mirrored one (<c>MirrorPose</c> each Tick) — the pose the view shows.</summary>
        private PieceInstance PickIdlePieceAt(Vec2 p)
        {
            _hitCandidates.Clear();
            foreach (var piece in _model.Pieces)
            {
                if (!_bodyByInstance.ContainsKey(piece.InstanceId))
                    continue; // never spawned, or Lost (despawned) — nothing on the field to see

                var type = _typeByInstance[piece.InstanceId];
                var world = Geometry.WorldVertices(type.Shape, piece.Position, piece.Rotation);
                _hitCandidates.Add(new PieceHitResolver.HitCandidate(piece.InstanceId, piece.State,
                    piece.PlacementIndex, world, piece.Position));
            }

            var instanceId = PieceHitResolver.Resolve(_hitCandidates, p, _tapTolerance.WorldRadius);
            return instanceId < 0 ? null : FindPiece(instanceId);
        }

        private void SpawnIdle(PieceInstance piece)
        {
            var type = _model.TypeFor(piece);
            var bodyId = _simulation.SpawnFloating(type, piece.Position, piece.Rotation);

            _bodyByInstance[piece.InstanceId] = bodyId;
            _typeByInstance[piece.InstanceId] = type;

            var view = _viewFactory.Create(type, piece.PlacementIndex);
            view.SetState(PieceLifecycle.Idle);
            view.Sync(piece.Position, piece.Rotation);
            _viewByInstance[piece.InstanceId] = view;
        }

        /// <summary>
        /// Spawn a preplaced piece (TASK-031b): identical body/view setup to <see cref="SpawnIdle"/>, but the body
        /// is RELEASED immediately so it drops into the physics at t=0 (Falling), NOT hung Idle. It is therefore
        /// never released by <see cref="PickIdlePieceAt"/> (only Idle pieces are; an active one merely catches a tap where
        /// it lies on top, TASK-649), never awaited as a tap, and — unlike
        /// <see cref="ReleasePiece"/> — does NOT count a player attempt (it is level furniture, not a move). From
        /// here it follows the ordinary fall→rest→Place path; since TASK-305 it lands as a free Dynamic body in every
        /// mode (never frozen) so a later drop can still pivot it.
        /// </summary>
        private void SpawnPreplaced(PieceInstance piece)
        {
            var type = _model.TypeFor(piece);
            var bodyId = _simulation.SpawnFloating(type, piece.Position, piece.Rotation);

            _bodyByInstance[piece.InstanceId] = bodyId;
            _typeByInstance[piece.InstanceId] = type;

            _simulation.Release(bodyId);
            piece.State = PieceLifecycle.Falling;
            piece.RestTimer = 0f;

            var view = _viewFactory.Create(type, piece.PlacementIndex);
            view.SetState(PieceLifecycle.Falling);
            view.Sync(piece.Position, piece.Rotation);
            _viewByInstance[piece.InstanceId] = view;
        }

        private void ClearRuntime()
        {
            foreach (var view in _viewByInstance.Values)
                view.Dispose();
            _bodyByInstance.Clear();
            _viewByInstance.Clear();
            _typeByInstance.Clear();
            _globalRestTimer = 0f;
            _groupOfPlacement.Clear();
            _groupTapped.Clear();
            _groupWindowStepsLeft.Clear();
            _lastGlobalRestReached = false;
        }

        public void Dispose()
        {
            _levelLoadedSub?.Dispose();
            _levelLoadedSub = null;
        }
    }
}
