using System.Collections.Generic;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
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
            // TASK-178: one collider for the convex primitives (circle/polygon), N for a concave polyomino (a compound
            // body — one PolygonCollider2D per authored convex part on the SAME Rigidbody2D). All toggled/materialised
            // together, so [0] is representative for the enabled-state guard.
            public Collider2D[] Colliders;
            public float Radius;
            // TASK-097 released-only gate: air flow acts ONLY on bodies that have been released (tapped, or
            // preplaced-as-released). An Idle floating piece is a full Dynamic body (gravity off, collider off) that
            // AddForce WOULD otherwise blow off its authored hang pose — see the owner's live-code refutation in the
            // stage-1 gate. Set true in Release(); never reset. TASK-305: a Placed body now stays Dynamic (freeze
            // removed), so flow continues to act on it after it rests — intended live physics (see ApplyFlow).
            public bool Released;
#if GPIE_DEVELOPMENT
            // Retained only so the [SHAPELOG] diagnostic can name the piece at Release time (TASK-072); the
            // adapter no longer holds the PieceType. Guarded so the release player never carries dead fields.
            public string Kind;
            public ShapePrimitive Primitive;
#endif
        }

        private readonly Dictionary<int, Body> _bodies = new();
        private readonly List<GameObject> _statics = new();
        // TASK-097: directed air-flow zones for this level. Null/empty ⇒ no flow (every pre-TASK-097 level). Read
        // every Step by ApplyFlow; cleared by Reset. Domain data (no UnityEngine), the same array the domain holds.
        private AirZone[] _flowZones;
        // Reused across HasSupportBelow calls so the every-fixed-step support probe allocates nothing (no
        // GC in the physics hot path).
        private readonly RaycastHit2D[] _supportHits = new RaycastHit2D[8];
        private readonly ShapeFillTuning _tuning;
        private Transform _root;
        private Rect _bounds;
        private int _nextId = 1;
        // TASK-325 (S2): true once this level loaded at least one SURFACE static (an EdgeCollider2D chain). A thin edge
        // chain is a zero-width film a fast piece can tunnel THROUGH in one Discrete step, so pieces in such a level use
        // continuous collision (see SpawnFloating). Set in LoadStatics, read in SpawnFloating, cleared in Reset. A level
        // with no surface leaves it false ⇒ pieces stay Discrete (Unity's default) ⇒ every pre-TASK-325 level is
        // byte-for-byte unchanged in its contact manifolds and rest poses.
        private bool _hasSurfaceStatic;
        // TASK-379: an optional LOCAL 2D physics scene that isolates this instance's Box2D world from every other
        // run's. null (production DI + any pre-TASK-379 caller) ⇒ the default/active-scene world, byte-for-byte
        // unchanged: Root stays in the active scene and Step drives the global Physics2D.Simulate. A valid handle ⇒
        // Root (and thus every body/static parented under it) is moved into that scene and Step drives ITS
        // PhysicsScene2D — so a FRESH scene per run gives a virgin broadphase / body-id pool / warm-start cache with
        // no cross-run residue. That residue (destroying bodies does NOT reset the world) is the root cause of the
        // history-/parity-dependent verdicts diagnosed in dev-architect 01 §2; a per-run scene is the documented
        // Unity "hard restart" (reload-the-scene) done cheaply, without a domain reload.
        private readonly Scene? _physicsScene;

        // Production DI constructor (VContainer): pinned with [Inject] so the container binds THIS exact parameter
        // list (ShapeFillTuning only, as before) regardless of the second constructor — prod resolves no Scene and
        // stays byte-for-byte. The isolation ctor below is called explicitly by the sim harness (LevelSimRunner).
        [Inject]
        public UnityShapeFillSimulation(ShapeFillTuning tuning) : this(tuning, null)
        {
        }

        // TASK-379: isolation constructor used by LevelSimRunner — physicsScene is this run's own local 2D world.
        public UnityShapeFillSimulation(ShapeFillTuning tuning, Scene? physicsScene)
        {
            _tuning = tuning;
            _physicsScene = physicsScene;
        }

        private Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var go = new GameObject("ShapeFillSimulation");
                    // TASK-379: when isolating, move the root into this run's own physics scene. Every body/static is
                    // parented under Root, so they follow it into the scene and simulate in — and are destroyed with
                    // — that isolated Box2D world. MoveGameObjectToScene requires a scene-root object; Root has no
                    // parent, so this is valid.
                    if (_physicsScene.HasValue)
                        SceneManager.MoveGameObjectToScene(go, _physicsScene.Value);
                    _root = go.transform;
                }
                return _root;
            }
        }

        public void LoadStatics(Polygon[] staticGeometry, Rect bounds, Vec2 gravity)
        {
            _bounds = bounds;
            _hasSurfaceStatic = false;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = new Vector2(gravity.X, gravity.Y);

            if (staticGeometry == null)
                return;

            foreach (var polygon in staticGeometry)
            {
                if (polygon?.Outline == null)
                    continue;

                // TASK-346: a VISUAL-ONLY static (collider:false) builds NO physics body — it is drawn by the presenter
                // (a concave wood mass) but never enters the sim, so its concave outline can never reach Box2D's split.
                // The sim world is thus byte-identical whether such a block is present or not (poseHash gate, TASK-346).
                if (!polygon.HasCollider)
                    continue;

                // TASK-325 (S2): a SURFACE traces the authored polyline EXACTLY with an EdgeCollider2D chain (an open
                // chain of segments, no coordinate-dependent convex split — device-deterministic by construction). A
                // FILLED SOLID keeps the single PolygonCollider2D built from the whole contour (unchanged). A chain
                // needs ≥2 points; a solid ≥3 (the validator flags too-few loudly — this guard only skips a degenerate).
                if (polygon.IsSurface)
                {
                    if (polygon.Outline.Length < 2)
                        continue;
                    var go = new GameObject("StaticSurface");
                    go.transform.SetParent(Root, false);
                    var edge = go.AddComponent<EdgeCollider2D>();
                    edge.points = ToPoints(polygon.Outline);
                    _statics.Add(go);
                    _hasSurfaceStatic = true;
                }
                else
                {
                    if (polygon.Outline.Length < 3)
                        continue;
                    var go = new GameObject("Static");
                    go.transform.SetParent(Root, false);
                    var collider = go.AddComponent<PolygonCollider2D>();
                    collider.points = ToPoints(polygon.Outline);
                    _statics.Add(go);
                }
            }
        }

        public void LoadFlowZones(AirZone[] zones) => _flowZones = zones;

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
            // TASK-072/178: a circle → NATIVE CircleCollider2D (see note above); a concave polyomino → a COMPOUND of
            // one PolygonCollider2D per authored convex part (Shape.ConvexParts, exact & platform-invariant, so Box2D
            // never re-splits a concave contour by a coordinate-dependent split); every other primitive → one
            // PolygonCollider2D from its exact/quantized vertices.
            Collider2D[] colliders;
            if (type.Shape.Primitive == ShapePrimitive.Circle)
            {
                var circle = go.AddComponent<CircleCollider2D>();
                circle.radius = type.Shape.Radius; // authored radius, NOT derived from vertices (would change mass)
                colliders = new Collider2D[] { circle };
            }
            else if (type.Shape.ConvexParts != null)
            {
                var parts = type.Shape.ConvexParts;
                colliders = new Collider2D[parts.Length];
                for (var i = 0; i < parts.Length; i++)
                {
                    var poly = go.AddComponent<PolygonCollider2D>();
                    poly.points = ToPoints(parts[i]);
                    colliders[i] = poly;
                }
            }
            else
            {
                var polygon = go.AddComponent<PolygonCollider2D>();
                polygon.points = ToPoints(type.Shape.Vertices);
                colliders = new Collider2D[] { polygon };
            }

            // One shared material across all parts (friction/restitution is per-piece, not per-part).
            var material = new PhysicsMaterial2D($"mat_{type.Kind}")
            {
                friction = type.Material.Friction,
                bounciness = type.Material.Restitution,
            };
            foreach (var c in colliders)
                c.sharedMaterial = material;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.useAutoMass = true;
            rb.gravityScale = 0f; // floating: at rest until released
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            // TASK-325 (S2 named cost): if this level carries a thin EdgeCollider2D SURFACE, a fast piece can tunnel
            // straight through the zero-width film in one Discrete step (dev-architect RFC 03, Q3). Continuous collision
            // sub-steps the sweep so it cannot. Applied ONLY when a surface exists ⇒ the whole pre-TASK-325 corpus (no
            // surface) stays Discrete and byte-for-byte unchanged; the anti-tunnel LOWER BOUND on segment length is the
            // balancer's sim-gate number, tracked as tech-debt, not hardcoded here.
            rb.collisionDetectionMode = _hasSurfaceStatic
                ? CollisionDetectionMode2D.Continuous
                : CollisionDetectionMode2D.Discrete;

            // Density MUST be assigned after the auto-mass Dynamic body exists — Unity rejects Collider2D.density
            // on a collider that has no attached dynamic auto-mass body ("Density cannot be set on the collider
            // unless it is attached to a dynamic rigid-body that is using auto-mass"), which fired during setup on
            // the balance multi-load levels and aborted their sim runs (TASK-021 round-3, SpawnFloating). The value
            // is unchanged and every campaign density is 1.0 (the collider default), so the mass auto-computed when
            // Release() enables the collider is byte-for-byte identical to before — the assembly path is untouched.
            // TASK-178: the SAME density on every part ⇒ auto-mass sums the exact part areas ⇒ the compound mass and
            // inertia are the deterministic sum of rectangles (no trig), platform-invariant by construction.
            var density = Mathf.Max(0.0001f, type.Material.Density);
            foreach (var c in colliders)
            {
                c.density = density;
                // Idle piece is physically absent: no collision so hanging pieces never block another's drop
                // column (RFC §5.3). Release() re-enables the colliders + gravity.
                c.enabled = false;
            }

            var id = _nextId++;
            _bodies[id] = new Body
            {
                Rigidbody = rb,
                Colliders = colliders,
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
            if (body.Colliders != null)
                foreach (var c in body.Colliders)
                    if (c != null)
                        c.enabled = true; // materialise: Idle piece becomes physical (RFC §5.3)
            body.Released = true; // TASK-097: from now on air-flow zones may push this body (released-only gate)
            body.Rigidbody.gravityScale = 1f;
            // Damping settles the fall (angular in particular tames rolling half-discs) — RFC 07 §2.3.
            body.Rigidbody.linearDamping = _tuning.ReleaseLinearDrag;
            body.Rigidbody.angularDamping = _tuning.ReleaseAngularDrag;
#if GPIE_DEVELOPMENT
            LogShape(bodyId, body);
#endif
        }

        // TASK-305: FreezeAt removed — no body is ever transitioned to Kinematic. A rested piece stays Dynamic; the
        // verdict is judged at global rest (see ShapeFillMechanicController). Nothing here locks a pose anymore.

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
            if (!_bodies.TryGetValue(bodyId, out var body) || body.Colliders == null ||
                body.Colliders.Length == 0 || body.Colliders[0] == null || !body.Colliders[0].enabled)
                return false;

            // TASK-178: sweep ALL of the body's colliders straight down by the small probe gap. Rigidbody2D.Cast casts
            // every attached collider at once and EXCLUDES the body's own colliders, so a compound polyomino is probed
            // by whichever part sits lowest — casting a single part would be blind to the part that actually rests
            // first. For a single-collider body this is byte-equivalent to the old Collider.Cast. Reports whatever it
            // would first touch below the actual lower edge (any shape/rotation), initial overlaps included.
            var count = body.Rigidbody.Cast(Vector2.down, _supportHits, maxGap);
            for (var i = 0; i < count; i++)
            {
                var other = _supportHits[i].collider;
                if (other == null || other.attachedRigidbody == body.Rigidbody)
                    continue; // skip our own colliders (belt-and-braces; Rigidbody.Cast already excludes them)
                // Support = the floor (a static collider, no rigidbody) or a kinematic body. NB (TASK-305): no piece
                // is ever kinematic anymore (freeze removed), so the kinematic branch is now dead — this whole seam
                // has had no production callers since TASK-043 (assist snap removed) and is a deprecation follow-up.
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

        public void Step(float dt)
        {
            ApplyFlow(); // TASK-097: accumulate directed air-flow force before integrating this step
            // TASK-379: step THIS run's isolated world when a local scene was supplied; otherwise the global
            // default-scene world (production path, byte-for-byte unchanged). A LocalPhysicsMode.Physics2D scene is
            // never auto-simulated — it advances ONLY through its own PhysicsScene2D.Simulate.
            if (_physicsScene.HasValue)
                _physicsScene.Value.GetPhysicsScene2D().Simulate(dt);
            else
                Physics2D.Simulate(dt);
        }

        /// <summary>
        /// TASK-097: apply each air-flow zone's directed force to the released Dynamic bodies inside it, once per
        /// Step, immediately before <see cref="Physics2D.Simulate"/>. Force is <c>direction · acceleration · mass</c>:
        /// Unity-2D's <c>AddForce</c> has only <c>ForceMode2D.Force</c> (∝ 1/m), no Acceleration mode, so multiplying
        /// by <c>rb.mass</c> yields a mass-INDEPENDENT acceleration <c>a = F/m = accel</c> for any mass — the
        /// balancer's mass-independent model (spread 209×) realised without per-piece tuning.
        /// Determinism (ADR-0001): each body reads only its own zones and accumulates additively (order-invariant);
        /// no body reads another's mutating state; called exactly once per fixed Step. Membership is by body CENTRE
        /// (rb.position) — see FEATURES.md / level-authoring §L(air): the zone must be large relative to a piece.
        /// </summary>
        private void ApplyFlow()
        {
            if (_flowZones == null || _flowZones.Length == 0)
                return;

            foreach (var body in _bodies.Values)
            {
                if (!body.Released) // idle-hanging pieces (not yet tapped) keep their authored pose — owner stage-1 gate
                    continue;
                var rb = body.Rigidbody;
                // TASK-305: a Placed piece now stays Dynamic (freeze removed), so — unlike before — flow keeps acting
                // on a released piece after it rests. That is intended (live physics): a piece pressed into its saddle
                // stays at rest against the saddle normal, so global rest is still reached. The guard now only skips a
                // body with no live Rigidbody or a non-Dynamic one (none is ever Kinematic anymore).
                if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic)
                    continue;

                var p = rb.position;
                var centre = new Vec2(p.x, p.y);
                foreach (var zone in _flowZones)
                {
                    if (!zone.Region.Contains(centre))
                        continue;
                    var force = new Vector2(zone.Direction.X, zone.Direction.Y) * (zone.Acceleration * rb.mass);
                    rb.AddForce(force);
                }
            }
        }

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
            _flowZones = null; // TASK-097: drop the level's flow zones on teardown (OnLevelLoaded reinstalls them)
            _hasSurfaceStatic = false; // TASK-325: LoadStatics re-derives it for the next level
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

            // TASK-178: a compound polyomino logs every part (part index + its vertices) so the editor↔device parity
            // diff covers the summed mass/inertia of the whole compound, not just one part.
            var colliders = body.Colliders ?? System.Array.Empty<Collider2D>();
            sb.Append(" parts=").Append(colliders.Length.ToString(ic));
            for (var p = 0; p < colliders.Length; p++)
            {
                if (colliders[p] is CircleCollider2D circle)
                {
                    sb.Append(" p").Append(p.ToString(ic)).Append("=circle radius=").Append(F5(circle.radius));
                }
                else if (colliders[p] is PolygonCollider2D polygon)
                {
                    var pts = polygon.points;
                    sb.Append(" p").Append(p.ToString(ic)).Append("=polygon verts=").Append(pts.Length.ToString(ic));
                    for (var i = 0; i < pts.Length; i++)
                        sb.Append(" v").Append(i.ToString(ic))
                            .Append("=(").Append(F5(pts[i].x)).Append(',').Append(F5(pts[i].y)).Append(')');
                }
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
