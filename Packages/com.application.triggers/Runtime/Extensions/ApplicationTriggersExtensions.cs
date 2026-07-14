#if R3
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Infrastructure.ApplicationTriggers.Interfaces;
using R3;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Infrastructure.ApplicationTriggers
{
    public static class ApplicationTriggersExtensions
    {
        public static Observable<bool> HasFocusAsObservable(this IApplicationFocusTrigger trigger)
        {
            if (trigger == null)
                throw new ArgumentNullException(nameof(trigger));

            var fromApplicationEvent = Observable
                .FromEvent<bool>(
                    h => Application.focusChanged += h,
                    h => Application.focusChanged -= h);

            var fromTriggerEvent = Observable
                .FromEvent<bool>(
                    h => trigger.FocusChanged += h,
                    h => trigger.FocusChanged -= h);

            return fromApplicationEvent
                .Merge(fromTriggerEvent)
                .DebounceFrame(1);
        }

        public static ReadOnlyReactiveProperty<bool> HasFocusAsReactiveProperty(this IApplicationFocusTrigger trigger)
        {
            if (trigger == null)
                throw new ArgumentNullException(nameof(trigger));

            return trigger
                .HasFocusAsObservable()
                .ToReadOnlyReactiveProperty(trigger.HasFocus);
        }

        public static async UniTask WaitFocusAsync(
            this IApplicationFocusTrigger trigger,
            float cancelAfterInSeconds = 5,
            CancellationToken cancellationToken = default)
        {
            using (var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(cancelAfterInSeconds));

                await trigger
                    .HasFocusAsObservable()
                    .Where(isFocused => isFocused == false)
                    .FirstAsync(cancellationToken: cancellationTokenSource.Token);
            }

            await trigger
                .HasFocusAsObservable()
                .Where(isFocused => isFocused)
                .FirstAsync(cancellationToken: cancellationToken);
        }

        public static Observable<bool> HasPauseAsObservable(this IApplicationPauseTrigger trigger)
        {
            if (trigger == null)
                throw new ArgumentNullException(nameof(trigger));

#if UNITY_EDITOR
            return Observable
                .FromEvent<bool>(
                    h => EditorApplication.focusChanged += h,
                    h => EditorApplication.focusChanged -= h)
                .Select(isFocused => !isFocused);
#else
            return Observable
                .FromEvent<bool>(
                    h => trigger.PauseChanged += h,
                    h => trigger.PauseChanged -= h);
#endif
        }

        public static ReadOnlyReactiveProperty<bool> HasPauseAsReactiveProperty(this IApplicationPauseTrigger trigger)
        {
            if (trigger == null)
                throw new ArgumentNullException(nameof(trigger));

            return trigger
                .HasPauseAsObservable()
                .ToReadOnlyReactiveProperty(trigger.HasPause);
        }

        public static async UniTask WaitPauseAsync(
            this IApplicationPauseTrigger trigger,
            float cancelAfterInSeconds = 5,
            CancellationToken cancellationToken = default)
        {
            using (var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(cancelAfterInSeconds));

                await trigger
                    .HasPauseAsObservable()
                    .Where(isPaused => isPaused)
                    .FirstAsync(cancellationToken: cancellationTokenSource.Token);
            }

            await trigger
                .HasPauseAsObservable()
                .Where(isPaused => isPaused == false)
                .FirstAsync(cancellationToken: cancellationToken);
        }

        public static Observable<Unit> QuitAsObservable(this IApplicationQuitTrigger trigger)
        {
            if (trigger == null)
                throw new ArgumentNullException(nameof(trigger));
            
            var quittingObservable = Observable
                .FromEvent(
                    h => trigger.IsQuitting += h,
                    h => trigger.IsQuitting -= h);

#if UNITY_EDITOR
            var editorPlayModeObservable = Observable
                .FromEvent<PlayModeStateChange>
                (
                    h => EditorApplication.playModeStateChanged += h,
                    h => EditorApplication.playModeStateChanged -= h
                )
                .Where(state => state == PlayModeStateChange.ExitingPlayMode)
                .AsUnitObservable();
#endif

#if UNITY_EDITOR
            return quittingObservable
                .Merge(editorPlayModeObservable)
                .DebounceFrame(1);
#else
            return quittingObservable;
#endif
        }
    }
}
#endif