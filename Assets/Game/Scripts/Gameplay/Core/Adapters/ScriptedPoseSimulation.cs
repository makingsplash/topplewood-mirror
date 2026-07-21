using System.Collections.Generic;

namespace Gameplay.Core.Adapters
{
    /// <summary>
    /// Headless synthetic simulation (RFC §3): replays a POSE CANVAS instead of running physics. There is
    /// no pure-C# physics (ADR-0001) — this exercises mechanic LOGIC (FSM / coverage / win) deterministically.
    /// QA sets each body's pose/speed/in-bounds between Step() calls. Real solvability is validated with the
    /// Unity implementation in batchmode, not here.
    /// </summary>
    public sealed class ScriptedPoseSimulation : IShapeFillSimulation
    {
        private sealed class Body
        {
            public BodyPose Pose;
            public float Speed;
            public bool WithinBounds = true;
            public bool Released;
        }

        private readonly Dictionary<int, Body> _bodies = new();
        private int _nextId = 1;

        public int SpawnFloating(BlockDef def, Vec2 spawn)
        {
            var id = _nextId++;
            _bodies[id] = new Body { Pose = new BodyPose(spawn, 0f), Speed = 0f };
            return id;
        }

        public int SpawnSettled(BlockDef def, Vec2 pos, float rotation)
        {
            var id = _nextId++;
            _bodies[id] = new Body { Pose = new BodyPose(pos, rotation), Speed = 0f };
            return id;
        }

        public void Release(int bodyId)
        {
            if (_bodies.TryGetValue(bodyId, out var b))
                b.Released = true;
        }

        public BodyPose GetPose(int bodyId) => _bodies.TryGetValue(bodyId, out var b) ? b.Pose : default;
        public float GetSpeed(int bodyId) => _bodies.TryGetValue(bodyId, out var b) ? b.Speed : 0f;
        public bool IsWithinBounds(int bodyId) => !_bodies.TryGetValue(bodyId, out var b) || b.WithinBounds;

        public void Despawn(int bodyId) => _bodies.Remove(bodyId);

        public void LoadStatics(Polygon[] staticGeometry, Rect bounds, Vec2 gravity) { }

        public void Reset()
        {
            _bodies.Clear();
            _nextId = 1;
        }

        public void Step(float dt) { }

        public long Hash()
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                foreach (var kv in _bodies)
                {
                    hash = (hash ^ kv.Key) * 1099511628211L;
                    hash = (hash ^ Quantize(kv.Value.Pose.Position.X)) * 1099511628211L;
                    hash = (hash ^ Quantize(kv.Value.Pose.Position.Y)) * 1099511628211L;
                    hash = (hash ^ Quantize(kv.Value.Pose.Rotation)) * 1099511628211L;
                }
                return hash;
            }
        }

        private static long Quantize(float v) => (long)(v * 1000f);

        // ---- QA pose-canvas controls ----

        public void SetPose(int bodyId, Vec2 position, float rotation)
        {
            if (_bodies.TryGetValue(bodyId, out var b))
                b.Pose = new BodyPose(position, rotation);
        }

        public void SetSpeed(int bodyId, float speed)
        {
            if (_bodies.TryGetValue(bodyId, out var b))
                b.Speed = speed;
        }

        public void SetWithinBounds(int bodyId, bool within)
        {
            if (_bodies.TryGetValue(bodyId, out var b))
                b.WithinBounds = within;
        }
    }
}
