using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Shared unlit material for block/silhouette meshes. Colour is driven per-instance via a
    /// MaterialPropertyBlock, so one material batches all blocks. Prefers URP/Unlit, falls back to the
    /// built-in sprite shader.
    /// </summary>
    public static class BlockMaterial
    {
        public static readonly int ColorProperty = Shader.PropertyToID("_BaseColor");
        public static readonly int LegacyColorProperty = Shader.PropertyToID("_Color");
        private static readonly int CullProperty = Shader.PropertyToID("_Cull");

        private static Material _shared;

        public static Material Shared
        {
            get
            {
                if (_shared == null)
                {
                    // Game/Wood Block рисует волокно, фаску и блик процедурно и держит тот же
                    // _BaseColor per-instance, что и URP/Unlit, — покраска деталей не меняется.
                    // Если шейдер не собрался (не попал в сборку / вырезан стриппингом),
                    // откатываемся на прежнюю цепочку: деталь станет плоской, но не пропадёт.
                    var shader = Shader.Find("Game/Wood Block")
                                 ?? Shader.Find("Universal Render Pipeline/Unlit")
                                 ?? Shader.Find("Sprites/Default")
                                 ?? Shader.Find("Unlit/Color");
                    _shared = new Material(shader) { name = "ShapeFillBlockMaterial" };

                    // Blocks are one-sided fan meshes (normal +Z) and tumble under physics, while the
                    // bringup camera looks along +Z — with the default Cull Back the visible face is culled
                    // and blocks disappear in Play (TASK-007 rework). Render double-sided. Guarded so it is a
                    // no-op on fallback shaders (Sprites/Default, Unlit/Color) that have no _Cull property.
                    if (_shared.HasProperty(CullProperty))
                        _shared.SetFloat(CullProperty, (float)UnityEngine.Rendering.CullMode.Off);

                    // Настройки стиля (WoodTuning) кладём СРАЗУ: иначе материал, созданный после
                    // загрузки сохранённых чисел, поехал бы с дефолтами шейдера — и деталь в игре
                    // отличалась бы от той, что владелец настроил в песочнице.
                    // Именно ApplyToBlock, а не Apply: Apply сам берёт Shared и получилось бы кольцо.
                    WoodTuning.Current.ApplyToBlock(_shared);
                }
                return _shared;
            }
        }
    }
}
