using System;
using System.Collections.Generic;
using Gameplay.Core.Adapters;
using R3;

namespace Gameplay.Core.Mechanic
{
    /// <summary>
    /// Fills the <c>IMechanicController</c> seam with the pivoted shape_fill loop (GAME_RULES §2–3), all pure
    /// C# (headless-testable via ScriptedPoseSimulation): all pieces are pre-placed Idle over their target
    /// slots; a tap hit-tests which Idle piece it hit and releases it; releasing materialises the body so it
    /// falls; poses are mirrored into PieceInstances; the Idle→Falling→Placed/Lost FSM runs; and on global
    /// rest a per-piece pose-match decides Won/Lost. Physics itself lives behind IShapeFillSimulation.
    /// </summary>
    public sealed class ShapeFillMechanicController : IMechanicController, IDisposable
    {
        private readonly GameModel _model;
        private readonly IShapeFillSimulation _simulation;
        private readonly IMatchEvaluator _match;
        private readonly LevelDataValidator _validator;
        private readonly IPieceViewFactory _viewFactory;
        private readonly ShapeFillTuning _tuning;

        private readonly Dictionary<int, int> _bodyByInstance = new();
        private readonly Dictionary<int, IPieceView> _viewByInstance = new();
        private readonly Dictionary<int, PieceType> _typeByInstance = new();
        private readonly List<PlacedPiece> _placedScratch = new();
        // Slots already claimed by a Placed piece this session — one slot, one piece (mirrors the evaluator's
        // greedy assignment) so the snap assist never sends two pieces to the same slot (RFC 07 §2.2).
        private readonly HashSet<int> _occupiedSlots = new();
        // TASK-042 (gate-FAIL revision): the slot each Placed piece has claimed (InstanceId → slot index). Lets the
        // honesty gate measure a candidate pose's overlap against the AUTHORED baseline of the specific pair of
        // slots (below) rather than an absolute tolerance, and lets the sim detector agree with the gate.
        private readonly Dictionary<int, int> _slotByInstance = new();
        // TASK-042: authored baseline overlap map — SAT penetration between every pair of target slots with each
        // Kind's shape placed at its exact slot pose, baked once at load. _slotBaseline[i,j] is how much slots i,j
        // overlap BY DESIGN (0 for disjoint slots; >0 for authored micro-overlaps like owl's ear in the head). A
        // snap/rest pose is admissible only when its real penetration stays within this baseline + OverlapTolerance,
        // so the canonical silhouette passes by construction on every level. Null until the first level loads.
        private float[,] _slotBaseline;
        // TASK-042 diagnosability (owner: "добавь reason-счётчики в сим, не гадай"): how many snap candidates the
        // honesty gate rejected this run, split by cause. Non-zero on a canonical run pinpoints which clamp fired
        // (a design overlap the baseline under-counts, vs. a slot the bot could not support). Read by the sim.
        private int _snapRejectsByOverlap;
        private int _snapRejectsBySupport;
        // TASK-042 gate-v3 (owner 2026-07-26 20:55, pt.1): how many freeze-at-rest attempts the Place support-guard
        // rejected this run — the freeze-path counterpart of _snapRejectsBySupport (which covers the in-fall/settle
        // path). Together they prove BOTH assembly paths into Placed are support-gated. Non-zero in a wrong order is
        // the owner's requested proof that the freeze path is covered; 0 in canon (a canon piece always rests on a
        // static/Placed piece by the time it stops). Reset per level in ClearRuntime.
        private int _freezeRejectsBySupport;
        // Pieces gliding from their capture pose onto the slot (soft-settle, game-balancer 13 + human 11:36),
        // keyed by InstanceId. While this is non-empty the rest verdict is deferred so the figure is never
        // judged mid-glide (§6.4).
        private readonly Dictionary<int, Settle> _settling = new();
        private readonly List<int> _settleDone = new();
        // Pieces whose authored start tilt is large enough to be righted by REAL contact torque (TASK-024): they
        // SKIP the in-fall first-contact snap, stay Dynamic while gravity rotates them to rest, and — since TASK-030
        // variant B — are FROZEN EXACTLY AT their physical rest pose (Place → freeze-at-rest, no glide, no angle
        // correction). The whole visible turn is physics; the piece must land in the silhouette by falling, hitting,
        // rotating and sliding, and posTol/angTol only JUDGE the result (owner: remove the "пододвигание" nudge). We
        // still claim the nearest same-Kind slot for the win check + slot monopoly. Flat pieces (start ≈ slot angle)
        // are absent here and keep the byte-for-byte in-fall path. Transient: recomputed deterministically from the
        // level data on every spawn/restore, never persisted. Keyed by InstanceId. Scoped to tilted pieces only.
        private readonly HashSet<int> _needsPhysicalSettle = new();

        private IDisposable _levelLoadedSub;
        private IDisposable _stateRestoredSub;
        private LevelData _level;
        // Balance physics mode (TASK-021 → free-beam TASK-023): the beam is a FREE Dynamic body resting on the
        // fulcrum (no world hinge/pin/limits), loads are released as synchronous groups (coalescence window), the
        // in-fall snap assist is off, and the verdict waits for GLOBAL rest so a still-swinging beam is never
        // judged. Assembly mode (66 levels) is byte-for-byte the pre-TASK-021 behaviour.
        private bool _balanceMode;
        // Catapult sub-mode of balance (TASK-025 → de-pinned TASK-027): _balanceMode is ALSO true here, so the whole
        // free-body machinery (no in-fall snap, PlaceDynamic for arm+loads, global-rest verdict, BalanceSnapLoads)
        // runs unchanged. After TASK-027 the pivot arm is NO LONGER world-pinned — the owner playtest rejected the
        // pin as a "helper": the arm now rests as a FREE Dynamic body on a LOW fulcrum, its tilt limited by the
        // NATURAL ground-stop (a small cube tilts it until the far end touches the ground; a boulder then flips it
        // around the ridge and launches the pebble). So _catapultMode now adds only ONE delta: coalescence is
        // suppressed so the charge→overload chain is released as sequential SINGLE taps (the seesaw pair path
        // _balanceMode && !_catapultMode is intact).
        private bool _catapultMode;
        // Time the whole field has stayed below the rest speed, in balance mode only — the verdict fires only
        // once it exceeds RestDurationSeconds (the beam and everything on it have stopped swinging).
        private float _globalRestTimer;

        // ── Synchronous group release (TASK-023, balance-only). Taps on separate Idle pieces are buffered into a
        //    coalescence group and released in ONE flush step so a symmetric pair enters the physics together and
        //    the free beam stays level. Transient runtime state — NOT persisted. Assembly mode never touches it. ──
        // InstanceIds tapped-and-armed, waiting for the flush.
        private readonly List<int> _pendingRelease = new();
        // >0 ⇒ the coalescence window is open (counts down in Tick).
        private int _coalesceWindowStepsLeft;
        // Last computed global-rest state, exposed via ReadyForNextRelease so the sim driver releases the next
        // group only once the beam + loads have settled (balance) — recomputed each tick the field is not falling.
        private bool _lastGlobalRestReached;

        /// <summary>In-flight soft-settle glide for one piece: driven kinematically toward the slot over
        /// TotalSteps fixed steps. Position uses a critically-damped approach SEEDED with the piece's capture
        /// velocity (VelX/VelY) so the settle continues the fall's momentum without a pop and never overshoots
        /// (human 12:02); rotation is an eased turn from StartRot to the slot's exact target angle TargetRot. For an
        /// in-fall capture the piece visibly rotates its full landing tilt into the silhouette (human 2026-07-24).
        /// Since TASK-030 variant B this glide is used ONLY by the assembly in-fall snap (flat pieces) and the
        /// balance crisp-at-rest (BalanceSnapLoads) — tilted physical-settle pieces are frozen at rest with NO glide.</summary>
        private sealed class Settle
        {
            public int BodyId;
            public Vec2 Pos;   // current glide position (critically-damped state)
            public float VelX; // preserved linear velocity (critically-damped state), seeded at capture
            public float VelY;
            public float StartRot;
            public Vec2 TargetPos;
            public float TargetRot;
            public int Step;
            public int TotalSteps;
        }

