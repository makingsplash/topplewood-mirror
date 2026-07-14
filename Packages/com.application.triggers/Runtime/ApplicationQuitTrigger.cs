using System;
using Infrastructure.ApplicationTriggers.Interfaces;
using UnityEngine;

namespace Infrastructure.ApplicationTriggers
{
    public class ApplicationQuitTrigger : IApplicationQuitTrigger
    {
        public event Action IsQuitting
        {
            add => Application.quitting += value;
            remove => Application.quitting -= value;
        }

        public event Func<bool> WantsToQuit
        {
            add => Application.wantsToQuit += value;
            remove => Application.wantsToQuit -= value;
        }
    }
}