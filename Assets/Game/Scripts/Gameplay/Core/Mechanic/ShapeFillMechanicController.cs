using System;
using System.Collections.Generic;
using Gameplay.Core.Adapters;
using R3;

namespace Gameplay.Core.Mechanic
{
    /// <summary>
    /// Fills the <c>IMechanicController</c> seam with the shape_fill loop (GAME_RULES §2–3), all pure C#
    /// (headless-testable via ScriptedPoseSimulation): tap→release, mirror simulation poses into
    /// BlockInstances, drive the Floating→Falling→Settled/Lost FSM, feed the pool, and on rest measure
    /// coverage to decide Won/Lost. Physics itself lives behind IShapeFillSimulation.
    /// </summary>
    public sealed class ShapeFillMechanicController : IMechanicController, IDisposable
    {
        private readonly GameModel _model;
        private readonly IShapeFillSimulation _simulation;
        private readonly ICoverageEvaluator _coverage;
        private readonly LevelDataValidator _validator;
        private readonly IBlockViewFactory _viewFactory;
        private readonly ShapeFillTuning _tuning;

        private readonly Dictionary<int, int> _bodyByInstance = new();
        private readonly Dictionary<int, IBlockView> _viewByInstance = new();
        private readonly Dictionary<int, BlockDef> _defByInstance = new();
        private readonly List<PlacedShape> _settledScratch = new();

        private IDisposable _levelLoadedSub;
        private IDisposable _stateRestoredSub;
        private LevelData _level;
        private ISilhouetteRegion _region;
        private float _silhouetteMinX;
        private float _silhouetteMaxX;

        public ShapeFillMechanicController(
            GameModel model,
            IShapeFillSimulation simulation,
            ICoverageEvaluator coverage,
            LevelDataValidator validator,
            IBlockViewFactory viewFactory,
            ShapeFillTuning tuning)
        {
            _model = model;
            _simulation = simulation;
            _coverage = coverage;
            _validator = validator;
            _viewFactory = viewFactory;
            _tuning = tuning;

            _levelLoadedSub = _model.LevelLoaded.Subscribe(_ => OnLevelLoaded());
            _stateRestoredSub = _model.StateRestored.Subscribe(_ => OnStateRestored());
        }

        private void OnLevelLoaded()
        {
            _level = _model.CurrentLevel;

            var validation = _validator.Validate(_level);
            if (!validation.Ok)
            {
                // Structural brak: refuse to run rather than present an unwinnable level.
                _model.Resolve(GameOutcome.Lost);
                return;
            }

            _region = new PolygonSilhouetteRegion(_level.Silhouette);
            CacheSilhouetteSpan();

            _bodyByInstance.Clear();
            _viewByInstance.Clear();
            _defByInstance.Clear();

            _simulation.Reset();
            _simulation.LoadStatics(_level.StaticGeometry, _level.Bounds, _level.Gravity);

            _model.SetPhase(GamePhase.Running);
            SpawnNext();
        }

        /// <summary>
        /// Rebuild runtime state from a restored snapshot (RFC §4.2). Runs AFTER OnLevelLoaded (restore
        /// happens onto the already-loaded level), so it resets the simulation/views the fresh load spawned
        /// and re-materialises bodies/views from each block's saved state. Settled blocks are re-spawned in
        /// their exact pose (kinematic, coverage preserved); the single Floating block is re-spawned; Lost
        /// blocks are skipped (their pool unit is already accounted for by PoolCursor).
        /// </summary>
        private void OnStateRestored()
        {
            _level = _model.CurrentLevel;
            _region = new PolygonSilhouetteRegion(_level.Silhouette);
            CacheSilhouetteSpan();

            foreach (var view in _viewByInstance.Values)
                view.Dispose();

            _bodyByInstance.Clear();
            _viewByInstance.Clear();
            _defByInstance.Clear();

            _simulation.Reset();
            _simulation.LoadStatics(_level.StaticGeometry, _level.Bounds, _level.Gravity);

            foreach (var block in _model.Blocks)
            {
                if (block.State == BlockLifecycle.Lost)
                    continue;

                var def = _model.DefFor(block);
                var bodyId = block.State == BlockLifecycle.Settled
                    ? _simulation.SpawnSettled(def, block.Position, block.Rotation)
                    : _simulation.SpawnFloating(def, block.Position);

                _bodyByInstance[block.InstanceId] = bodyId;
                _defByInstance[block.InstanceId] = def;

                var view = _viewFactory.Create(def);
                view.SetState(block.State);
                view.Sync(block.Position, block.Rotation);
                _viewByInstance[block.InstanceId] = view;
            }
        }

        public void OnIntent(InputIntent intent)
        {
            if (_model.CurrentPhase != GamePhase.Running)
                return;

            var floating = FindFloating();
            if (floating == null)
                return;

            // v0 drop mechanic (TASK-007 rework): the tap picks the drop column. Move the floating block to
            // the tapped world-X (clamped to the silhouette span) before releasing, so blocks spread across
            // the shape and coverage can reach θ instead of stacking in one fixed column. intent.X is a
            // world/level X — PointerTapInputSource already projected the screen tap through the camera.
            var def = _defByInstance[floating.InstanceId];
            var dropPos = new Vec2(ClampDropX(intent.X, def), floating.Position.Y);

            _simulation.Despawn(_bodyByInstance[floating.InstanceId]);
            var bodyId = _simulation.SpawnFloating(def, dropPos);
            _bodyByInstance[floating.InstanceId] = bodyId;
            floating.Position = dropPos;
            _viewByInstance[floating.InstanceId].Sync(dropPos, 0f);

            _simulation.Release(bodyId);
            floating.State = BlockLifecycle.Falling;
            floating.RestTimer = 0f;
            _viewByInstance[floating.InstanceId].SetState(BlockLifecycle.Falling);
            _model.RegisterAttempt();
        }

