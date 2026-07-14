using System;
using UnityEngine;

namespace SGS.Services.Ui
{
    public class AnimationMarkerTrigger : MonoBehaviour
    {
        public event Action Triggered;
        public event Action<string> TriggeredWithId;

        // used by anim to trigger
        public void Trigger()
        {
            Triggered?.Invoke();
        }

        public void TriggerWithId(string id)
        {
            TriggeredWithId?.Invoke(id);
        }
    }
}