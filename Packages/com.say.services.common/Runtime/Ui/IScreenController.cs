using System;
using UnityEngine;

namespace SGS.Services.Ui
{
    public interface IScreenController
    {
        event Action OnClose;
        GameObject GameObject { get; }

        void Init();
        void DeInit();
    }
}