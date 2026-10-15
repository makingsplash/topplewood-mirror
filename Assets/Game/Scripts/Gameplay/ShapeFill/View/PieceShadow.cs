using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Мягкая тень под деталью: копия того же меша, сдвинутая и размытая шейдером
    /// <c>Game/Wood Piece Shadow</c>. Владелец просил объём («тени нужны за деталями»).
    ///
    /// Тень — ДОЧЕРНИЙ объект детали, поэтому кувыркается вместе с ней, но смещение обязано
    /// оставаться мировым (свет один на всю сцену). Поэтому его каждый кадр переводят в локальные
    /// координаты через <see cref="LocalOffset"/> — иначе у повёрнутой детали тень уезжала бы вбок
    /// или вверх, и фигура «висела» бы в воздухе.
    ///
    /// Живые тени помнятся списком (<see cref="RefreshLiveShadows"/>): масштаб задаётся один раз при
    /// создании, а владелец крутит его ползунком в Play — без списка новый масштаб доехал бы только
    /// до деталей, созданных ПОСЛЕ правки.
    /// </summary>
    public static class PieceShadow
    {
        /// <summary>Локальный сдвиг по Z: дальше от камеры, чем деталь (камера смотрит вдоль +Z).
        /// Прежнее поведение — тень между деталью и статикой, то есть ПОВЕРХ пола.</summary>
        private const float ZBehind = 0.15f;

        /// <summary>Дальше СТАТИКИ (та стоит на 0.5, см. StaticGeometryView.ZBehindPieces): непрозрачный
        /// пол пишет глубину и сам срезает тень, легшую на него. Так тень остаётся только на стене.</summary>
        private const float ZBehindStatics = 0.9f;

        private static readonly int SoftId = Shader.PropertyToID("_Soft");

        private static Material _shared;
        private static Material _silhouetteShared;

        // Обычные (не слабые) ссылки: уничтоженный Unity-объект сравнивается с null, по этому
        // признаку запись и выметается в RefreshLiveShadows. Список короткий — детали на экране.
        private static readonly List<Transform> LiveShadows = new List<Transform>(64);

        public static Material Shared
        {
            get
            {
                if (_shared == null)
                {
                    // Сериализованная ссылка из RuntimeShaders (TASK-554): строкой шейдер в плеер не попадал,
                    // и на устройстве теней не было вовсе. Промах реестр пишет ошибкой — деталь без тени.
                    var shader = RuntimeShaders.WoodPieceShadow;
                    if (shader == null)
                        return null;
                    _shared = new Material(shader) { name = "PieceShadowMaterial" };
                    _shared.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

                    // Сохранённые числа стиля — сразу, как и у материала деталей. ApplyToShadow,
                    // а не Apply: Apply сам берёт Shared, вышло бы кольцо.
                    WoodTuning.Current.ApplyToShadow(_shared);
                }
                return _shared;
            }
        }

        /// <summary>
        /// TASK-368: тень составной freeform-детали — тот же шейдер с кейвордом <c>_CONCAVE_SDF</c>: размытие края
        /// считается по СИЛУЭТУ, а не по <c>uv2.x</c> меша. Иначе стык частей (его uv2.x = 0, то есть альфа 0)
        /// прорезал бы тень прозрачной щелью. Контур — per-renderer в MaterialPropertyBlock (<see cref="Attach(Transform, Mesh, Vector4[], int)"/>).
        /// </summary>
        public static Material SilhouetteShared
        {
            get
            {
                if (_silhouetteShared == null)
                {
                    var shader = RuntimeShaders.WoodPieceShadow;
                    if (shader == null)
                        return null;
                    _silhouetteShared = new Material(shader) { name = "PieceShadowSilhouetteMaterial" };
                    _silhouetteShared.EnableKeyword(BlockMaterial.ConcaveKeyword);
                    _silhouetteShared.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    WoodTuning.Current.ApplyToShadow(_silhouetteShared);
                }
                return _silhouetteShared;
            }
        }

        /// <summary>
        /// Вешает тень на деталь. Возвращает трансформ тени (или null, если шейдера нет) —
        /// звать <see cref="LocalOffset"/> при каждом изменении позы.
        /// </summary>
        public static Transform Attach(Transform owner, Mesh mesh) => Attach(owner, mesh, null, 0);

        /// <summary>
        /// То же, но с силуэтом (TASK-368): при <paramref name="silhouette"/> ≠ null тень берёт
        /// <see cref="SilhouetteShared"/> и получает упакованный контур (<see cref="BlockMaterial.PackSilhouette"/>) в свой
        /// MaterialPropertyBlock. Кладётся один раз: <see cref="ApplySoft"/> блок дописывает, а не очищает.
        /// </summary>
        public static Transform Attach(Transform owner, Mesh mesh, Vector4[] silhouette, int silhouetteCount)
            => Attach(owner, mesh, silhouette, silhouetteCount, null);

        /// <summary>
        /// TASK-567 (долг TASK-472): тень МАССЫ статики по запечённому полю силуэта. Шейдер тени под <c>_CONCAVE_SDF</c>
        /// с полем берёт ЗНАКОВОЕ расстояние (снаружи силуэта альфа 0), поэтому меш тени может быть квадом габарита —
        /// а у слитой массы и обязан им быть: веера членов перекрываются, и полупрозрачная тень потемнела бы дважды.
        /// Без этого пути вогнутая масса оставалась без тени: её uv2.x = 0, то есть альфа 0 по всей площади.
        /// </summary>
        public static Transform Attach(Transform owner, Mesh mesh, SilhouetteField field)
            => Attach(owner, mesh, null, 0, field);

        /// <summary>Общий путь: контур (цикл по рёбрам) и/или поле. Есть хоть одно — материал силуэта; поле кладётся
        /// ДО <see cref="ApplySoft"/> (тот блок дописывает, а не очищает).</summary>
        public static Transform Attach(Transform owner, Mesh mesh, Vector4[] silhouette, int silhouetteCount,
            SilhouetteField field)
        {
            var material = silhouette != null || field != null ? SilhouetteShared : Shared;
            if (material == null || owner == null || mesh == null)
                return null;

            var go = new GameObject("Shadow");
            var t = go.transform;
            t.SetParent(owner, false);
            t.localScale = Vector3.one * WoodTuning.Current.ShadowScale;
            t.localPosition = LocalOffset(0f, mesh);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (silhouette != null || field != null)
            {
                var mpb = new MaterialPropertyBlock();
                if (silhouette != null)
                    BlockMaterial.SetSilhouette(mpb, silhouette, silhouetteCount, field);
                else
                    field.Apply(mpb);
                renderer.SetPropertyBlock(mpb);
            }
            ApplySoft(renderer, mesh);

            LiveShadows.Add(t);
            return t;
        }

        /// <summary>Перебрать живые тени и поставить им текущий масштаб (владелец крутит его в Play).</summary>
        public static void RefreshLiveShadows()
        {
            var scale = Vector3.one * WoodTuning.Current.ShadowScale;
            for (var i = LiveShadows.Count - 1; i >= 0; i--)
            {
                var t = LiveShadows[i];
                if (t == null)
                {
                    LiveShadows.RemoveAt(i);
                    continue;
                }
                t.localScale = scale;

                // Глубину тоже: тумблер «тень только на стене» крутят в Play, а у статики поза
                // ставится один раз при создании и сама не пересчитается.
                var p = t.localPosition;
                p.z = WoodTuning.Current.ShadowOnlyOnWall ? ZBehindStatics : ZBehind;
                t.localPosition = p;

                // Мягкость тоже перекладываем: она зависит от размера детали, а деталь может
                // пересобраться под новый радиус скругления прямо под ползунком.
                var filter = t.GetComponent<MeshFilter>();
                ApplySoft(t.GetComponent<MeshRenderer>(), filter == null ? null : filter.sharedMesh);
            }
        }

        /// <summary>
        /// Мягкость края — в МИРОВЫХ единицах, поэтому её ужимаем под размер детали ровно так же, как
        /// фаску: на детали 0.3 мировые 0.055 размывают тень на 18% силуэта, и вместо тени выходит
        /// грязное пятно шире самой детали. Через MaterialPropertyBlock, а не материалом: материал
        /// у всех теней общий, а множитель у каждой свой.
        /// </summary>
        private static void ApplySoft(Renderer renderer, Mesh mesh)
        {
            if (renderer == null)
                return;
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);
            mpb.SetFloat(SoftId, WoodTuning.Current.ShadowSoft * SizeFactor(mesh));
            renderer.SetPropertyBlock(mpb);
        }

        /// <summary>Множитель толщин для детали этого меша — по короткой стороне её габарита.</summary>
        private static float SizeFactor(Mesh mesh)
        {
            if (mesh == null)
                return 1f;
            var size = mesh.bounds.size;
            return WoodTuning.Current.DetailFactor(Mathf.Max(Mathf.Min(size.x, size.y), 1e-4f));
        }

        /// <summary>
        /// Мировое смещение тени, пересчитанное в локальные координаты повёрнутой детали.
        /// Плюс поправка на увеличение: дочерний объект масштабируется вокруг НУЛЯ детали, а не
        /// вокруг центра её меша, и без поправки тень уезжала бы у деталей со смещённым контуром
        /// (статика собрана в мировых координатах — там смещение равно всей позиции блока).
        /// </summary>
        public static Vector3 LocalOffset(float rotationRadians, Mesh mesh)
        {
            var tuning = WoodTuning.Current;

            // Сдвиг — в масштабе детали: мировые −0.131 на детали 0.3 отодвигают тень на 44% её
            // силуэта, и она читается отдельной чёрной плашкой, а не тенью под деталью.
            var k = SizeFactor(mesh);
            var offset = Quaternion.Euler(0f, 0f, -rotationRadians * Mathf.Rad2Deg)
                         * new Vector3(tuning.ShadowOffsetX * k, tuning.ShadowOffsetY * k, 0f);
            if (mesh != null)
            {
                var center = mesh.bounds.center;
                offset += (1f - tuning.ShadowScale) * new Vector3(center.x, center.y, 0f);
            }
            offset.z = tuning.ShadowOnlyOnWall ? ZBehindStatics : ZBehind;
            return offset;
        }
    }
}
