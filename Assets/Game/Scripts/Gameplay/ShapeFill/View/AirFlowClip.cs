using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Держит видимый поток зоны воздуха ВНУТРИ её области силы (TASK-448): частица, которая выходит
    /// за прямоугольник <c>region</c> или за полосу глубины потока, гасится вместе со своим хвостом.
    ///
    /// Зачем жёсткий срез, а не только расчёт. Долёт и раскрытие конуса считаются от геометрии зоны,
    /// но донорский поток идёт с включённым NoiseModule: шум уводит частицы с расчётной траектории,
    /// и по одной геометрии границу не удержать. Срез стоит ПОСЛЕ всех модулей, поэтому держит
    /// границу при любой их настройке — и при замене префаба тоже.
    ///
    /// Гасится частица, которая окажется снаружи на СЛЕДУЮЩЕМ кадре (позиция + скорость·dt с запасом).
    /// Иначе она успела бы один кадр нарисоваться за границей: сим частиц идёт до LateUpdate, а
    /// рендер после. Хвост у донора умирает вместе с частицей (dieWithParticles), поэтому весь
    /// пройденный путь, а с ним и хвост, остаётся внутри.
    ///
    /// Буфер выделяется один раз в <see cref="Bind"/>; в кадре аллокаций нет.
    /// </summary>
    public sealed class AirFlowClip : MonoBehaviour
    {
        /// <summary>На сколько кадров вперёд проверяется позиция. Больше единицы — запас на скачок dt.</summary>
        private const float LookaheadFrames = 1.5f;

        private ParticleSystem[] _systems;
        private ParticleSystem.Particle[] _buffer;
        private Vector2 _min;
        private Vector2 _max;
        private float _zMin;
        private float _zMax;

        /// <summary>
        /// <paramref name="min"/>/<paramref name="max"/> — область силы зоны в мировых координатах,
        /// <paramref name="zMin"/>/<paramref name="zMax"/> — полоса глубины, в которой поток обязан
        /// жить, чтобы оставаться ЗА статикой и деталями.
        /// </summary>
        public void Bind(ParticleSystem[] systems, Vector2 min, Vector2 max, float zMin, float zMax)
        {
            _systems = systems;
            _min = min;
            _max = max;
            _zMin = zMin;
            _zMax = zMax;

            var capacity = 0;
            foreach (var ps in systems)
                capacity = Mathf.Max(capacity, ps.main.maxParticles);
            _buffer = new ParticleSystem.Particle[capacity];
        }

        private void LateUpdate() => Apply(Time.deltaTime * LookaheadFrames);

        /// <summary>
        /// Один проход среза. Открыт для съёмки кадров: в редакторе время не течёт, LateUpdate не
        /// зовётся, и без явного вызова кадр приёмки показывал бы поток, которого в игре нет.
        /// </summary>
        /// <returns>Сколько частиц погашено — для диагностики съёмки.</returns>
        public int Apply(float lookahead)
        {
            if (_systems == null)
                return 0;

            var total = 0;
            foreach (var ps in _systems)
            {
                if (ps == null || ps.particleCount == 0)
                    continue;

                var n = ps.GetParticles(_buffer);
                var toWorld = ToWorld(ps);
                var widthScale = Mathf.Abs(ps.transform.lossyScale.x) * RibbonWidth(ps);
                var killed = 0;

                for (var i = 0; i < n; i++)
                {
                    var p = _buffer[i];
                    var pos = toWorld.MultiplyPoint3x4(p.position);
                    var next = pos + toWorld.MultiplyVector(p.totalVelocity) * lookahead;

                    // Полуширина ленты: хвост рисуется вокруг пути частицы, а не по самому пути.
                    var pad = p.GetCurrentSize(ps) * 0.5f * widthScale;
                    if (Inside(pos, pad) && Inside(next, pad))
                        continue;

                    p.remainingLifetime = -1f;
                    _buffer[i] = p;
                    killed++;
                }

                if (killed > 0)
                    ps.SetParticles(_buffer, n);
                total += killed;
            }

            return total;
        }

        /// <summary>
        /// Какая доля размера частицы видна поперёк. У донорского потока сама частица не рисуется
        /// (режим None) — видна только лента-трейл, а её ширина = размер × множитель ширины трейла.
        /// Отступ по полному размеру частицы съедал до 0.22 ед. с каждой стороны зоны шириной 1.1
        /// (mailbox): поток оставался только у вентилятора.
        /// </summary>
        private static float RibbonWidth(ParticleSystem ps)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var trails = ps.trails;
            if (renderer == null || renderer.renderMode != ParticleSystemRenderMode.None || !trails.enabled)
                return 1f;
            return trails.sizeAffectsWidth ? trails.widthOverTrailMultiplier : 0f;
        }

        private bool Inside(Vector3 p, float pad) =>
            p.x >= _min.x + pad && p.x <= _max.x - pad &&
            p.y >= _min.y + pad && p.y <= _max.y - pad &&
            p.z >= _zMin && p.z <= _zMax;

        /// <summary>GetParticles отдаёт позиции в пространстве СИМУЛЯЦИИ: у донорских систем оно
        /// Local, у запасного потока World.</summary>
        private static Matrix4x4 ToWorld(ParticleSystem ps)
        {
            var main = ps.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.World:
                    return Matrix4x4.identity;
                case ParticleSystemSimulationSpace.Custom:
                    return main.customSimulationSpace != null
                        ? main.customSimulationSpace.localToWorldMatrix
                        : Matrix4x4.identity;
                default:
                    return ps.transform.localToWorldMatrix;
            }
        }
    }
}