        public void Tick(float dt)
        {
            if (_model.CurrentPhase != GamePhase.Running)
                return;

            var anyFalling = false;

            foreach (var block in _model.Blocks)
            {
                if (!_bodyByInstance.TryGetValue(block.InstanceId, out var bodyId))
                    continue;

                if (block.State == BlockLifecycle.Falling)
                {
                    UpdateFalling(block, bodyId, dt);
                    if (block.State == BlockLifecycle.Falling)
                        anyFalling = true;
                }

                // The body may have been despawned (Lost) inside UpdateFalling.
                if (_bodyByInstance.TryGetValue(block.InstanceId, out bodyId))
                    MirrorPose(block, bodyId);
            }

            if (anyFalling)
                return;

            ResolveAtRest();
        }

        private void UpdateFalling(BlockInstance block, int bodyId, float dt)
        {
            if (!_simulation.IsWithinBounds(bodyId))
            {
                block.State = BlockLifecycle.Lost;
                _viewByInstance[block.InstanceId].SetState(BlockLifecycle.Lost);
                _simulation.Despawn(bodyId);
                _bodyByInstance.Remove(block.InstanceId);
                return;
            }

            if (_simulation.GetSpeed(bodyId) < _tuning.RestSpeedEpsilon)
            {
                block.RestTimer += dt;
                if (block.RestTimer >= _tuning.RestDurationSeconds)
                {
                    block.State = BlockLifecycle.Settled;
                    _viewByInstance[block.InstanceId].SetState(BlockLifecycle.Settled);
                }
            }
            else
            {
                block.RestTimer = 0f;
            }
        }

        private void MirrorPose(BlockInstance block, int bodyId)
        {
            var pose = _simulation.GetPose(bodyId);
            block.Position = pose.Position;
            block.Rotation = pose.Rotation;
            _viewByInstance[block.InstanceId].Sync(pose.Position, pose.Rotation);
        }

        private void ResolveAtRest()
        {
            var coverage = EvaluateCoverage();

            if (coverage >= _level.CoverageThreshold)
            {
                _model.Resolve(GameOutcome.Won);
                return;
            }

            if (FindFloating() != null)
                return; // a block is already queued; wait for the player's tap.

            if (ReachedAttemptLimit())
            {
                _model.Resolve(GameOutcome.Lost);
                return;
            }

            if (_model.PoolExhausted)
            {
                _model.Resolve(GameOutcome.Lost);
                return;
            }

            SpawnNext();
        }

        private float EvaluateCoverage()
        {
            _settledScratch.Clear();
            foreach (var block in _model.Blocks)
            {
                if (block.State != BlockLifecycle.Settled)
                    continue;
                _settledScratch.Add(new PlacedShape(_defByInstance[block.InstanceId].Shape, block.Position, block.Rotation));
            }
            return _coverage.Evaluate(_region, _settledScratch, _tuning.CoverageSampleN);
        }

        private bool ReachedAttemptLimit()
        {
            if (_level.AttemptLimit.HasValue && _model.AttemptsUsed >= _level.AttemptLimit.Value)
                return true;
            if (_level.MoveLimit.HasValue && _model.AttemptsUsed >= _level.MoveLimit.Value)
                return true;
            return false;
        }

        private void SpawnNext()
        {
            var block = _model.NextPooledBlock();
            if (block == null)
                return;

            var def = _model.DefFor(block);
            var spawn = _level.DropSpawn ?? new Vec2(0f, _level.Bounds.Max.Y);

            var bodyId = _simulation.SpawnFloating(def, spawn);
            block.State = BlockLifecycle.Floating;
            block.Position = spawn;
            block.RestTimer = 0f;

            _bodyByInstance[block.InstanceId] = bodyId;
            _defByInstance[block.InstanceId] = def;

            var view = _viewFactory.Create(def);
            view.SetState(BlockLifecycle.Floating);
            view.Sync(spawn, 0f);
            _viewByInstance[block.InstanceId] = view;
        }

        private void CacheSilhouetteSpan()
        {
            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var v in _level.Silhouette.Outline)
            {
                if (v.X < min) min = v.X;
                if (v.X > max) max = v.X;
            }
            _silhouetteMinX = min;
            _silhouetteMaxX = max;
        }

        /// <summary>
        /// Clamp the tapped world-X to the drop column range so the whole block footprint lands inside the
        /// silhouette (v0). If the block is wider than the silhouette it is centred.
        /// </summary>
        private float ClampDropX(float worldX, BlockDef def)
        {
            var half = ShapeHalfWidth(def.Shape);
            var lo = _silhouetteMinX + half;
            var hi = _silhouetteMaxX - half;
            if (lo > hi)
                return (_silhouetteMinX + _silhouetteMaxX) * 0.5f;
            if (worldX < lo) return lo;
            if (worldX > hi) return hi;
            return worldX;
        }

        private static float ShapeHalfWidth(Shape shape)
        {
            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var v in shape.Vertices)
            {
                if (v.X < min) min = v.X;
                if (v.X > max) max = v.X;
            }
            return (max - min) * 0.5f;
        }

        private BlockInstance FindFloating()
        {
            foreach (var block in _model.Blocks)
            {
                if (block.State == BlockLifecycle.Floating && _bodyByInstance.ContainsKey(block.InstanceId))
                    return block;
            }
            return null;
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
