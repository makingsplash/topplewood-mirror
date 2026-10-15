using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// TASK-557: шум волокна шейдера <c>Game/Wood Block</c> — ТАЙЛОВАЯ ТЕКСТУРА, а не процедура в пикселе.
    ///
    /// Зачем: волокно звало fbm шесть раз на пиксель (волна, разброс, шероховатость — и всё это дважды ради рельефа),
    /// то есть 72 хеша и 18 сглаженных интерполяций во fp32. Замер на телефоне владельца (Adreno 619, TASK-557
    /// 01-dev-implementer §2): одно волокно стоило 16–17 мс GPU из бюджета 16.7. Мобильные игры рисуют такой шум
    /// выборкой из текстуры — здесь fbm запечён целиком, и один вызов fbm в шейдере стал одной выборкой.
    ///
    /// Что сохранено от процедуры: тот же value-noise со сглаживанием <c>f·f·(3−2f)</c>, три октавы с весами
    /// 0.5/0.25/0.125, тот же диапазон [0, 0.875] — поэтому смещения <c>fbm − 0.5</c> в шейдере и числа досок
    /// (<see cref="WoodGrainVariant"/>) не трогались. Что изменилось: конкретная раскладка узлов (другой хеш) и
    /// шаг октав ровно 2 вместо 2.03 — иначе октавы не тайлятся. Рисунок волокна поэтому другой, характер тот же.
    ///
    /// Текстура генерируется кодом один раз на сессию (детерминированно, без ассета): параметры — константы ниже,
    /// масштаб выборки шейдер берёт из <see cref="ScaleId"/>, то есть из этого же класса.
    /// </summary>
    public static class WoodGrainNoise
    {
        public static readonly int TextureId = Shader.PropertyToID("_WoodNoiseTex");
        public static readonly int ScaleId = Shader.PropertyToID("_WoodNoiseScale");

        /// <summary>Сторона текстуры, текселей.</summary>
        public const int Size = 512;

        /// <summary>Период тайла в узлах ПЕРВОЙ октавы. 32 узла при 512 текселях — 16 текселей на ячейку первой
        /// октавы и 4 на ячейку третьей. Волна волокна (<c>along·0.75</c>) повторяется через ≈17 мировых единиц при
        /// масштабе рисунка 0.4 — шире экрана.</summary>
        public const int Period = 32;

        public const int Octaves = 3;

        private static Texture2D _texture;

        public static Texture2D Texture => _texture != null ? _texture : (_texture = Build());

        /// <summary>Положить шум в материал дерева. На чужом шейдере (откат на URP/Unlit) — no-op.</summary>
        public static void ApplyTo(Material material)
        {
            if (material == null || !material.HasProperty(TextureId))
                return;
            material.SetTexture(TextureId, Texture);
            material.SetFloat(ScaleId, 1f / Period);
        }

        /// <summary>fbm в центрах текселей, строка за строкой (индекс <c>y·size + x</c>). Тексель x хранит значение в
        /// точке <c>(x + 0.5)·period/size</c> — ровно там, куда билинейная выборка по <c>uv = p/period</c> кладёт центр.</summary>
        public static float[] Generate(int size, int period, int octaves)
        {
            var lattices = new float[octaves][];
            for (var o = 0; o < octaves; o++)
            {
                var n = period << o;
                var lattice = new float[n * n];
                for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                    lattice[y * n + x] = Hash(x, y, o);
                lattices[o] = lattice;
            }

            var values = new float[size * size];
            var step = (float)period / size;
            for (var ty = 0; ty < size; ty++)
            for (var tx = 0; tx < size; tx++)
            {
                float px = (tx + 0.5f) * step, py = (ty + 0.5f) * step;
                float sum = 0f, amp = 0.5f;
                for (var o = 0; o < octaves; o++)
                {
                    var n = period << o;
                    var lattice = lattices[o];
                    var ix = Mathf.FloorToInt(px);
                    var iy = Mathf.FloorToInt(py);
                    float fx = px - ix, fy = py - iy;
                    fx = fx * fx * (3f - 2f * fx);
                    fy = fy * fy * (3f - 2f * fy);
                    int x0 = ix % n, y0 = iy % n;
                    int x1 = (x0 + 1) % n, y1 = (y0 + 1) % n;
                    var a = lattice[y0 * n + x0];
                    var b = lattice[y0 * n + x1];
                    var c = lattice[y1 * n + x0];
                    var d = lattice[y1 * n + x1];
                    var v = Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
                    sum += v * amp;
                    amp *= 0.5f;
                    px *= 2f;
                    py *= 2f;
                }
                values[ty * size + tx] = sum;
            }
            return values;
        }

        /// <summary>Целочисленный хеш узла → [0, 1). Целый, а не float-хеш шейдера: текстура обязана выходить
        /// побайтно одинаковой на любом устройстве и в редакторе.</summary>
        private static float Hash(int x, int y, int octave)
        {
            unchecked
            {
                var h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)(octave + 1) * 0xCB1AB31Fu;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h >> 8) * (1f / 16777216f);
            }
        }

        private static Texture2D Build()
        {
            var values = Generate(Size, Period, Octaves);
            var bytes = new byte[values.Length];
            for (var i = 0; i < values.Length; i++)
                bytes[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(values[i] * 255f), 0, 255);

            var texture = new Texture2D(Size, Size, TextureFormat.R8, false, true)
            {
                name = "WoodGrainNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
                // Держится статикой и материалом, созданным кодом: без флага UnloadUnusedAssets при смене сцены
                // мог бы выгрузить текстуру из-под живого материала.
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            texture.SetPixelData(bytes, 0);
            texture.Apply(false, true);
            return texture;
        }
    }
}
