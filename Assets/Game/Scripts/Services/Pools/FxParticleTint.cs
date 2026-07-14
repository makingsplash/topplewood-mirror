using UnityEngine;

namespace Services.Pools
{
    /// <summary>
    /// Перекраска выбранных партикл-систем FX в цвет собранных тайлов (продолжение цветовой волны).
    /// Вешается на корень FX-префаба; системы вне списка (вспышка, кольцо) остаются белыми.
    /// Цвет тайла не используется напрямую: он снапится к ближайшему по оттенку цвету
    /// из чистой палитры — иначе приглушённые цвета тайлов дают «грязные» частицы.
    /// Пул переиспользует инстанс, поэтому цвет задаётся на каждом спавне заново.
    /// </summary>
    public sealed class FxParticleTint : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] _tintedSystems;

        [Tooltip("Чистые «светящиеся» цвета для частиц. Цвет тайла заменяется ближайшим по оттенку из этого списка")]
        [SerializeField] private Color[] _cleanPalette =
        {
            new Color(1f, 0.30f, 0.25f),  // красный
            new Color(1f, 0.62f, 0.25f),  // оранжевый
            new Color(1f, 0.92f, 0.30f),  // жёлтый
            new Color(0.75f, 1f, 0.30f),  // лайм
            new Color(0.40f, 1f, 0.45f),  // зелёный
            new Color(0.40f, 1f, 0.78f),  // мятный
            new Color(0.40f, 0.95f, 1f),  // голубой
            new Color(0.40f, 0.68f, 1f),  // синий
            new Color(0.58f, 0.52f, 1f),  // индиго
            new Color(0.80f, 0.50f, 1f),  // фиолетовый
            new Color(1f, 0.48f, 0.95f),  // фуксия
            new Color(1f, 0.52f, 0.70f),  // розовый
        };

        [Tooltip("Тайлы почти без цвета (белые/серые/чёрные) дают белые частицы, если насыщенность ниже порога")]
        [SerializeField, Range(0f, 0.5f)] private float _neutralSaturationThreshold = 0.15f;

        public void Apply(Color color)
        {
            ApplyResolved(ResolveCleanColor(color));
        }

        /// <summary>
        /// Точная покраска без снапа к палитре — для нейтральных цветов (серый камень, пыль),
        /// которые палитра считает «бесцветными» и заменила бы белым.
        /// </summary>
        public void ApplyExact(Color color)
        {
            ApplyResolved(color);
        }

        private void ApplyResolved(Color particleColor)
        {
            if (_tintedSystems == null)
            {
                return;
            }

            for (var i = 0; i < _tintedSystems.Length; i++)
            {
                var system = _tintedSystems[i];
                if (system == null)
                {
                    continue;
                }

                var main = system.main;
                main.startColor = new ParticleSystem.MinMaxGradient(particleColor);

                // Burst на t=0 мог уже отыграть до применения цвета — перезапускаем систему
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Play(false);
            }
        }

        private Color ResolveCleanColor(Color tileColor)
        {
            Color.RGBToHSV(tileColor, out var hue, out var saturation, out _);

            if (saturation < _neutralSaturationThreshold)
            {
                return Color.white;
            }

            if (_cleanPalette == null || _cleanPalette.Length == 0)
            {
                // Палитра не задана — хотя бы поднимаем яркость исходного цвета
                return Color.HSVToRGB(hue, saturation, 1f);
            }

            var nearest = _cleanPalette[0];
            var nearestDistance = float.MaxValue;
            for (var i = 0; i < _cleanPalette.Length; i++)
            {
                Color.RGBToHSV(_cleanPalette[i], out var paletteHue, out _, out _);
                var distance = HueDistance(hue, paletteHue);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = _cleanPalette[i];
                }
            }

            return nearest;
        }

        private static float HueDistance(float a, float b)
        {
            var delta = Mathf.Abs(a - b);
            return delta > 0.5f ? 1f - delta : delta;
        }
    }
}
