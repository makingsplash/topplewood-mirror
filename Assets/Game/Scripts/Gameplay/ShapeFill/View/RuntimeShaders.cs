using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// TASK-554: единственный вход к шейдерам, из которых рантайм собирает материалы кодом (<c>new Material(shader)</c>).
    ///
    /// Почему ассет, а не <c>Shader.Find</c>: строковый поиск — НЕ ссылка, сборка про него не знает. Шейдер, на
    /// который не ссылается ни один ассет, в плеер не попадает, <c>Shader.Find</c> там возвращает null, и цепочка
    /// фолбэков тихо рисует плоский Unlit. Так ни одна Android-сборка не показала дерево на деталях
    /// (<c>docs/platform/unity-gotchas.md</c> → «<c>Shader.Find</c> в плеере…»). Здесь шейдеры лежат СЕРИАЛИЗОВАННЫМИ
    /// ссылками в ассете под <c>Resources</c>, а всё из <c>Resources</c> входит в плеер вместе с зависимостями —
    /// включение гарантировано построением, без списка в настройках графики.
    ///
    /// Промах громкий: нет ассета или ссылки — одна ошибка в лог на шейдер, дальше вызывающий идёт по своему фолбэку.
    /// Новый шейдер для рантайма = новое поле здесь + строка в <see cref="Entries"/>; голый <c>Shader.Find</c> в
    /// рантайм-коде роняет EditMode-тест и сборку (<c>RuntimeShaderGuard</c>).
    /// </summary>
    public sealed class RuntimeShaders : ScriptableObject
    {
        /// <summary>Путь ассета для <c>Resources.Load</c>: <c>Assets/Resources/Rendering/RuntimeShaders.asset</c>.</summary>
        public const string ResourcePath = "Rendering/RuntimeShaders";

        public const string WoodBlockName = "Game/Wood Block";
        public const string WoodPieceShadowName = "Game/Wood Piece Shadow";
        public const string UrpUnlitName = "Universal Render Pipeline/Unlit";
        public const string SpritesDefaultName = "Sprites/Default";
        public const string IdleDashOutlineName = "Game/Idle Dash Outline";

        [SerializeField] private Shader woodBlock;
        [SerializeField] private Shader woodPieceShadow;
        [SerializeField] private Shader urpUnlit;
        [SerializeField] private Shader spritesDefault;
        [SerializeField] private Shader idleDashOutline;

        private static RuntimeShaders _instance;
        private static bool _loaded;
        private static readonly HashSet<string> Reported = new HashSet<string>();

        public static Shader WoodBlock => Resolve(Instance == null ? null : Instance.woodBlock, WoodBlockName);
        public static Shader WoodPieceShadow => Resolve(Instance == null ? null : Instance.woodPieceShadow, WoodPieceShadowName);
        public static Shader UrpUnlit => Resolve(Instance == null ? null : Instance.urpUnlit, UrpUnlitName);
        public static Shader SpritesDefault => Resolve(Instance == null ? null : Instance.spritesDefault, SpritesDefaultName);
        public static Shader IdleDashOutline => Resolve(Instance == null ? null : Instance.idleDashOutline, IdleDashOutlineName);

        /// <summary>Пары «ожидаемое имя шейдера → сериализованная ссылка»: по ним сборка и тест сверяют ассет.</summary>
        public IEnumerable<KeyValuePair<string, Shader>> Entries()
        {
            yield return new KeyValuePair<string, Shader>(WoodBlockName, woodBlock);
            yield return new KeyValuePair<string, Shader>(WoodPieceShadowName, woodPieceShadow);
            yield return new KeyValuePair<string, Shader>(UrpUnlitName, urpUnlit);
            yield return new KeyValuePair<string, Shader>(SpritesDefaultName, spritesDefault);
            yield return new KeyValuePair<string, Shader>(IdleDashOutlineName, idleDashOutline);
        }

        private static RuntimeShaders Instance
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _instance = Resources.Load<RuntimeShaders>(ResourcePath);
                }
                return _instance;
            }
        }

        /// <summary>
        /// Сериализованная ссылка, если она есть; иначе ОДНА ошибка в лог и последняя попытка строкой — в редакторе она
        /// найдёт шейдер, в плеере скорее всего нет, и вызывающий уйдёт в свой фолбэк уже не молча. Возвращает
        /// настоящий C#-null (не «уничтоженный» Unity-объект), чтобы цепочки фолбэков вызывающих работали честно.
        /// </summary>
        private static Shader Resolve(Shader serialized, string name)
        {
            if (serialized != null)
                return serialized;

            if (Reported.Add(name))
                Debug.LogError(Instance == null
                    ? $"[RuntimeShaders] нет ассета Resources/{ResourcePath} — шейдер '{name}' может не попасть в сборку, материал уйдёт в фолбэк (без дерева/тени)."
                    : $"[RuntimeShaders] в ассете Resources/{ResourcePath} пустая ссылка на '{name}' — материал уйдёт в фолбэк (без дерева/тени).");

            var found = Shader.Find(name);
            return found != null ? found : null;
        }
    }
}
