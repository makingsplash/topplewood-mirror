using System;

namespace Game.Services.Input
{
    [Flags]
    public enum InputActionTypeFlags
    {
        None = 0,
        Click = 1 << 0,
        PointerDrag = 1 << 1,
        MultiTouch = 1 << 2,
        Pointer0Hold = 1 << 3,
        
        All = Click | PointerDrag | MultiTouch | Pointer0Hold
    }
}
