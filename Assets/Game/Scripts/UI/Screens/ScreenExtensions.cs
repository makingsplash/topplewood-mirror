using Cysharp.Threading.Tasks;
using R3;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Screens
{
    [MovedFrom(sourceNamespace: "UI.Screens")]
    public static class ScreenExtensions
    {
        public static UniTask WaitCloseAsync(this BaseScreen screen)
        {
            return Observable
                .FromEvent(action => screen.OnClose += action, action => screen.OnClose -= action)
                .FirstAsync()
                .AsUniTask();
        }
    }
}
