using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using VContainer.Unity;

namespace VContainer
{
    public static class VContainerExtensions
    {
        public static IObjectResolver CreateContainer(Action<IContainerBuilder> buildAction)
        {
            if (buildAction == null)
                throw new ArgumentNullException(nameof(buildAction));

            var builder = new ContainerBuilder();
            buildAction.Invoke(builder);
            return builder.Build();
        }

        public static IObjectResolver CreateContainer(this IInstaller installer)
        {
            if (installer is null)
                throw new ArgumentNullException(nameof(installer));

            var builder = new ContainerBuilder();
            installer.Install(builder);
            return builder.Build();
        }

        public static IObjectResolver CreateContainer(this IEnumerable<IInstaller> installers)
        {
            if (installers is null)
                throw new ArgumentNullException(nameof(installers));

            var builder = new ContainerBuilder();

            foreach (var installer in installers)
            {
                installer.Install(builder);
            }

            return builder.Build();
        }

        public static T Instantiate<T>(this IObjectResolver resolver, Lifetime lifetime = Lifetime.Singleton)
        {
            var registrationBuilder = new RegistrationBuilder(typeof(T), lifetime);
            var registration = registrationBuilder.Build();
            return (T)resolver.Resolve(registration);
        }

        public static T Instantiate<T>(this IObjectResolver resolver, Lifetime lifetime = Lifetime.Singleton, params object[] args)
        {
            var registrationBuilder = new RegistrationBuilder(typeof(T), lifetime);

            foreach (var arg in args)
                registrationBuilder.WithParameter(arg.GetType(), arg);

            var registration = registrationBuilder.Build();
            return (T)resolver.Resolve(registration);
        }

        public static GameObject InstantiateAndInject([NotNull] this IObjectResolver resolver, [NotNull] GameObject prefab, Transform parent = null)
        {
            if (prefab == null)
                throw new NullReferenceException(nameof(prefab));

            var prefabWasActive = prefab.activeSelf;
            prefab.SetActive(false);
            var instance = resolver.Instantiate(prefab, parent);
            prefab.SetActive(prefabWasActive);
            instance.SetActive(prefabWasActive);
            return instance;
        }

        public static RegistrationBuilder RegisterNonLazy<T>(this IContainerBuilder builder, Lifetime lifetime = Lifetime.Singleton)
        {
            return RegisterNonLazy<T>(builder, null, lifetime);
        }

        public static RegistrationBuilder RegisterNonLazy<T>(this IContainerBuilder builder, Action<T> executeAfterResolving,
            Lifetime lifetime = Lifetime.Singleton)
        {
            var registrationBuilder = builder.Register<T>(lifetime);

            builder.RegisterBuildCallback(container =>
            {
                var result = container.Resolve<T>();
                executeAfterResolving?.Invoke(result);
            });
            return registrationBuilder;
        }
        
        public static RegistrationBuilder RegisterNonLazy<TInterface, TImplement>(this IContainerBuilder builder, Lifetime lifetime = Lifetime.Singleton) 
            where TImplement : TInterface
        {
            return RegisterNonLazy<TInterface, TImplement>(builder, null, lifetime);
        }
        
        public static RegistrationBuilder RegisterNonLazy<TInterface, TImplement>(
            this IContainerBuilder builder,
            Action<TInterface> executeAfterResolving,
            Lifetime lifetime = Lifetime.Singleton) 
            where TImplement : TInterface
        {
            var registrationBuilder = builder.Register<TInterface, TImplement>(lifetime);

            builder.RegisterBuildCallback(container =>
            {
                var result = container.Resolve<TInterface>();
                executeAfterResolving?.Invoke(result);
            });
            return registrationBuilder;
        }
        
        public static ComponentRegistrationBuilder RegisterComponentInNewPrefabNonLazy<T>(
            this IContainerBuilder builder,
            T prefab,
            Lifetime lifetime = Lifetime.Singleton) where T : Component
        {
            return RegisterComponentInNewPrefabNonLazy(builder, prefab, null, lifetime);
        }
        
        public static ComponentRegistrationBuilder RegisterComponentInNewPrefabNonLazy<T>(
            this IContainerBuilder builder,
            T prefab,
            Action<T> executeAfterResolving,
            Lifetime lifetime = Lifetime.Singleton) where T : Component
        {
            var registrationBuilder = builder
                .RegisterComponentInNewPrefab(prefab, lifetime);

            builder.RegisterBuildCallback(resolver =>
            {
                var component = resolver.Resolve<T>();
                executeAfterResolving?.Invoke(component);
            });
            return registrationBuilder;
        }

        public static RegistrationBuilder RegisterEntryPoint(
            this IContainerBuilder builder,
            Type type,
            Lifetime lifetime = Lifetime.Singleton)
        {
            EntryPointsBuilder.EnsureDispatcherRegistered(builder);
            return builder.Register(type, lifetime).AsImplementedInterfaces();
        }

        public static RegistrationBuilder RegisterFactory<T>(this IContainerBuilder builder, Lifetime lifetime = Lifetime.Singleton) where T : class
        {
            return builder.RegisterFactory<T>(resolver => () => resolver.Instantiate<T>(), lifetime);
        }
        
        public static IDisposable TryGetValue<TObject, TScope>(this ResolvedObject<TObject, TScope> resolvedObject, out TObject value)
            where TObject : class 
            where TScope : LifetimeScope
        {
            value = resolvedObject.Value;
            return resolvedObject;
        }
    }
}