        public ShapeFillMechanicController(
            GameModel model,
            IShapeFillSimulation simulation,
            IMatchEvaluator match,
            LevelDataValidator validator,
            IPieceViewFactory viewFactory,
            ShapeFillTuning tuning)
        {
            _model = model;
            _simulation = simulation;
            _match = match;
            _validator = validator;
            _viewFactory = viewFactory;
            _tuning = tuning;

            _levelLoadedSub = _model.LevelLoaded.Subscribe(_ => OnLevelLoaded());
            _stateRestoredSub = _model.StateRestored.Subscribe(_ => OnStateRestored());
        }

        private void OnLevelLoaded()
        {
            _level = _model.CurrentLevel;
            _balanceMode = _level.PhysicsMode is PhysicsMode.Balance or PhysicsMode.Catapult;
            _catapultMode = _level.PhysicsMode == PhysicsMode.Catapult;

            var validation = _validator.Validate(_level);
            if (!validation.Ok)
            {
                // Structural brak: refuse to run rather than present an unwinnable level.
                _model.Resolve(GameOutcome.Lost);
                return;
            }

            ClearRuntime();
            _simulation.Reset();
            _simulation.LoadStatics(_level.StaticGeometry, _level.Bounds, _level.Gravity);

            _model.SetPhase(GamePhase.Running);

            BuildSlotBaseline();

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

        /// <summary>
        /// Rebuild runtime state from a restored snapshot (RFC §8). Runs AFTER OnLevelLoaded, so it tears down
        /// the fresh spawn and re-materialises bodies/views from each piece's saved state: Idle → floating
        /// (non-colliding), Placed → settled (kinematic, supports others), Lost → skipped.
        /// </summary>
        private void OnStateRestored()
        {
            _level = _model.CurrentLevel;
            _balanceMode = _level.PhysicsMode is PhysicsMode.Balance or PhysicsMode.Catapult;
            _catapultMode = _level.PhysicsMode == PhysicsMode.Catapult;

            ClearRuntime();
            _simulation.Reset();
            _simulation.LoadStatics(_level.StaticGeometry, _level.Bounds, _level.Gravity);

            BuildSlotBaseline();

            foreach (var piece in _model.Pieces)
            {
                if (piece.State == PieceLifecycle.Lost)
                    continue;

                // A preplaced piece (TASK-031b) that has not yet settled must re-enter Dynamic (falling), NOT
                // floating-Idle — otherwise the restore would make the level lever tap-releasable. (Campaign flow
                // only snapshots at rest, so in prod a preplaced piece restores as Placed and takes the branch
                // below; this keeps the QA save/load round-trip correct.)
                if (piece.State != PieceLifecycle.Placed && _level.Pieces[piece.PlacementIndex].Preplaced)
                {
                    SpawnPreplaced(piece);
                    continue;
                }

                var type = _model.TypeFor(piece);
                var bodyId = piece.State == PieceLifecycle.Placed
                    ? _simulation.SpawnSettled(type, piece.Position, piece.Rotation)
                    : _simulation.SpawnFloating(type, piece.Position, piece.Rotation);

                _bodyByInstance[piece.InstanceId] = bodyId;
                _typeByInstance[piece.InstanceId] = type;
                // Placed pieces have already come to rest (settled kinematic) — they never re-enter the fall path,
                // so only non-Placed pieces need the TASK-024 physical-settle classification recomputed.
                if (piece.State != PieceLifecycle.Placed)
                    RegisterPhysicalSettle(piece, type);
                else
                    // TASK-042: a restored Placed piece never re-runs the snap that would claim its slot, so
                    // reconstruct the claim here (nearest same-Kind slot) — otherwise the honesty gate would treat
                    // it as slot-less and measure a later snap against a 0 baseline, mis-rejecting a legit design
                    // overlap. Mirrors the one-slot-one-piece monopoly the live path maintains via ClaimSlot.
                    ClaimNearestSlot(piece);

                var view = _viewFactory.Create(type);
                view.SetState(piece.State);
                view.Sync(piece.Position, piece.Rotation);
                _viewByInstance[piece.InstanceId] = view;
            }
        }

        public void OnIntent(InputIntent intent)
        {
            if (_model.CurrentPhase != GamePhase.Running)
                return;

            var hit = PickIdlePieceAt(new Vec2(intent.X, intent.Y));
            if (hit == null)
                return;

            if (!_balanceMode)
            {
                // Assembly (66 levels): one tap = one immediate release — byte-for-byte the pre-TASK-023 path.
                ReleasePiece(hit);
                return;
            }

            if (_catapultMode)
            {
                // Catapult (TASK-025): the solution is a SEQUENTIAL single-tap chain (fulcrum → arm → pebble charges
                // the far end → boulder overloads the near end and flips the arm). Coalescing a fast pebble+boulder
                // double-tap into one synchronous step would launch them together and break the charge, so release
                // immediately (like assembly). The seesaw pair path below (_balanceMode && !_catapultMode) is intact.
                ReleasePiece(hit);
                return;
            }

            // Balance (TASK-023): buffer the tap into the coalescence group instead of releasing now. The piece
            // stays Idle in the model until the flush (PickIdlePieceAt must keep seeing it while the window is
            // open), and the whole group releases synchronously in FlushPendingRelease so a symmetric pair enters
            // the physics in the same step (net torque ≈ 0 → the free beam holds level).
            if (_pendingRelease.Contains(hit.InstanceId))
                return; // re-tap on an already-armed piece — ignore, not a duplicate

            _pendingRelease.Add(hit.InstanceId);
            if (_coalesceWindowStepsLeft <= 0)
                _coalesceWindowStepsLeft = _tuning.CoalesceWindowSteps; // first tap opens the window
            _viewByInstance[hit.InstanceId].SetArmed(true);

            if (_pendingRelease.Count >= _tuning.MaxCoalesceGroup)
                FlushPendingRelease(); // group full → release now (minimal latency)
        }

        /// <summary>Materialise one Idle piece into free fall (assembly path and every coalescence flush).</summary>
        private void ReleasePiece(PieceInstance piece)
        {
            _simulation.Release(_bodyByInstance[piece.InstanceId]);
            piece.State = PieceLifecycle.Falling;
            piece.RestTimer = 0f;
            _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Falling);
            _model.RegisterAttempt();
        }

        /// <summary>
        /// Release the whole armed coalescence group in one shot (TASK-023 §2.2): every buffered piece becomes
        /// physical NOW, so the next <c>_simulation.Step</c> integrates them together — a symmetric pair falls
        /// synchronously. Order within the flush is irrelevant (no physics step runs between the Release calls),
        /// which keeps the release deterministic. A group of one (base piece, or a lone tap whose window expired)
        /// releases a single piece → on a seesaw that honestly tips the beam → Lost (the winnability gate).
        /// </summary>
        private void FlushPendingRelease()
        {
            foreach (var instanceId in _pendingRelease)
            {
                var piece = FindPiece(instanceId);
                if (piece == null || piece.State != PieceLifecycle.Idle)
                    continue;
                ReleasePiece(piece);
                _viewByInstance[instanceId].SetArmed(false);
            }
            _pendingRelease.Clear();
            _coalesceWindowStepsLeft = 0;
        }

        private PieceInstance FindPiece(int instanceId)
        {
            foreach (var piece in _model.Pieces)
                if (piece.InstanceId == instanceId)
                    return piece;
            return null;
        }

