using Gameplay.Core;
using UnityEngine;
using Rect = Gameplay.Core.Rect; // disambiguate from UnityEngine.Rect

namespace Gameplay.View
{
    /// <summary>
    /// Unity view for one directed air-flow zone (TASK-097 propeller/fan). Three layers, all built once at level
    /// load (never per-frame), all nudged behind the pieces on +Z so falling pieces read in front:
    /// (1) a TRANSLUCENT fill mesh whose outline is the force <see cref="AirZone.Region"/> byte-for-byte, so §D5
    ///     "the visible area == the force area" holds by construction (the mesh is built from the very coords the
    ///     physics adapter tests membership against);
    /// (2) a procedural EMITTER bar on the up-wind edge — the propeller housing, drawn procedurally (owner PNG
    ///     decode: propeller.png has no alpha, would render as a white patch) → placeholder-grade art, folded into
    ///     TASK-082's pre-soft-launch art pass, NOT a new debt;
    /// (3) tiled ARROW sprites along the flow, loaded like <c>TutorialView</c>'s finger (Resources.Load + graceful
    ///     fallback). If the sprite is missing the fill + emitter still convey the zone and its source side.
    /// The domain stays Unity-free; all Vec2⇄Vector2 lives here.
    /// </summary>
    public sealed class AirZoneView : MonoBehaviour
    {
        // Pieces live at z=0; larger z = further from the +Z-facing camera (see BlockMaterial). Keep the whole zone
        // behind pieces, and layer fill < emitter < arrows within the zone so the cue reads over the fill.
        private const float ZFill = 0.7f;
        private const float ZEmitter = 0.6f;
        private const float ZArrow = 0.55f;

        private const float EmitterThickness = 0.18f;   // world units, along the flow axis (placeholder-grade)
        private const float ArrowWorldSize = 0.6f;       // target on-screen arrow size, world units
        private const float ArrowSpacing = 0.9f;         // gap between tiled arrows along the flow
        private const int ArrowMaxCount = 8;             // cap so a huge zone does not spawn hundreds of sprites

        // arrow-icon-1178.png relocated from the repo root into Resources (TASK-097). Owner byte-decode: 512×512,
        // RGBA, genuinely transparent background ⇒ usable as a Sprite as-is. Assumed to point +X in the source art;
        // if the pass in TASK-082 supplies a differently-oriented glyph, only ARROW_POINTS_DEG changes.
        private const string ArrowResourcePath = "Gameplay/arrow-icon-1178";
        private const float ArrowPointsDeg = 0f; // source art points +X; rotate to the flow from here

        private static readonly Color DefaultFill = new Color(0.30f, 0.70f, 1.00f, 0.28f); // translucent airy blue
        private static readonly Color EmitterColor = new Color(0.55f, 0.58f, 0.62f);       // neutral housing grey

        private static Sprite _arrowSprite;
        private static bool _arrowLoaded;

        public void Initialize(AirZone zone)
        {
            var region = zone.Region;
            var min = new Vector2(region.Min.X, region.Min.Y);
            var max = new Vector2(region.Max.X, region.Max.Y);
            var centre = (min + max) * 0.5f;
            var dir = new Vector2(zone.Direction.X, zone.Direction.Y);
            if (dir.sqrMagnitude > 0f)
                dir = dir.normalized; // domain already normalises; guard the direct-constructed path too

            BuildFill(region, zone.Color);
            BuildEmitter(min, max, centre, dir);
            BuildArrows(min, max, centre, dir);
        }

        // (1) Translucent fill mesh == the force Region (§D5). Reuses the piece/static triangulation.
        private void BuildFill(Rect region, string colorHex)
        {
            var outline = new[]
            {
                new Vec2(region.Min.X, region.Min.Y),
                new Vec2(region.Max.X, region.Min.Y),
                new Vec2(region.Max.X, region.Max.Y),
                new Vec2(region.Min.X, region.Max.Y),
            };

            var color = DefaultFill;
            if (!string.IsNullOrEmpty(colorHex) && ColorUtility.TryParseHtmlString(colorHex, out var parsed))
                color = new Color(parsed.r, parsed.g, parsed.b, DefaultFill.a); // keep the translucent alpha

            var go = NewMeshChild("Fill", PieceMeshBuilder.Build(outline), ZFill, TranslucentMaterial(color));
            go.transform.localPosition = new Vector3(0f, 0f, ZFill);
        }

