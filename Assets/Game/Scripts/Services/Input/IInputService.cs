using R3;
using UnityEngine;

namespace Game.Services.Input
{
    public interface IInputService
    {
        Observable<Vector2> OnClickedAsObservable();

        // TASK-114/TASK-374: the SECOND finger's tap, needed for the co-release gesture (two pieces tapped together).
        // Pointer 0's OnClicked could not carry it — a touch device aliases Pointer.current to the primary touch, so
        // finger 2 never reached gameplay. A separate stream keeps the pointer-0 contract byte-for-byte for every
        // other consumer while the gameplay adapter merges both.
        Observable<Vector2> OnPointer1ClickedAsObservable();

        Observable<Vector2> OnPointerDragAsObservable();

        Observable<Vector2> OnMultiTouchAsObservable();

        Observable<bool> IsMultiTouchIsActiveAsObservable();

        Observable<Vector2> OnPointer0HoldStartedAsObservable();
        Observable<Vector2> OnPointer0HoldEndedAsObservable();

        bool TryConsumeBufferedGameplayClick(out Vector2 clickPosition);
        void SetGameplayClickBufferingEnabled(bool isEnabled);
        void ClearBufferedGameplayClick();

        void Enable();

        void Disable();
    }
}