        public void Tick(float dt)
        {
            if (_model.CurrentPhase != GamePhase.Running)
                return;

            // Balance coalescence window (TASK-023): count it down and flush the armed group when it expires. A
            // window that expires with a single armed piece still releases it (honest Lost — no artificial block).
            if (_balanceMode && _coalesceWindowStepsLeft > 0)
            {
                _coalesceWindowStepsLeft--;
                if (_coalesceWindowStepsLeft == 0)
                    FlushPendingRelease();
            }

            AdvanceSettling();

            var anyFalling = false;

            foreach (var piece in _model.Pieces)
            {
                if (!_bodyByInstance.TryGetValue(piece.InstanceId, out var bodyId))
                    continue;

                if (piece.State == PieceLifecycle.Falling)
                {
                    UpdateFalling(piece, bodyId, dt);
                    if (piece.State == PieceLifecycle.Falling)
                        anyFalling = true;
                }

                // The body may have been despawned (Lost) inside UpdateFalling. Settling pieces are already
                // Placed, so MirrorPose just tracks the view along the kinematic glide set in AdvanceSettling.
                if (_bodyByInstance.TryGetValue(piece.InstanceId, out bodyId))
                    MirrorPose(piece, bodyId);
            }

            // Defer the verdict while any piece is still falling OR gliding (a piece that BeganSettle this tick
            // is added to _settling, so the same-tick guard also covers freshly-captured pieces).
            if (anyFalling || _settling.Count > 0)
                return;

            // Balance mode: placed pieces stay Dynamic and keep swinging on the beam, so "nothing Falling" is NOT
            // yet rest. Hold the verdict until the whole field has been quiet for RestDurationSeconds, then judge
            // the settled pose (beam within angTol of level ⇒ balanced). _lastGlobalRestReached is cached here for
            // ReadyForNextRelease (the sim's next-group gate). Assembly mode is unaffected.
            if (_balanceMode)
            {
                _lastGlobalRestReached = GlobalRestReached(dt);
                if (!_lastGlobalRestReached)
                    return;
            }

            ResolveAtRest();
        }

        /// <summary>
        /// Field-readiness gate for the sim driver's next release group (TASK-023 §5.2) — encapsulates BOTH modes
        /// so the sim never re-derives it. Never ready while anything is Falling. Assembly ≡ the old "no Falling
        /// piece" quiet check (byte-for-byte). Balance additionally waits until the coalescence group has flushed
        /// (nothing armed, window closed), no load is mid-settle, and the whole field has reached global rest — so
        /// the next pair is released only onto a beam that has stopped swinging.
        /// </summary>
        public bool ReadyForNextRelease
        {
            get
            {
                foreach (var piece in _model.Pieces)
                    if (piece.State == PieceLifecycle.Falling)
                        return false;

                if (!_balanceMode)
                    return true; // assembly: identical to the pre-TASK-023 IsFieldQuiet

                if (_pendingRelease.Count > 0 || _coalesceWindowStepsLeft > 0)
                    return false; // a group is still arming
                if (_settling.Count > 0)
                    return false; // loads gliding onto slots — not settled yet
                return _lastGlobalRestReached; // beam + loads have come to global rest
            }
        }

        /// <summary>
        /// True once EVERY live body has stayed below the rest speed for RestDurationSeconds (balance mode only).
        /// A placed-but-Dynamic pivot beam and the loads riding it keep swinging after they leave the Falling
        /// state; judging the figure before they settle would read a mid-crank pose (and a divergent poseHash).
        /// Any body still moving resets the timer, so the gate reflects a genuine single global rest.
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
        /// Advance every in-flight soft-settle glide by one fixed step. Position is driven by a critically-damped
        /// approach toward the slot SEEDED with the piece's capture velocity — it continues the fall's momentum
        /// instead of restarting from a dead stop (human 12:02: no hitch), never overshoots the slot, and leaves
        /// at most a tiny within-tolerance residual (the organic hand-placed feel, game-designer 12 §3.2).
        /// Rotation is the eased turn onto the slot's exact target angle — the full landing tilt rotates into the
        /// silhouette (human 2026-07-24). The step count comes from SettleDurationSeconds so the whole glide is
        /// headless-deterministic (game-balancer 13 §6.1).
        /// </summary>
        private void AdvanceSettling()
        {
            if (_settling.Count == 0)
                return;

            var dt = _tuning.FixedStep;
            // Critically-damped time constant: the spring is tuned to converge well within the settle window
            // (≈2× smoothTime). SettleDurationSeconds stays the balancer's single duration lever.
            var smoothTime = _tuning.SettleDurationSeconds * 0.5f;

            _settleDone.Clear();
            foreach (var kv in _settling)
            {
                var s = kv.Value;
                s.Step++;

                var px = SmoothDamp(s.Pos.X, s.TargetPos.X, ref s.VelX, smoothTime, dt);
                var py = SmoothDamp(s.Pos.Y, s.TargetPos.Y, ref s.VelY, smoothTime, dt);
                s.Pos = new Vec2(px, py);

                var e = Ease(_tuning.SettleEasing, (float)s.Step / s.TotalSteps);
                var rot = s.StartRot + (s.TargetRot - s.StartRot) * e;

                _simulation.FreezeAt(s.BodyId, s.Pos, rot);

                if (s.Step >= s.TotalSteps)
                    _settleDone.Add(kv.Key);
            }

            foreach (var instanceId in _settleDone)
                _settling.Remove(instanceId);
        }

        /// <summary>
        /// Critically-damped smoothing toward <paramref name="target"/> that PRESERVES the incoming velocity
        /// (seeded via <paramref name="velocity"/>) so the settle continues the fall without a pop, and never
        /// overshoots (human 12:02). Same math as UnityEngine.Mathf.SmoothDamp, reimplemented in the Unity-free
        /// domain (no maxSpeed lever); deterministic for a fixed <paramref name="dt"/>.
        /// </summary>
        private static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float dt)
        {
            if (smoothTime < 1e-4f)
                smoothTime = 1e-4f;

            var omega = 2f / smoothTime;
            var x = omega * dt;
            var exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            var change = current - target;
            var original = target;
            var temp = (velocity + omega * change) * dt;
            velocity = (velocity - omega * temp) * exp;
            var output = target + (change + temp) * exp;

            // Never overshoot: a preserved high velocity would otherwise carry the piece past the slot.
            if ((original - current > 0f) == (output > original))
            {
                output = original;
                velocity = (output - original) / dt;
            }

            return output;
        }

        private void UpdateFalling(PieceInstance piece, int bodyId, float dt)
        {
            if (!_simulation.IsWithinBounds(bodyId))
            {
                piece.State = PieceLifecycle.Lost;
                _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Lost);
                _simulation.Despawn(bodyId);
                _bodyByInstance.Remove(piece.InstanceId);
                return;
            }

