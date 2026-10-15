using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Shared unlit material for block/silhouette meshes. Colour is driven per-instance via a
    /// MaterialPropertyBlock, so one material batches all blocks. Shader is <c>Game/Wood Block</c> taken from
    /// <see cref="RuntimeShaders"/> (serialized reference, so it is in the player — TASK-554); URP/Unlit and the
    /// built-in sprite shader are fallbacks only, and a miss is logged.
    /// </summary>
    public static class BlockMaterial
    {
        public static readonly int ColorProperty = Shader.PropertyToID("_BaseColor");
        public static readonly int LegacyColorProperty = Shader.PropertyToID("_Color");
        private static readonly int CullProperty = Shader.PropertyToID("_Cull");

        // TASK-353/368: канал контура силуэта для аналитического SDF-варианта шейдеров (вогнутая масса, freeform-деталь,
        // их тень). Заливается per-renderer через MaterialPropertyBlock (SetSilhouette), не в материал.
        public static readonly int ConcaveOutlineProperty = Shader.PropertyToID("_ConcaveOutline");
        public static readonly int ConcaveOutlineCountProperty = Shader.PropertyToID("_ConcaveOutlineCount");

        /// <summary>Потолок узлов контура в uniform-массиве <c>_ConcaveOutline[64]</c> (SilhouetteSdf.hlsl). Единый
        /// источник лимита для всех, кто кладёт силуэт в шейдер.</summary>
        public const int SilhouetteMax = 64;

        public const string ConcaveKeyword = "_CONCAVE_SDF";

        /// <summary>
        /// Упаковать CCW-контур для <see cref="SetSilhouette"/>. Массив ВСЕГДА длины <see cref="SilhouetteMax"/>, хвост
        /// нулями: Unity фиксирует длину массива в MaterialPropertyBlock при ПЕРВОМ <c>SetVectorArray</c> и молча
        /// обрезает более длинный массив позже — на блоке, переиспользуемом без <c>Clear()</c>, второй контур потерял бы
        /// хвост (замер TASK-368: 5 → 40 узлов дало 5; после <c>Clear()</c> длина не держится и обрезки нет).
        /// Контур длиннее лимита → <c>null</c>: отказ виден вызывающему, а не обрезан молча.
        /// </summary>
        public static Vector4[] PackSilhouette(Vec2[] ccw)
        {
            if (ccw == null || ccw.Length > SilhouetteMax)
                return null;
            var packed = new Vector4[SilhouetteMax];
            for (var i = 0; i < ccw.Length; i++)
                packed[i] = new Vector4(ccw[i].X, ccw[i].Y, 0f, 0f);
            return packed;
        }

        /// <summary>Положить упакованный контур (<see cref="PackSilhouette"/>) и число его узлов в блок. Звать ПОСЛЕ
        /// каждого <c>mpb.Clear()</c> — Clear стирает и контур.</summary>
        public static void SetSilhouette(MaterialPropertyBlock mpb, Vector4[] packed, int count)
            => SetSilhouette(mpb, packed, count, null);

        /// <summary>TASK-557: то же плюс запечённое поле силуэта (<see cref="SilhouetteField"/>) — с ним шейдер дерева
        /// берёт кромку одной выборкой вместо цикла по рёбрам. Без поля (<c>null</c>) — прежний цикл. Контур кладётся
        /// в обоих случаях: тень детали (Game/Wood Piece Shadow) считает размытие по нему.</summary>
        public static void SetSilhouette(MaterialPropertyBlock mpb, Vector4[] packed, int count, SilhouetteField field)
        {
            if (mpb == null || packed == null)
                return;
            mpb.SetVectorArray(ConcaveOutlineProperty, packed);
            mpb.SetFloat(ConcaveOutlineCountProperty, count);
            field?.Apply(mpb);
        }

        private static Material _shared;
        private static Material _concaveShared;

        public static Material Shared
        {
            get
            {
                if (_shared == null)
                {
                    // Game/Wood Block рисует фаску и блик процедурно, волокно — по шумовой текстуре
                    // (WoodGrainNoise, TASK-557), и держит тот же
                    // _BaseColor per-instance, что и URP/Unlit, — покраска деталей не меняется.
                    // Шейдер берётся сериализованной ссылкой из RuntimeShaders, поэтому он в плеере
                    // (TASK-554: строкой его в сборке не было, и деталь на устройстве была плоской).
                    // Промах реестр пишет в лог ошибкой; тогда — прежний откат: деталь плоская, но не пропадёт.
                    var shader = RuntimeShaders.WoodBlock
                                 ?? RuntimeShaders.UrpUnlit
                                 ?? RuntimeShaders.SpritesDefault;
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

                    // TASK-557: шум волокна — тайловая текстура, а не процедура в пикселе.
                    WoodGrainNoise.ApplyTo(_shared);
                }
                return _shared;
            }
        }

        /// <summary>
        /// TASK-353/368: материал «кромка по силуэту» — вогнутой ВИДИМОЙ МАССЫ и составной freeform-ДЕТАЛИ. Тот же
        /// <c>Game/Wood Block</c>, но с включённым кейвордом <c>_CONCAVE_SDF</c> — фрагмент считает расстояние до
        /// кромки аналитически по полилинии силуэта (в объектном пространстве рендерера), а не по вершинам меша, у
        /// которого внутренние рёбра дали бы шов. Кейворд живёт на МАТЕРИАЛЕ, а не в MaterialPropertyBlock (MPB
        /// кейворды не несёт), поэтому нужен отдельный инстанс материала; контур у каждого рендерера свой — в MPB.
        /// Откат — как у <see cref="Shared"/>: если <c>Game/Wood Block</c> нет в <see cref="RuntimeShaders"/> (ошибка в лог), кейворд на fallback-шейдере —
        /// no-op, и деталь деградирует в плоскую заливку, а не в пропажу.
        /// </summary>
        public static Material ConcaveShared
        {
            get
            {
                if (_concaveShared == null)
                {
                    var shader = RuntimeShaders.WoodBlock
                                 ?? RuntimeShaders.UrpUnlit
                                 ?? RuntimeShaders.SpritesDefault;
                    _concaveShared = new Material(shader) { name = "ShapeFillConcaveMaterial" };
                    _concaveShared.EnableKeyword(ConcaveKeyword);

                    if (_concaveShared.HasProperty(CullProperty))
                        _concaveShared.SetFloat(CullProperty, (float)UnityEngine.Rendering.CullMode.Off);

                    WoodTuning.Current.ApplyToBlock(_concaveShared);
                    WoodGrainNoise.ApplyTo(_concaveShared);
                }
                return _concaveShared;
            }
        }
    }
}
