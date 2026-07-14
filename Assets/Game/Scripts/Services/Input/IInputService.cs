using R3;
using UnityEngine;

namespace Game.Services.Input
{
    public interface IInputService
    {
        Observable<Vector2> OnClickedAsObservable();
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