            // Soft-settle assist: begin the eased glide the moment the still-falling piece enters the capture
            // window of a free slot — so the correction blends into the descent instead of playing as a delayed
            // animation after the piece has already come to rest (human 11:36). No rest dwell is required.
            //
            // Support gate (TASK-010, human 15:20): snap is a SETTLING assist, not an in-air magnet. Capture a
            // slot only once the piece already has support beneath it (the floor or an already-settled body
            // within a small gap under its lower edge — it is about to sit down). While it is in free fall with
            // empty space below, it keeps falling even through a slot's capture window, so a piece bound for a
            // lower slot is never grabbed by a higher one it merely passes through (the L1 two_cubes regress).
            // Physical & level-agnostic — no dependence on neighbouring slots' Kind/position.
            //
            // Balance mode has NO in-fall snap assist (TASK-021 → free-beam TASK-023): the beam tilt IS the
            // mechanic, so a piece must ride the real beam physics onto it rather than being glided into an
            // authored slot pose mid-fall. Loads are crisped onto their slots only at global rest (BalanceSnapLoads).
            //
            // Physical-settle pieces (TASK-024) ALSO skip the in-fall snap: an authored-tilt piece captured on
            // first contact would be frozen mid-tilt and glided kinematically — the player sees kinematics, not
            // physics (owner complaint 2026-07-24). Instead it stays Dynamic so contact gravity honestly rights it,
            // and it is frozen only at rest below (rest-dwell → Place → freeze-at-rest, TASK-030 variant B: NO glide,
            // no angle correction). While it is still rotating, GetSpeed (max(|lin|,|ang|·r)) stays above ε on the
            // angular term, so RestTimer never accumulates and it is not placed prematurely.
            if (!_balanceMode && !NeedsPhysicalSettle(piece) &&
                _simulation.HasSupportBelow(bodyId, _tuning.SnapSupportProbeDistance))
            {
                // enforceHonesty: true — this snap MOVES the piece onto the slot pose, so that pose must be
                // admissible (TASK-042). The claim-only callers pass false: they freeze the piece at its physical
                // rest pose and only record which slot it satisfies for the win check, so gating the slot pose there
                // would wrongly deny a legit claim.
                var slot = FindSnapSlot(piece, bodyId, enforceHonesty: true);
                if (slot >= 0)
                {
                    BeginSettle(piece, bodyId, slot);
                    return;
                }
            }

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
        /// A piece that came to rest without ever entering a slot's capture window gets no assist — freeze it
        /// exactly where it lies. Freezing (kinematic) kills post-rest drift and makes the piece a rigid support
        /// the next drop cannot shove (RFC 07 §2.1). Flat pieces captured in-fall are handled eagerly in
        /// UpdateFalling (the soft-settle glide begins during the fall, human 11:36). Physical-settle pieces
        /// (TASK-024 → TASK-030 variant B) reach here after contact torque has righted them to rest, and are frozen
        /// exactly at that rest pose — no glide, no angle correction (they only claim their slot for the win check).
        /// </summary>
        private void Place(PieceInstance piece, int bodyId)
        {
            var pose = _simulation.GetPose(bodyId);

            if (_balanceMode)
            {
                // Free-beam (TASK-023, extended to catapults TASK-027): BOTH the pivot beam/arm and the loads land as
                // free Dynamic bodies judged from their rest pose — nothing is world-pinned. On a seesaw a single load
                // honestly tips the free beam (Lost) while a synchronous symmetric pair keeps it level; on a catapult
                // the free arm rests on a LOW fulcrum with its tilt limited by the natural ground-stop, and the boulder
                // slam flips it around the ridge to launch the pebble (the pin was removed as an owner-rejected helper,
                // TASK-027). The Pivot flag is retained (it marks "judged from the free rest pose, never snapped").
                PlaceDynamic(piece, pose);
                return;
            }

            // TASK-042 honesty guard: never CEMENT a rest pose that interpenetrates an already-frozen piece beyond
            // the authored design overlap (a tunnelling / momentary-jam artifact — the owner's "взаимопроникновение").
            // Leave the piece Dynamic so contacts push it out; if it can never settle clean the order just times out
            // (an honest un-winnable wrong order), it never freezes an intersecting pose. A clean canonical rest
            // (touching contacts ≤ baseline + slop) passes untouched → assembly is byte-for-byte the same. The
            // baseline is keyed off the slot this piece would occupy (its nearest same-Kind slot).
            var restVerts = Geometry.WorldVertices(_typeByInstance[piece.InstanceId].Shape, pose.Position, pose.Rotation);
            var restSlot = NearestSameKindSlot(piece.Kind, pose.Position);
            if (OverlapsAnyPlacedBeyondBaseline(restVerts, piece.InstanceId, restSlot))
                return;

            // TASK-042 gate-v2 (owner 2026-07-26 20:42, pt.1 — the main residual of the user "невидимая опора" bug):
            // the in-fall snap already carries a support gate, but freeze-at-rest through THIS path did not — so a
            // piece momentarily resting on a still-Dynamic (Falling) neighbour was frozen, and once that neighbour
            // drove away the frozen piece hung in mid-air. Only a static or an already-Placed piece is PERMANENT
            // support; a Falling piece is not (IsPoseSupported credits statics + Placed only). Not supported → stay
            // Dynamic and keep settling; a wrong order that never gains real support just times out (an honest
            // un-winnable order) rather than cementing a floating pose. In canon a piece always rests on a
            // static/Placed piece by the time it stops (releases resolve bottom-up), so this passes → assembly stays
            // byte-for-byte. Balance mode returned early above (PlaceDynamic), so this only gates the assembly freeze.
            //
            // gate-v3 (owner 2026-07-26 20:55): count the rejection. IsPoseSupported now shares its probe with the sim
            // detector (SnapSupportProbeDistance — see SimRun.ComputeHonestyMetrics), so a pose this guard accepts as
            // "landed" is grounded in the detector too, and any pose it rejects here stays Dynamic instead of freezing
            // in air. The count is the owner's proof the freeze path is covered.
            if (!IsPoseSupported(restVerts))
            {
                _freezeRejectsBySupport++;
                return;
            }

            // Physical-settle piece (TASK-024 → TASK-030 variant B, owner playtest 2026-07-25): it skipped the
            // in-fall snap and has now come to physical rest after contact torque righted its authored tilt. FREEZE
            // IT EXACTLY WHERE PHYSICS LEFT IT — no pose or angle correction, neither instant nor glided. The old
            // micro-finish glide (BeginSettle) is removed for tilted pieces: the owner saw it "nudge" the piece onto
            // its slot 0.3-0.5 s after landing, which reads as non-physics ("пододвигание"). The piece must land in
            // the silhouette purely by falling-hitting-rotating-sliding under physics. We still CLAIM the nearest
            // free same-Kind slot (greedy by position via FindSnapSlot, mirroring BalanceSnapLoads) so the win check
            // and slot monopoly still map one piece to one slot — but the pose stays the physical rest pose and
            // simply falls through to the freeze-where-it-lies path below. The slot tolerances (posTol/angTol) are
            // now genuinely judge-only. A piece that settled outside every capture window (honest miss) claims
            // nothing and still freezes where it lies. Stable kinematic support for the next piece (§3) is intact —
            // freezing at rest still turns it into a rigid body the next drop cannot shove.
            if (NeedsPhysicalSettle(piece))
            {
                var slot = FindSnapSlot(piece, bodyId, enforceHonesty: false);
                if (slot >= 0)
                    ClaimSlot(piece, slot);
            }

            FreezePlaced(piece, pose.Position, pose.Rotation);
            _simulation.FreezeAt(bodyId, pose.Position, pose.Rotation);
        }

        /// <summary>
        /// Balance-mode landing for a free-beam body (TASK-023): the beam OR a load piece (pan / weight / cap).
        /// Mark it Placed so it counts toward the verdict, but DO NOT freeze it — it stays Dynamic and rides the
        /// physics as the beam swings, so the figure is only complete once the whole field settles (the global
        /// rest gate in Tick). MirrorPose keeps its model/view pose tracking the live body every tick. The beam is
        /// judged from this free rest pose (a tilted beam fails on angle); loads are crisped onto their slots at
        /// global rest by <see cref="BalanceSnapLoads"/>. Supersedes the TASK-021 PlacePivot (world-pinned hinge),
        /// removed with the pivot approach.
        /// </summary>
        private void PlaceDynamic(PieceInstance piece, BodyPose pose)
        {
            FreezePlaced(piece, pose.Position, pose.Rotation);
        }

