using System.Collections.Generic;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using UnityEngine;
using Rect = Gameplay.Core.Rect; // disambiguate from UnityEngine.Rect

namespace Gameplay.Adapters
{
    /// <summary>
    /// Production physics adapter (RFC §3): owns a Physics2D world in Script simulation mode. Step(dt)
    /// runs exactly one Physics2D.Simulate(dt) — run-time acceleration is achieved by calling Step more
    /// times per frame (step COUNT), never by enlarging the fixed step (ADR-0001). Vec2 ⇄ Vector2 lives
    /// only here; the domain stays Unity-free.
    /// </summary>
    public sealed class UnityShapeFillSimulation : IShapeFillSimulation
    {
        private sealed class Body
        {
            public Rigidbody2D Rigidbody;
            public Collider2D Collider;
            public float Radius;
        }

        private readonly Dictionary<int, Body> _bodies = new();
        private readonly List<GameObject> _statics = new();
        // Reused across HasSupportBelow calls so the every-fixed-step support probe allocates nothing (no
        // GC in the physics hot path).
        private readonly RaycastHit2D[] _supportHits = new RaycastHit2D[8];
        private readonly ShapeFillTuning _tuning;
        private Transform _root;
        private Rect _bounds;
        private int _nextId = 1;

        public UnityShapeFillSimulation(ShapeFillTuning tuning)
        {
            _tuning = tuning;
        }

