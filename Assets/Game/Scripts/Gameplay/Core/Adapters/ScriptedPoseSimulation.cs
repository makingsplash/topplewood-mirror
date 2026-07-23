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
            public Vec2 Velocity;
            public bool WithinBounds = true;
            public bool Released;
            // No physics here to derive support from, so the pose-canvas author drives it. Defaults true so the
            // synthetic snap behaviour is unchanged (snap allowed) unless a test explicitly models free fall.
            public bool SupportBelow = true;
        }

        private readonly Dictionary<int, Body> _bodies = new();
        private int _nextId = 1;

        public int SpawnFloating(PieceType type, Vec2 spawn, float rotation)
        {
            var id = _nextId++;
            _bodies[id] = new Body { Pose = new BodyPose(spawn, rotation), Speed = 0f };
            return id;
        }

        public int SpawnSettled(PieceType type, Vec2 pos, float rotation)
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

        public void FreezeAt(int bodyId, Vec2 position, float rotation)
        {
            if (_bodies.TryGetValue(bodyId, out var b))
            {
                b.Pose = new BodyPose(position, rotation);
                b.Speed = 0f;
            }
        }

        public BodyPose GetPose(int bodyId) => _bodies.TryGetValue(bodyId, out var b) ? b.Pose : default;
        public float GetSpeed(int bodyId) => _bodies.TryGetValue(bodyId, out var b) ? b.Speed : 0f;
        public Vec2 GetVelocity(int bodyId) => _bodies.TryGetValue(bodyId, out var b) ? b.Velocity : Vec2.Zero;
        public bool IsWithinBounds(int bodyId) => !_bodies.TryGetValue(bodyId, out var b) || b.WithinBounds;
        public bool HasSupportBelow(int bodyId, float maxGap) => _bodies.TryGetValue(bodyId, out var b) && b.SupportBelow;

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

        public void SetVelocity(int bodyId, Vec2 velocity)
        {
            if (_bodies.TryGetValue(bodyId, out var b))
                b.Velocity = velocity;
        }

        public void SetWithinBounds(int bodyId, bool within)
        {
            if (_bodies.TryGetValue(bodyId, out var b))
                b.WithinBounds = within;
        }

        public void SetSupportBelow(int bodyId, bool supported)
        {
            if (_bodies.TryGetValue(bodyId, out var b))
                b.SupportBelow = supported;
        }
    }
}