        /// <summary>
        /// Balance-mode final crisp-up (TASK-021 round-3, Failure B). Run once at global rest: for each settled
        /// NON-pivot load, re-run the support-gate snap (<see cref="FindSnapSlot"/> — same capture window and greedy
        /// nearest-slot assignment as the assembly assist) against its live rest pose; if it landed within a slot's
        /// window, freeze it onto the exact slot pose and claim the slot. A level beam leaves each pan/weight/cap
        /// stack sitting at its slot ⇒ every load captures ⇒ clean pose match ⇒ win; a tilted beam parks the loads
        /// off-slot ⇒ FindSnapSlot misses them ⇒ the figure stays incomplete ⇒ correctly no win. This closes the gap
        /// that free physics alone cannot: a pan resting ON the beam is never inside posTol of its authored slot.
        ///
        /// The pivot beam is deliberately NOT snapped — its match is judged from the free hinge rest pose, because
        /// the crank angle IS the balance mechanic (a beam that settled tilted must fail on angle).
        ///
        /// Determinism: snapping ONLY at global rest (never mid-swing) keeps the pre-snap poses fixed-step
        /// reproducible and the snap a deterministic pose assignment, so poseHash stays stable across repeats.
        ///
        /// TASK-023 §7 (playtest #3 "the loads glide in unphysically"): instead of an instant FreezeAt teleport,
        /// each captured load begins the SAME soft-settle glide the assembly path uses (BeginSettle) — a short
        /// critically-damped ease from its rest pose onto the exact slot pose. It reads as the piece settling into
        /// place rather than a magnet snapping it. The verdict is deferred while any glide runs (ResolveAtRest
        /// returns early on a non-empty _settling, mirroring the Tick settle gate), so the figure is never judged
        /// mid-glide. The glide is a fixed step count (SettleDurationSeconds) → determinism intact, residual ≈ 0.
        /// </summary>
        private void BalanceSnapLoads()
        {
            foreach (var piece in _model.Pieces)
            {
                if (piece.State != PieceLifecycle.Placed)
                    continue;
                if (_typeByInstance[piece.InstanceId].Pivot)
                    continue; // the free beam is judged from its rest pose, never snapped
                if (_settling.ContainsKey(piece.InstanceId))
                    continue; // already gliding onto its slot
                if (!_bodyByInstance.TryGetValue(piece.InstanceId, out var bodyId))
                    continue;

                // TASK-042 gate-v2 (owner 2026-07-26 20:42, pt.3): the balance crisp obeys the SAME admissibility as
                // the assembly in-fall snap (enforceHonesty: true). On a LEVEL beam every load already rests at its
                // authored slot pose on the (Placed) beam, so both clamps pass by construction → canon crisps exactly
                // as before. On a TILTED beam the authored slot pose would drive the load through / hang it above the
                // displaced beam, so the support+baseline gate rejects the snap and the load honestly stays at its
                // physical rest pose (owner: "снап тянет грузы в слоты сквозь наклонённую балку" — pen up to 0.08).
                var slot = FindSnapSlot(piece, bodyId, enforceHonesty: true);
                if (slot < 0)
                    continue;

                BeginSettle(piece, bodyId, slot); // claims the slot + starts the eased glide (§7)
            }
        }

        /// <summary>
        /// Pure-physics crisp-at-rest (TASK-031d, opt-in <see cref="LevelData.FreezeLoadsAtRest"/>) — the variant-B
        /// freeze (TASK-030) applied to a whole catapult/balance level. Called ONLY at the FINAL global rest (round-2:
        /// no piece Idle, no release pending — the caller bails on any intermediate rest), so every piece has stayed
        /// Dynamic the whole level and this runs exactly once, on the settled field. For every settled non-pivot
        /// piece: claim its nearest same-Kind slot (greedy by position, mirroring <see cref="BalanceSnapLoads"/>) for
        /// the win check + slot monopoly, then FREEZE it EXACTLY at its physical rest pose — no glide, no position or
        /// angle correction. The piece must have reached the silhouette by real physics (falling, hitting, rotating,
        /// launching); posTol/angTol only JUDGE the result. A piece that settled outside every capture window (a
        /// launched load in the pit) claims nothing and simply freezes where it lies. Unlike BalanceSnapLoads this
        /// never fills <c>_settling</c>, so the verdict is not deferred. The pivot beam, if any, is left judged from
        /// its free rest pose (this level's lever is an unmarked preplaced piece, so nothing is skipped here).
        ///
        /// Determinism: freezing only at global rest (never mid-swing) with no physics step between the reads keeps
        /// the pose assignment reproducible, so poseHash stays stable (same argument as BalanceSnapLoads).
        /// </summary>
        private void FreezeLoadsAtRest()
        {
            foreach (var piece in _model.Pieces)
            {
                if (piece.State != PieceLifecycle.Placed)
                    continue;
                if (_typeByInstance[piece.InstanceId].Pivot)
                    continue; // a marked pivot beam is judged from its free rest pose, never frozen onto a slot
                if (!_bodyByInstance.TryGetValue(piece.InstanceId, out var bodyId))
                    continue;

                var pose = _simulation.GetPose(bodyId);
                var slot = FindSnapSlot(piece, bodyId, enforceHonesty: false);
                if (slot >= 0)
                    ClaimSlot(piece, slot);

                _simulation.FreezeAt(bodyId, pose.Position, pose.Rotation);
                FreezePlaced(piece, pose.Position, pose.Rotation);
            }
        }

        /// <summary>
        /// Kick off the soft-settle glide for a captured piece: read its capture velocity (BEFORE freezing, so
        /// the glide can preserve the fall's momentum — human 12:02), claim the slot, kinematic-lock the body at
        /// its current (possibly still-falling) capture pose right now (so contacts/gravity can't drift it before
        /// the glide), mark it Placed (out of the Falling branch — the verdict is deferred while _settling is
        /// non-empty), and record the path. The glide continues the momentum into the slot via a critically-damped
        /// approach and turns the rotation onto the slot's exact target angle so the full tilt settles into the
        /// silhouette (human 2026-07-24); game-balancer 13 §1/§6 for the timing. Called from two places with the
        /// SAME body: the assembly in-fall snap (flat pieces, mid-fall capture pose) and the balance crisp-at-rest
        /// (BalanceSnapLoads, capture pose ≈ a physical REST pose so the angular glide is a tiny residual). Tilted
        /// physical-settle pieces do NOT use this glide since TASK-030 variant B — they freeze at their rest pose
        /// in Place with no correction, so the whole visible turn is real contact torque.
        /// </summary>
        private void BeginSettle(PieceInstance piece, int bodyId, int slot)
        {
            ClaimSlot(piece, slot);

            var target = _level.Target.Slots[slot];
            var captured = _simulation.GetPose(bodyId);
            var capturedVel = _simulation.GetVelocity(bodyId); // read before FreezeAt zeroes it
            var symmetryOrder = _typeByInstance[piece.InstanceId].SymmetryOrder;

            _simulation.FreezeAt(bodyId, captured.Position, captured.Rotation);

            piece.State = PieceLifecycle.Placed;
            _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Placed);

            var steps = (int)MathF.Round(_tuning.SettleDurationSeconds / _tuning.FixedStep);
            if (steps < 1)
                steps = 1;