        private Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var go = new GameObject("ShapeFillSimulation");
                    _root = go.transform;
                }
                return _root;
            }
        }

        public void LoadStatics(Polygon[] staticGeometry, Rect bounds, Vec2 gravity)
        {
            _bounds = bounds;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = new Vector2(gravity.X, gravity.Y);

            if (staticGeometry == null)
                return;

            foreach (var polygon in staticGeometry)
            {
                if (polygon?.Outline == null || polygon.Outline.Length < 3)
                    continue;

                var go = new GameObject("Static");
                go.transform.SetParent(Root, false);
                var collider = go.AddComponent<PolygonCollider2D>();
                collider.points = ToPoints(polygon.Outline);
                _statics.Add(go);
            }
        }

        public int SpawnFloating(PieceType type, Vec2 spawn, float rotation)
        {
            var go = new GameObject($"Piece_{type.Kind}_{_nextId}");
            go.transform.SetParent(Root, false);
            go.transform.position = new Vector3(spawn.X, spawn.Y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, rotation * Mathf.Rad2Deg);

            var collider = go.AddComponent<PolygonCollider2D>();
            collider.points = ToPoints(type.Shape.Vertices);
            collider.sharedMaterial = new PhysicsMaterial2D($"mat_{type.Kind}")
            {
                friction = type.Material.Friction,
                bounciness = type.Material.Restitution,
            };

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.useAutoMass = true;
            rb.gravityScale = 0f; // floating: at rest until released
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;

            // Density MUST be assigned after the auto-mass Dynamic body exists — Unity rejects Collider2D.density
            // on a collider that has no attached dynamic auto-mass body ("Density cannot be set on the collider
            // unless it is attached to a dynamic rigid-body that is using auto-mass"), which fired during setup on
            // the balance multi-load levels and aborted their sim runs (TASK-021 round-3, SpawnFloating). The value
            // is unchanged and every campaign density is 1.0 (the collider default), so the mass auto-computed when
            // Release() enables the collider is byte-for-byte identical to before — the assembly path is untouched.
            collider.density = Mathf.Max(0.0001f, type.Material.Density);
            // Idle piece is physically absent: no collision so hanging pieces never block another's drop
            // column (RFC §5.3). Release() re-enables the collider + gravity.
            collider.enabled = false;

            var id = _nextId++;
            _bodies[id] = new Body
            {
                Rigidbody = rb,
                Collider = collider,
                // Collider is disabled while Idle, so its bounds are unreliable — derive the rest-speed radius
                // from the shape vertices instead (used only for the angular-speed term in GetSpeed).
                Radius = VertexRadius(type.Shape.Vertices),
            };
            return id;
        }

        public int SpawnSettled(PieceType type, Vec2 pos, float rotation)
        {
            var go = new GameObject($"Piece_{type.Kind}_{_nextId}");
            go.transform.SetParent(Root, false);
            go.transform.position = new Vector3(pos.X, pos.Y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, rotation * Mathf.Rad2Deg);

            var collider = go.AddComponent<PolygonCollider2D>();
            collider.points = ToPoints(type.Shape.Vertices);
            collider.density = Mathf.Max(0.0001f, type.Material.Density);
            collider.sharedMaterial = new PhysicsMaterial2D($"mat_{type.Kind}")
            {
                friction = type.Material.Friction,
                bounciness = type.Material.Restitution,
            };

            var rb = go.AddComponent<Rigidbody2D>();
            // Kinematic: the restored stack keeps its exact saved pose (coverage preserved), no re-settle.
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;

            var id = _nextId++;
            _bodies[id] = new Body
            {
                Rigidbody = rb,
                Collider = collider,
                Radius = Mathf.Max(0.0001f, collider.bounds.extents.magnitude),
            };
            return id;
        }

        public void Release(int bodyId)
        {
            if (!_bodies.TryGetValue(bodyId, out var body) || body.Rigidbody == null)
                return;
            if (body.Collider != null)
                body.Collider.enabled = true; // materialise: Idle piece becomes physical (RFC §5.3)
            body.Rigidbody.gravityScale = 1f;
            // Damping settles the fall (angular in particular tames rolling half-discs) — RFC 07 §2.3.
            body.Rigidbody.linearDamping = _tuning.ReleaseLinearDrag;
            body.Rigidbody.angularDamping = _tuning.ReleaseAngularDrag;
        }

        public void FreezeAt(int bodyId, Vec2 position, float rotation)
        {
            if (!_bodies.TryGetValue(bodyId, out var body) || body.Rigidbody == null)
                return;
            var rb = body.Rigidbody;
            // Lock exactly like SpawnSettled: zero velocity, kinematic, exact pose → no post-rest drift, and the
            // body becomes a rigid support the next piece cannot shove (RFC 07 §2.1).
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            var deg = rotation * Mathf.Rad2Deg;
            rb.position = new Vector2(position.X, position.Y);
            rb.rotation = deg;
            var t = rb.transform;
            t.position = new Vector3(position.X, position.Y, t.position.z);
            t.rotation = Quaternion.Euler(0f, 0f, deg);
        }

        public BodyPose GetPose(int bodyId)
        {
            if (!_bodies.TryGetValue(bodyId, out var body) || body.Rigidbody == null)
                return default;
            var p = body.Rigidbody.position;
            return new BodyPose(new Vec2(p.x, p.y), body.Rigidbody.rotation * Mathf.Deg2Rad);
        }

        public float GetSpeed(int bodyId)
        {
            if (!_bodies.TryGetValue(bodyId, out var body) || body.Rigidbody == null)
                return 0f;
            var linear = body.Rigidbody.linearVelocity.magnitude;
            var angular = Mathf.Abs(body.Rigidbody.angularVelocity) * Mathf.Deg2Rad * body.Radius;
            return Mathf.Max(linear, angular);
        }

        public Vec2 GetVelocity(int bodyId)
        {
            if (!_bodies.TryGetValue(bodyId, out var body) || body.Rigidbody == null)
                return Vec2.Zero;
            var v = body.Rigidbody.linearVelocity;
            return new Vec2(v.x, v.y);
        }

        public bool HasSupportBelow(int bodyId, float maxGap)
        {
            if (!_bodies.TryGetValue(bodyId, out var body) || body.Collider == null || !body.Collider.enabled)
                return false;

            // Sweep the piece's own collider straight down by the small probe gap: this reports whatever it
            // would first touch below its actual lower edge (any shape/rotation), initial overlaps included.
            // ignoreSiblingColliders excludes the piece itself.
            var count = body.Collider.Cast(Vector2.down, _supportHits, maxGap);
            for (var i = 0; i < count; i++)
            {
                var other = _supportHits[i].collider;
                if (other == null || other == body.Collider)
                    continue;
                // Support = the floor (a static collider, no rigidbody) or an already-settled piece (kinematic,
                // locked by FreezeAt/SpawnSettled). Another still-falling dynamic body is not yet a support.
                var otherRb = other.attachedRigidbody;
                if (otherRb == null || otherRb.bodyType == RigidbodyType2D.Kinematic)
                    return true;
            }
            return false;
        }

        public bool IsWithinBounds(int bodyId)
        {
            if (!_bodies.TryGetValue(bodyId, out var body) || body.Rigidbody == null)
                return false;
            var p = body.Rigidbody.position;
            return _bounds.Contains(new Vec2(p.x, p.y));
        }

        public void Despawn(int bodyId)
        {
            if (_bodies.TryGetValue(bodyId, out var body))
            {
                if (body.Rigidbody != null)
                    Object.Destroy(body.Rigidbody.gameObject);
                _bodies.Remove(bodyId);
            }
        }

        public void Step(float dt) => Physics2D.Simulate(dt);

        public void Reset()
        {
            foreach (var body in _bodies.Values)
            {
                if (body.Rigidbody != null)
                    Object.Destroy(body.Rigidbody.gameObject);
            }
            _bodies.Clear();

            foreach (var go in _statics)
            {
                if (go != null)
                    Object.Destroy(go);
            }
            _statics.Clear();
            _nextId = 1;
        }

        public long Hash()
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                foreach (var kv in _bodies)
                {
                    if (kv.Value.Rigidbody == null)
                        continue;
                    var p = kv.Value.Rigidbody.position;
                    hash = (hash ^ kv.Key) * 1099511628211L;
                    hash = (hash ^ (long)(p.x * 1000f)) * 1099511628211L;
                    hash = (hash ^ (long)(p.y * 1000f)) * 1099511628211L;
                    hash = (hash ^ (long)(kv.Value.Rigidbody.rotation * 1000f)) * 1099511628211L;
                }
                return hash;
            }
        }

        private static Vector2[] ToPoints(Vec2[] verts)
        {
            var points = new Vector2[verts.Length];
            for (var i = 0; i < verts.Length; i++)
                points[i] = new Vector2(verts[i].X, verts[i].Y);
            return points;
        }

        private static float VertexRadius(Vec2[] verts)
        {
            var max = 0.0001f;
            foreach (var v in verts)
            {
                var r = v.Length;
                if (r > max) max = r;
            }
            return max;
        }
    }
}
