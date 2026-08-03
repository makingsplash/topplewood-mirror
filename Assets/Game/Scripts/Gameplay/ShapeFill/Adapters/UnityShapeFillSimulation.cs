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
#if GPIE_DEVELOPMENT
            // Retained only so the [SHAPELOG] diagnostic can name the piece at Release time (TASK-072); the
            // adapter no longer holds the PieceType. Guarded so the release player never carries dead fields.
            public string Kind;
            public ShapePrimitive Primitive;
#endif
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

            // TASK-072: a circle gets a NATIVE CircleCollider2D from the AUTHORED radius, not a PolygonCollider2D
            // built from Shape.Circle's trig vertices. Those vertices are platform-dependent (Mono vs IL2CPP
            // MathF.Cos/Sin) and, at 24 segments, exceed Box2D's 8-vertex convex cap so the poly is re-split into
            // convex sub-shapes by a split that depends on the exact coordinates — either alone shifts the contact
            // manifold editor↔device by double-digit percent. A native circle has one exact manifold and no
            // vertices, removing both. Every other primitive keeps a PolygonCollider2D (its vertices are exact, or
            // — for the trig-based half/quarter discs — quantized in Shape to a platform-invariant grid).
            Collider2D collider;
            if (type.Shape.Primitive == ShapePrimitive.Circle)
            {
                var circle = go.AddComponent<CircleCollider2D>();
                circle.radius = type.Shape.Radius; // authored radius, NOT derived from vertices (would change mass)
                collider = circle;
            }
            else
            {
                var polygon = go.AddComponent<PolygonCollider2D>();
                polygon.points = ToPoints(type.Shape.Vertices);
                collider = polygon;
            }
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
#if GPIE_DEVELOPMENT
                Kind = type.Kind,
                Primitive = type.Shape.Primitive,
#endif
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
#if GPIE_DEVELOPMENT
            LogShape(bodyId, body);
#endif
        }

        public void FreezeAt(int bodyId, Vec2 position, float rotation)
        {
            if (!_bodies.TryGetValue(bodyId, out var body) || body.Rigidbody == null)
                return;
            var rb = body.Rigidbody;
            // Lock at rest: zero velocity, kinematic, exact pose → no post-rest drift, and the body becomes a
            // rigid support the next piece cannot shove (RFC 07 §2.1).
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
                // locked by FreezeAt). Another still-falling dynamic body is not yet a support.
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

#if GPIE_DEVELOPMENT
        // TASK-072 diagnostic: one [SHAPELOG] line per piece the instant it materialises. Emitted at Release
        // (not SpawnFloating) BECAUSE that is when the collider is enabled and Unity's auto-mass is actually
        // computed — at spawn the collider is disabled and rb.mass/inertia are not yet the real values (see the
        // density note in SpawnFloating). Same InvariantCulture / fixed-width formatting as [STEPLOG] so the
        // editor and device logs diff byte-for-byte: if collider vertices differ across platforms the trig-poly
        // input was platform-dependent (mechanism #1); if they match but mass/inertia differ the cause is the
        // convex re-split / manifold generation (mechanism #2). GPIE_DEVELOPMENT-gated — never in a shipped build.
        private void LogShape(int bodyId, Body body)
        {
            var rb = body.Rigidbody;
            var sb = new System.Text.StringBuilder();
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append("[SHAPELOG] id=").Append(bodyId.ToString(ic))
                .Append(" kind=").Append(body.Kind)
                .Append(" primitive=").Append(body.Primitive.ToString());

            if (body.Collider is CircleCollider2D circle)
            {
                sb.Append(" collider=circle radius=").Append(F5(circle.radius)).Append(" verts=0");
            }
            else if (body.Collider is PolygonCollider2D polygon)
            {
                var pts = polygon.points;
                sb.Append(" collider=polygon verts=").Append(pts.Length.ToString(ic));
                for (var i = 0; i < pts.Length; i++)
                    sb.Append(" v").Append(i.ToString(ic))
                        .Append("=(").Append(F5(pts[i].x)).Append(',').Append(F5(pts[i].y)).Append(')');
            }

            sb.Append(" mass=").Append(F5(rb.mass)).Append(" inertia=").Append(F5(rb.inertia));
            Debug.Log(sb.ToString());
        }

        // Signed fixed-width — identical formatting to StepPoseLogger.F5 so [SHAPELOG] and [STEPLOG] diff cleanly.
        private static string F5(float v)
            => v.ToString("+0.00000;-0.00000", System.Globalization.CultureInfo.InvariantCulture);
#endif
    }
}
