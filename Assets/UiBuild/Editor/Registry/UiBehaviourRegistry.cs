using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Iliai.UiBuild
{
    /// <summary>Метка хэндлера: `[UiHandler("Button")]` → тип блока `behaviour` в спеке.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class UiHandlerAttribute : Attribute
    {
        public string Type { get; }
        public UiHandlerAttribute(string type) { Type = type; }
    }

    /// <summary>
    /// Хэндлер блока `behaviour`. Проект добавляет свои, реализовав этот интерфейс в ЛЮБОЙ
    /// Editor-сборке — реестр найдёт их сам, править ядро не нужно. Именно этот шов делает
    /// компилятор переносимым: всё, что зависит от фреймворка проекта, живёт снаружи.
    /// </summary>
    public interface IUiBehaviourHandler
    {
        /// <summary>Компоненты, которыми владеет этот хэндлер. Если блок ушёл из спеки, они
        /// снимаются при пересборке — иначе на префабе остаётся мусор от прошлой версии вёрстки.</summary>
        IEnumerable<Type> ManagedTypes { get; }

        void Apply(UiBuildContext ctx, GameObject target, UiBehaviourSpec spec);
    }

    /// <summary>Шаг после записи префаба: регистрация в загрузчике ассетов, генерация констант,
    /// что угодно проектное. Ядро само ничего такого не знает и знать не должно.</summary>
    public interface IUiPostBuildStep
    {
        int Order { get; }
        void Run(UiBuildContext ctx, UiSpec spec, GameObject prefabRoot, string prefabPath);
    }

    public static class UiBehaviourRegistry
    {
        static Dictionary<string, IUiBehaviourHandler> _handlers;
        static List<IUiPostBuildStep> _steps;
        static HashSet<Type> _managed;

        public static void Invalidate()
        {
            _handlers = null;
            _steps = null;
            _managed = null;
        }

        public static IReadOnlyDictionary<string, IUiBehaviourHandler> Handlers
        {
            get { EnsureBuilt(); return _handlers; }
        }

        public static IReadOnlyList<IUiPostBuildStep> PostBuildSteps
        {
            get { EnsureBuilt(); return _steps; }
        }

        /// <summary>Все типы, которыми владеют хэндлеры — множество для снятия stale-компонентов.</summary>
        public static IReadOnlyCollection<Type> ManagedTypes
        {
            get { EnsureBuilt(); return _managed; }
        }

        public static IUiBehaviourHandler Find(string type)
        {
            EnsureBuilt();
            return type != null && _handlers.TryGetValue(type, out IUiBehaviourHandler h) ? h : null;
        }

        public static string KnownList()
        {
            EnsureBuilt();
            var keys = new List<string>(_handlers.Keys);
            keys.Sort();
            return string.Join(", ", keys);
        }

        static void EnsureBuilt()
        {
            if (_handlers != null) return;
            _handlers = new Dictionary<string, IUiBehaviourHandler>(StringComparer.Ordinal);
            _steps = new List<IUiPostBuildStep>();
            _managed = new HashSet<Type>();

            foreach (Type t in TypeCache.GetTypesDerivedFrom<IUiBehaviourHandler>())
            {
                if (t.IsAbstract || t.IsInterface) continue;
                var attr = (UiHandlerAttribute)Attribute.GetCustomAttribute(t, typeof(UiHandlerAttribute));
                if (attr == null)
                {
                    Debug.LogWarning($"[uibuild] {t.FullName} реализует IUiBehaviourHandler, но без атрибута [UiHandler(\"тип\")] — пропущен.");
                    continue;
                }
                IUiBehaviourHandler instance;
                try { instance = (IUiBehaviourHandler)Activator.CreateInstance(t); }
                catch (Exception e)
                {
                    Debug.LogError($"[uibuild] хэндлер {t.FullName} не создался: {e.Message}");
                    continue;
                }
                if (_handlers.TryGetValue(attr.Type, out IUiBehaviourHandler existing))
                {
                    // Два хэндлера на один тип — реальная неоднозначность: молча взять любой означает
                    // сборку, зависящую от порядка загрузки сборок.
                    Debug.LogError($"[uibuild] тип behaviour '{attr.Type}' объявлен дважды: {existing.GetType().FullName} и {t.FullName}. Оставил первый.");
                    continue;
                }
                _handlers[attr.Type] = instance;
                if (instance.ManagedTypes == null) continue;
                foreach (Type m in instance.ManagedTypes) if (m != null) _managed.Add(m);
            }

            foreach (Type t in TypeCache.GetTypesDerivedFrom<IUiPostBuildStep>())
            {
                if (t.IsAbstract || t.IsInterface) continue;
                try { _steps.Add((IUiPostBuildStep)Activator.CreateInstance(t)); }
                catch (Exception e) { Debug.LogError($"[uibuild] post-build шаг {t.FullName} не создался: {e.Message}"); }
            }
            _steps.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }
}