        // (2) Procedural emitter bar on the up-wind edge (opposite the flow). Spans the region face perpendicular to
        // the flow; a thin slab along the flow. Support-function extents keep it flush for cardinal flows and a
        // reasonable placeholder for diagonal ones.
        private void BuildEmitter(Vector2 min, Vector2 max, Vector2 centre, Vector2 dir)
        {
            if (dir.sqrMagnitude <= 0f)
                return;
            var half = (max - min) * 0.5f;
            var perp = new Vector2(-dir.y, dir.x);
            var extentAlong = Mathf.Abs(dir.x) * half.x + Mathf.Abs(dir.y) * half.y;   // centre → up-wind face
            var extentPerp = Mathf.Abs(perp.x) * half.x + Mathf.Abs(perp.y) * half.y;  // half span across the face
            var faceCentre = centre - dir * extentAlong;

            var a = faceCentre + perp * extentPerp;
            var b = faceCentre - perp * extentPerp;
            var backA = a - dir * EmitterThickness;
            var backB = b - dir * EmitterThickness;
            var outline = new[]
            {
                new Vec2(backB.x, backB.y),
                new Vec2(b.x, b.y),
                new Vec2(a.x, a.y),
                new Vec2(backA.x, backA.y),
            };

            var mat = new Material(BlockMaterial.Shared) { name = "AirZoneEmitterMaterial" };
            mat.SetColor(BlockMaterial.ColorProperty, EmitterColor);
            mat.SetColor(BlockMaterial.LegacyColorProperty, EmitterColor);
            var go = NewMeshChild("Emitter", PieceMeshBuilder.Build(outline), ZEmitter, mat);
            go.transform.localPosition = new Vector3(0f, 0f, ZEmitter);
        }

        // (3) Tiled direction arrows. Missing sprite ⇒ graceful skip (fill + emitter still read), like TutorialView.
        private void BuildArrows(Vector2 min, Vector2 max, Vector2 centre, Vector2 dir)
        {
            if (dir.sqrMagnitude <= 0f)
                return;
            var sprite = ArrowSprite();
            if (sprite == null)
                return;

            var half = (max - min) * 0.5f;
            var extentAlong = Mathf.Abs(dir.x) * half.x + Mathf.Abs(dir.y) * half.y;
            var span = 2f * extentAlong;
            var count = Mathf.Clamp(Mathf.FloorToInt(span / ArrowSpacing), 1, ArrowMaxCount);

            var spriteSize = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            var scale = spriteSize > 0f ? ArrowWorldSize / spriteSize : 1f;
            var angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - ArrowPointsDeg;
            var start = centre - dir * (extentAlong - ArrowSpacing * 0.5f);

            for (var i = 0; i < count; i++)
            {
                var pos = start + dir * (i * ArrowSpacing);
                var go = new GameObject($"Arrow_{i}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(pos.x, pos.y, ZArrow);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                go.transform.localScale = new Vector3(scale, scale, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
            }
        }

        private GameObject NewMeshChild(string name, Mesh mesh, float z, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.mesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.transform.localPosition = new Vector3(0f, 0f, z);
            return go;
        }

        // Alpha-blended material for the fill. Sprites/Default blends by default (queue Transparent) and is always
        // present; the URP-Unlit / Unlit-Color fallbacks mirror BlockMaterial. Colour is set on the instance (few
        // zones per level) so each zone keeps its own alpha. Double-sided: the fan mesh is one-sided and the camera
        // looks along +Z, so cull-off (guarded — a no-op on shaders without _Cull) keeps the face visible.
        private static Material TranslucentMaterial(Color color)
        {
            var shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "AirZoneFillMaterial" };
            if (mat.HasProperty(BlockMaterial.ColorProperty))
                mat.SetColor(BlockMaterial.ColorProperty, color);
            if (mat.HasProperty(BlockMaterial.LegacyColorProperty))
                mat.SetColor(BlockMaterial.LegacyColorProperty, color);
            var cull = Shader.PropertyToID("_Cull");
            if (mat.HasProperty(cull))
                mat.SetFloat(cull, (float)UnityEngine.Rendering.CullMode.Off);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        private static Sprite ArrowSprite()
        {
            if (_arrowLoaded)
                return _arrowSprite;
            _arrowLoaded = true;
            _arrowSprite = Resources.Load<Sprite>(ArrowResourcePath);
            if (_arrowSprite == null)
                Debug.unityLogger.Log(LogType.Warning,
                    $"[AirZone] arrow sprite '{ArrowResourcePath}' not found — rendering zone without direction arrows");
            return _arrowSprite;
        }
    }
}
