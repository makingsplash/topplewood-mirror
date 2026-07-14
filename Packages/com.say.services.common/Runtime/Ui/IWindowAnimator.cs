using System;

namespace SGS.Services.Ui
{
    public interface IWindowAnimator
    {
        void Show(Action onFinished);
        void Hide(Action onFinished);
    }
}