            _settling[piece.InstanceId] = new Settle
            {
                BodyId = bodyId,
                Pos = captured.Position,
                // Seed the critically-damped glide with the capture velocity so it continues the fall instead of
                // restarting from a dead stop (human 12:02: no hitch). The approach never overshoots the slot and
                // leaves at most a tiny within-tolerance residual (the organic hand-placed feel, designer 12 §3.2).
                VelX = capturedVel.X,
                VelY = capturedVel.Y,
                StartRot = captured.Rotation,
                TargetPos = target.Position,
                TargetRot = SettleTargetRotation(captured.Rotation, target.Rotation, symmetryOrder),
                Step = 0,
                TotalSteps = steps,
            };
        }

        /// <summary>Freeze a piece Placed at an exact pose (no glide) and mirror it into the model + view.</summary>
        private void FreezePlaced(PieceInstance piece, Vec2 pos, float rot)
        {
            piece.State = PieceLifecycle.Placed;
            piece.Position = pos;
            piece.Rotation = rot;
            _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Placed);
            _viewByInstance[piece.InstanceId].Sync(pos, rot);
        }

        /// <summary>
        /// Settle target rotation — the piece turns its FULL landing tilt to the slot's exact angle (the nearest
        /// symmetry-equivalent along the shortest arc), so it VISIBLY rotates into the silhouette and sits clean
        /// (human 2026-07-24: "должна физически поворачиваться … реально садится в силуэт"). Supersedes the old
        /// "minimally successful angle" that left up to angTol of tilt (human 11:36) — reversed 2026-07-24. Settle
        /// now drives the angle to the exact target; angTol only JUDGES the result, it is no longer a turn floor.
        /// </summary>
        private static float SettleTargetRotation(float captured, float slot, int symmetryOrder)
        {
            var signed = SignedSymmetryReduce(captured - slot, symmetryOrder);
            return captured - signed; // ≡ slot (mod symmetry), nearest to captured → visible turn = |signed|.
        }

        /// <summary>Easing t∈[0,1]→[0,1]. SmootherStep (default) is C2 at both ends; the others are A/B alts.</summary>
        private static float Ease(SettleEasing curve, float t)
        {
            if (t <= 0f)
                return 0f;
            if (t >= 1f)
                return 1f;
            switch (curve)
            {
                case SettleEasing.SmoothStep:
                    return t * t * (3f - 2f * t);
                case SettleEasing.EaseOutCubic:
                    var inv = 1f - t;
                    return 1f - inv * inv * inv;
                default: // SmootherStep — 6t⁵−15t⁴+10t³
                    return t * t * t * (t * (t * 6f - 15f) + 10f);
            }
        }

        /// <summary>
        /// Nearest unclaimed slot of the same Kind within the snap capture window (position AND symmetry-reduced
        /// rotation), or -1 when snapping is off or nothing qualifies. Mirrors the evaluator's admissibility so
        /// a snapped piece trivially matches (RFC 07 §2.2). <paramref name="enforceHonesty"/> is true only for the
        /// in-fall snap that MOVES the piece onto the slot pose (that pose must be non-interpenetrating + supported,
        /// TASK-042); the claim-only callers pass false, since they freeze the piece at its physical rest pose.
        /// </summary>
        private int FindSnapSlot(PieceInstance piece, int bodyId, bool enforceHonesty)
        {
            if (!_tuning.SnapToSlotEnabled)
                return -1;

            var slots = _level.Target?.Slots;
            if (slots == null)
                return -1;

            var pose = _simulation.GetPose(bodyId);
            var symmetryOrder = _typeByInstance[piece.InstanceId].SymmetryOrder;

            var best = -1;
            var bestPosErr = float.MaxValue;
            for (var s = 0; s < slots.Count; s++)
            {
                if (_occupiedSlots.Contains(s))
                    continue;

                var slot = slots[s];
                if (slot.Kind != piece.Kind)
                    continue;

                var posErr = (slot.Position - pose.Position).Length;
                if (posErr > _tuning.SnapCaptureRadius)
                    continue;

                var angErr = SymmetryReduce(pose.Rotation - slot.Rotation, symmetryOrder);
                if (angErr > _tuning.SnapCaptureAngle)
                    continue;

                // TASK-042 honesty gate (in-fall snap only): reject a slot whose exact pose would drive the piece
                // through an already-placed piece beyond the authored baseline, or hang it on an invisible support
                // (see IsSnapPoseAdmissible). In a correct order the silhouette slots overlap only by design and each
                // rests on real support, so this always passes → assembly unchanged.
                if (enforceHonesty && !IsSnapPoseAdmissible(piece, s, slot))
                    continue;

                if (posErr < bestPosErr)
                {
                    bestPosErr = posErr;
                    best = s;
                }
            }

            return best;
        }

        /// <summary>
        /// TASK-042 honesty gate for a candidate snap (revised after the owner gate-FAIL 2026-07-26). The snap crisps
        /// a piece onto slot <paramref name="slotIndex"/>'s exact pose, so that pose must be physically admissible:
        /// (a) it must not drive the piece THROUGH an already-placed piece beyond the AUTHORED baseline overlap of
        /// the two slots (owner: candles interpenetrating the plate) — measuring against the design baseline, not an
        /// absolute tolerance, is what lets an authored micro-overlap like owl's ear-in-head pass by construction;
        /// and (b) the slot pose must rest on real support — a static or an already-placed piece within
        /// SnapSupportProbeDistance beneath it — so the snap never hangs a piece on an invisible platform (owner:
        /// "детали замирают в воздухе"). This replaces v1's absolute SnapMaxLift, which mis-rejected canon pieces
        /// that legitimately snap up a small distance onto genuine support. Rejections are counted by cause for the
        /// sim diagnostic. In a correct order both clamps pass → the assembly path is byte-for-byte unchanged.
        /// </summary>
        private bool IsSnapPoseAdmissible(PieceInstance piece, int slotIndex, TargetSlot slot)
        {
            var verts = Geometry.WorldVertices(_typeByInstance[piece.InstanceId].Shape, slot.Position, slot.Rotation);

            // (a) No interpenetration beyond the authored design overlap of this pair of slots.
            if (OverlapsAnyPlacedBeyondBaseline(verts, piece.InstanceId, slotIndex))
            {
                _snapRejectsByOverlap++;
                return false;
            }

            // (b) No invisible support: the slot pose must sit on a static or an already-placed piece.
            if (!IsPoseSupported(verts))
            {
                _snapRejectsBySupport++;
                return false;
            }

            return true;
        }

        /// <summary>True when <paramref name="verts"/> penetrates any Placed piece (other than
        /// <paramref name="selfId"/>) by MORE than that pair's authored baseline overlap plus <c>OverlapTolerance</c>
        /// — the interpenetration invariant (TASK-042). The baseline (slots <paramref name="selfSlot"/> ↔ the other
        /// piece's claimed slot) absorbs authored micro-overlaps so the canonical silhouette is never flagged; only
        /// overlap the design did not intend trips it. Idle (collider off) and Falling (still moving) pieces are not
        /// part of the settled figure and are excluded. A slot-less piece (selfSlot &lt; 0, an off-slot rest) gets a
        /// 0 baseline — any real overlap with it is unauthored.</summary>
        private bool OverlapsAnyPlacedBeyondBaseline(Vec2[] verts, int selfId, int selfSlot)
        {
            foreach (var other in _model.Pieces)
            {
                if (other.InstanceId == selfId || other.State != PieceLifecycle.Placed)
                    continue;
                if (!_typeByInstance.TryGetValue(other.InstanceId, out var otherType))
                    continue;
                var otherVerts = Geometry.WorldVertices(otherType.Shape, other.Position, other.Rotation);
                var pen = Geometry.ConvexPenetration(verts, otherVerts);
                var allowed = SlotBaselineBetween(selfSlot, SlotOf(other.InstanceId)) + _tuning.OverlapTolerance;
                if (pen > allowed)
                    return true;
            }
            return false;
        }

        /// <summary>True when the pose <paramref name="verts"/>, dropped by <c>SnapSupportProbeDistance</c>, touches
        /// a static or any already-Placed piece — i.e. it rests on real geometry rather than hanging in air
        /// (TASK-042). Every Placed piece is itself admitted only when supported, so touching any of them is
        /// sufficient (transitive groundedness holds by induction); statics are the base case.</summary>
        private bool IsPoseSupported(Vec2[] verts)
        {
            var lowered = ShiftDown(verts, _tuning.SnapSupportProbeDistance);

            var statics = _level.StaticGeometry;
            if (statics != null)
            {
                foreach (var s in statics)
                {
                    if (s?.Outline == null || s.Outline.Length < 3)
                        continue;
                    if (Geometry.ConvexPenetration(lowered, s.Outline) > 0f)
                        return true;
                }
            }

            foreach (var other in _model.Pieces)
            {
                if (other.State != PieceLifecycle.Placed)
                    continue;
                if (!_typeByInstance.TryGetValue(other.InstanceId, out var otherType))
                    continue;
                var otherVerts = Geometry.WorldVertices(otherType.Shape, other.Position, other.Rotation);
                if (Geometry.ConvexPenetration(lowered, otherVerts) > 0f)
                    return true;
            }

            return false;
        }

        /// <summary>Copy of <paramref name="verts"/> shifted straight down by <paramref name="d"/> — the small probe
        /// that turns "resting on" into an overlap the SAT test can see (TASK-042).</summary>
        private static Vec2[] ShiftDown(Vec2[] verts, float d)
        {
            var r = new Vec2[verts.Length];
            for (var i = 0; i < verts.Length; i++)
                r[i] = new Vec2(verts[i].X, verts[i].Y - d);
            return r;
        }

        // ── TASK-042 slot bookkeeping & authored-baseline map ─────────────────────────────────────────────────────

        /// <summary>Mark <paramref name="slot"/> claimed by <paramref name="piece"/> — the one-slot-one-piece
        /// monopoly (RFC 07 §2.2) PLUS the InstanceId → slot map the honesty gate reads to look up the pair's
        /// authored baseline overlap. Every prior <c>_occupiedSlots.Add</c> site now routes through here.</summary>
        private void ClaimSlot(PieceInstance piece, int slot)
        {
            _occupiedSlots.Add(slot);
            _slotByInstance[piece.InstanceId] = slot;
        }

        /// <summary>Claim the nearest UNOCCUPIED same-Kind slot for a piece that is Placed but never ran the live
        /// snap that would claim one (a restored snapshot) — keeps the gate's baseline lookups consistent.</summary>
        private void ClaimNearestSlot(PieceInstance piece)
        {
            var slot = NearestSameKindSlot(piece.Kind, piece.Position);
            if (slot >= 0 && !_occupiedSlots.Contains(slot))
                ClaimSlot(piece, slot);
        }

        /// <summary>The slot index a Placed piece claimed, or -1 if it never claimed one (froze off-slot).</summary>
        private int SlotOf(int instanceId) =>
            _slotByInstance.TryGetValue(instanceId, out var slot) ? slot : -1;

        /// <summary>Authored (design) overlap between two slots — 0 when either index is unclaimed/out of range.</summary>
        private float SlotBaselineBetween(int a, int b)
        {
            if (_slotBaseline == null || a < 0 || b < 0 ||
                a >= _slotBaseline.GetLength(0) || b >= _slotBaseline.GetLength(1))
                return 0f;
            return _slotBaseline[a, b];
        }

        /// <summary>Nearest same-Kind slot to <paramref name="pos"/> by position (ignores occupancy and the capture
        /// window — used only to pick the baseline reference for an off-slot rest), or -1 when none matches.</summary>
        private int NearestSameKindSlot(string kind, Vec2 pos)
        {
            var slots = _level.Target?.Slots;
            if (slots == null)
                return -1;

            var best = -1;
            var bestErr = float.MaxValue;
            for (var s = 0; s < slots.Count; s++)
            {
                if (slots[s].Kind != kind)
                    continue;
                var err = (slots[s].Position - pos).Length;
                if (err < bestErr)
                {
                    bestErr = err;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>The authored baseline overlap the sim honesty detector must allow between two Placed pieces so
        /// its proof agrees with the controller's gate (TASK-042). Keyed off the slots the two pieces claimed;
        /// 0 when either is slot-less. Public read-only for the sim; never mutates state.</summary>
        public float OverlapAllowance(int instanceIdA, int instanceIdB) =>
            SlotBaselineBetween(SlotOf(instanceIdA), SlotOf(instanceIdB));

        /// <summary>The slot index a Placed piece claimed, or -1 if it never claimed one — read by the sim's
        /// worst-pair diagnostic (TASK-042 gate-v2 pt.2) so the report shows which slots a flagged pair mapped to
        /// (the owner's cross-mapping vs. slop question). Never mutates state.</summary>
        public int ClaimedSlot(int instanceId) => SlotOf(instanceId);

        /// <summary>Number of snap candidates rejected this run because the slot pose interpenetrated a placed piece
        /// beyond the authored baseline. Non-zero on a canonical run flags a baseline the load-time bake under-counts
        /// (owner: reason-counters, not guessing). Reset per level in <see cref="ClearRuntime"/>.</summary>
        public int SnapRejectsByOverlap => _snapRejectsByOverlap;

        /// <summary>Number of snap candidates rejected this run because the slot pose had no static/placed support
        /// beneath it (would hang in air). Non-zero on a canonical run flags a slot the bot could not physically
        /// reach with support in place. Reset per level in <see cref="ClearRuntime"/>.</summary>
        public int SnapRejectsBySupport => _snapRejectsBySupport;

        /// <summary>Number of freeze-at-rest attempts the Place support-guard rejected this run (TASK-042 gate-v3).
        /// The freeze-path counterpart of <see cref="SnapRejectsBySupport"/>: non-zero in a wrong order proves the
        /// freeze path is support-gated (owner's coverage proof); 0 on a canonical run. Reset per level in
        /// <see cref="ClearRuntime"/>.</summary>
        public int FreezeRejectsBySupport => _freezeRejectsBySupport;

        /// <summary>
        /// Bake the authored-baseline overlap map (TASK-042): with each Kind's shape placed at its exact slot pose,
        /// the pairwise SAT penetration of every two slots. This is the overlap the DESIGN intends (0 for disjoint
        /// slots, a small positive for authored nestings like owl's ear in the head). The honesty gate admits a
        /// snap/rest pose only within this baseline + OverlapTolerance, so the canonical silhouette — whose poses ARE
        /// these slot poses — passes on every level by construction, while a wrong-order gross overlap still trips.
        /// </summary>
        private void BuildSlotBaseline()
        {
            _slotBaseline = null;
            var slots = _level.Target?.Slots;
            if (slots == null || slots.Count == 0)
                return;

            // Kind → shape, from the pieces the level pre-placed (each slot Kind has a matching piece).
            var shapeByKind = new Dictionary<string, Shape>();
            foreach (var piece in _model.Pieces)
            {
                var type = _model.TypeFor(piece);
                if (type != null && !shapeByKind.ContainsKey(type.Kind))
                    shapeByKind[type.Kind] = type.Shape;
            }

            var count = slots.Count;
            var worldVerts = new Vec2[count][];
            for (var i = 0; i < count; i++)
            {
                if (shapeByKind.TryGetValue(slots[i].Kind, out var shape))
                    worldVerts[i] = Geometry.WorldVertices(shape, slots[i].Position, slots[i].Rotation);
            }

            _slotBaseline = new float[count, count];
            for (var i = 0; i < count; i++)
            {
                if (worldVerts[i] == null)
                    continue;
                for (var j = i + 1; j < count; j++)
                {
                    if (worldVerts[j] == null)
                        continue;
                    var pen = Geometry.ConvexPenetration(worldVerts[i], worldVerts[j]);
                    _slotBaseline[i, j] = pen;
                    _slotBaseline[j, i] = pen;
                }
            }
        }

        /// <summary>
        /// Reduce an angle delta into the piece's symmetry period and return its magnitude (box order=4 → 90°;
        /// triangle/half order=1 → full circle). Same reduction as <see cref="PoseMatchEvaluator"/> so the snap
        /// window and the win check agree.
        /// </summary>
        private static float SymmetryReduce(float delta, int symmetryOrder)
            => MathF.Abs(SignedSymmetryReduce(delta, symmetryOrder));

        /// <summary>Signed counterpart of <see cref="SymmetryReduce"/>: the reduced delta in (−period/2, period/2].
        /// Used to glide along the shortest arc toward a slot while keeping the sign of the residual.</summary>
        private static float SignedSymmetryReduce(float delta, int symmetryOrder)
        {
            var order = symmetryOrder < 1 ? 1 : symmetryOrder;
            var period = (2f * MathF.PI) / order;
            var d = delta % period;
            if (d > period * 0.5f)
                d -= period;
            else if (d < -period * 0.5f)
                d += period;
            return d;
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
            var anyLost = false;
            var anyIdle = false;
            foreach (var piece in _model.Pieces)
            {
                if (piece.State == PieceLifecycle.Lost) anyLost = true;
                else if (piece.State == PieceLifecycle.Idle) anyIdle = true;
            }

            // Balance-mode crisp-up at global rest. Two paths, both judging only at final rest (canon §3):
            //  • freezeLoadsAtRest (TASK-031 round-2, pure physics): everything stayed Dynamic; freeze in place ONLY
            //    when the field is final (no Idle, no pending release) — see FreezeLoadsAtRest. Intermediate global
            //    rests bail out untouched so a load can still be launched by a later tap.
            //  • default (TASK-021 round-3, Failure B): the pivot beam is judged from its free rest pose, but the
            //    loads riding it are pure Dynamic physics with snap OFF — free physics parks a pan ON the beam, never
            //    inside its authored slot, so even a perfectly level beam scored medianMatchError ≈ posTol and never
            //    won. Snap the settled loads into their slots now (details in BalanceSnapLoads), gated on !anyIdle so
            //    an in-progress game is never crisped early.
            if (_balanceMode)
            {
                if (_level.FreezeLoadsAtRest)
                {
                    // Pure-physics verdict (TASK-031 round-2): under freezeLoadsAtRest EVERY piece stays Dynamic the
                    // whole level — nothing is frozen, claimed, or judged at an INTERMEDIATE global rest. While any
                    // piece is still Idle (the player has taps left) OR a coalescence group is arming, the field is
                    // not final: bail out without touching poses so a still-launchable load is never frozen mid-turn
                    // (the owner's "B фризится на палке до удара" regress). Only once nothing is Idle and no release
                    // is pending is the field truly final — then freeze every piece EXACTLY where physics parked it
                    // (claiming its nearest slot for the win check) and greedy-match the live poses. No shift, ever.
                    if (anyIdle || _pendingRelease.Count > 0 || _coalesceWindowStepsLeft > 0)
                        return;
                    FreezeLoadsAtRest();
                }
                else if (!anyIdle)
                {
                    BalanceSnapLoads();
                    // §7: the snap now begins a soft-settle glide instead of an instant teleport, so if any load
                    // started gliding, defer the verdict until it completes (Tick's settle gate keeps ResolveAtRest
                    // from being re-entered until _settling drains). Determinism is intact — the glide is fixed-step.
                    if (_settling.Count > 0)
                        return;
                }
            }

            var result = _match.Evaluate(_level.Target, CollectPlaced(), _level.MatchTolerance);

            if (result.AllSlotsFilled && !anyLost)
            {
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
            foreach (var piece in _model.Pieces)
            {
                if (piece.State != PieceLifecycle.Placed)
                    continue;
                var type = _typeByInstance[piece.InstanceId];
                _placedScratch.Add(new PlacedPiece(piece.Kind, piece.Position, piece.Rotation, type.SymmetryOrder));
            }
            return _placedScratch;
        }

        /// <summary>Nearest Idle piece whose shape (at its current pose) contains the tapped point (RFC §5.2).</summary>
        private PieceInstance PickIdlePieceAt(Vec2 p)
        {
            PieceInstance best = null;
            var bestSqr = float.MaxValue;

            foreach (var piece in _model.Pieces)
            {
                if (piece.State != PieceLifecycle.Idle || !_bodyByInstance.ContainsKey(piece.InstanceId))
                    continue;

                var type = _typeByInstance[piece.InstanceId];
                var world = Geometry.WorldVertices(type.Shape, piece.Position, piece.Rotation);
                if (!Geometry.PointInPolygon(world, p))
                    continue;

                var sqr = (piece.Position - p).SqrLength; // tie-break by centroid distance (determinism)
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = piece;
                }
            }

            return best;
        }

        private void SpawnIdle(PieceInstance piece)
        {
            var type = _model.TypeFor(piece);
            var bodyId = _simulation.SpawnFloating(type, piece.Position, piece.Rotation);

            _bodyByInstance[piece.InstanceId] = bodyId;
            _typeByInstance[piece.InstanceId] = type;
            RegisterPhysicalSettle(piece, type);

            var view = _viewFactory.Create(type);
            view.SetState(PieceLifecycle.Idle);
            view.Sync(piece.Position, piece.Rotation);
            _viewByInstance[piece.InstanceId] = view;
        }

        /// <summary>
        /// Spawn a preplaced piece (TASK-031b): identical body/view/classification setup to <see cref="SpawnIdle"/>,
        /// but the body is RELEASED immediately so it drops into the physics at t=0 (Falling), NOT hung Idle. It is
        /// therefore never picked by <see cref="PickIdlePieceAt"/> (Idle-only), never awaited as a tap, and — unlike
        /// <see cref="ReleasePiece"/> — does NOT count a player attempt (it is level furniture, not a move). From
        /// here it follows the ordinary fall→rest→Place path; in balance/catapult it lands as a free Dynamic body
        /// (PlaceDynamic) so a later drop can pivot it.
        /// </summary>
        private void SpawnPreplaced(PieceInstance piece)
        {
            var type = _model.TypeFor(piece);
            var bodyId = _simulation.SpawnFloating(type, piece.Position, piece.Rotation);

            _bodyByInstance[piece.InstanceId] = bodyId;
            _typeByInstance[piece.InstanceId] = type;
            RegisterPhysicalSettle(piece, type);

            _simulation.Release(bodyId);
            piece.State = PieceLifecycle.Falling;
            piece.RestTimer = 0f;

            var view = _viewFactory.Create(type);
            view.SetState(PieceLifecycle.Falling);
            view.Sync(piece.Position, piece.Rotation);
            _viewByInstance[piece.InstanceId] = view;
        }

        /// <summary>
        /// Classify a piece for the TASK-024 physical-settle path at spawn: if its authored start rotation differs
        /// from its NEAREST same-Kind slot's angle by more than <see cref="ShapeFillTuning.PhysicalSettleTiltThreshold"/>
        /// (symmetry-reduced, using the evaluator's reduction so snap/win agree), it is flagged so it skips the
        /// in-fall snap and is righted by real contact torque before being crisped at rest. Flat pieces (start ≈
        /// slot angle) are never flagged → byte-for-byte the pre-TASK-024 in-fall path. Deterministic from the
        /// level data (no persisted state), so it is safe to recompute on every spawn/restore.
        /// </summary>
        private void RegisterPhysicalSettle(PieceInstance piece, PieceType type)
        {
            var slots = _level?.Target?.Slots;
            if (slots == null || slots.Count == 0)
                return;

            // Nearest same-Kind slot to the authored start pose = the slot this piece is dropped for (levels author
            // a near-straight drop above the target, RFC §1). Its angle is what the piece must rotate onto.
            var bestPosSqr = float.MaxValue;
            var nearestRot = 0f;
            var found = false;
            for (var s = 0; s < slots.Count; s++)
            {
                var slot = slots[s];
                if (slot.Kind != piece.Kind)
                    continue;

                var posSqr = (slot.Position - piece.Position).SqrLength;
                if (posSqr < bestPosSqr)
                {
                    bestPosSqr = posSqr;
                    nearestRot = slot.Rotation;
                    found = true;
                }
            }

            if (!found)
                return;

            var residual = SymmetryReduce(piece.Rotation - nearestRot, type.SymmetryOrder);
            if (residual > _tuning.PhysicalSettleTiltThreshold)
                _needsPhysicalSettle.Add(piece.InstanceId);
        }

        /// <summary>True when the piece was flagged at spawn to settle on real contact torque (TASK-024).</summary>
        private bool NeedsPhysicalSettle(PieceInstance piece)
            => _needsPhysicalSettle.Contains(piece.InstanceId);

        private void ClearRuntime()
        {
            foreach (var view in _viewByInstance.Values)
                view.Dispose();
            _bodyByInstance.Clear();
            _viewByInstance.Clear();
            _typeByInstance.Clear();
            _occupiedSlots.Clear();
            _slotByInstance.Clear();
            _snapRejectsByOverlap = 0;
            _snapRejectsBySupport = 0;
            _freezeRejectsBySupport = 0;
            _settling.Clear();
            _needsPhysicalSettle.Clear();
            _globalRestTimer = 0f;
            _pendingRelease.Clear();
            _coalesceWindowStepsLeft = 0;
            _lastGlobalRestReached = false;
            // NB: _catapultMode / _balanceMode are deliberately NOT reset here — ClearRuntime runs AFTER both
            // OnLevelLoaded and OnStateRestored have set the mode from the level, so clearing them would clobber it.
        }

        public void Dispose()
        {
            _levelLoadedSub?.Dispose();
            _levelLoadedSub = null;
            _stateRestoredSub?.Dispose();
            _stateRestoredSub = null;
        }
    }
}
