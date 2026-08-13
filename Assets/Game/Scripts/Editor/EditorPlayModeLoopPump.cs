using UnityEditor;

namespace Game.Editor.Boot
{
    // TASK-134 — памп play-loop'а в интерактивном редактор-play (директива владельца 15:12). EDITOR-ONLY:
    // файл лежит в Assets/Game/Scripts/Editor/ без своего asmdef → компилируется в Assembly-CSharp-Editor
    // (та же сборка, что McpBridgeServer/AndroidDevBuild) и в плеер НЕ попадает. Рантайм-код игры не трогает.
    //
    // Зачем (историческая гипотеза). Прод-бут в редакторе виснет после ~3-5 кадров: грузятся 4/13 конфигов,
    // дальше play-loop, судя по пробнику (EditorBootLoopProbe), перестаёт прокручиваться. Гипотеза была:
    // редактор перестаёт ЗАКАЗЫВАТЬ кадры play-loop'а, когда «нечего делать». Памп форсит кадр из
    // EditorApplication.update (ДРУГОЙ, editor-loop) вызовом QueuePlayerLoopUpdate() — тем же, что даёт Step.
    //
    // ПОЧЕМУ ПАМП НЕ ПОМОГ И НЕ МОГ (TASK-134, owner 15:38 — истинная причина найдена). Настоящая причина
    // столла — не «редактор не заказывает кадры», а `SayKit.init()`, БЛОКИРУЮЩИЙ ГЛАВНЫЙ ПОТОК в редакторе
    // (снят `sample` по процессу Unity: main-thread в __psynch_cvwait внутри -[EditorApplication TickTimer],
    // 1883/2211 сэмплов, CPU 0.2%). При заблокированном главном потоке памп бесполезен by design: его колбэк
    // `EditorApplication.update` живёт на ТОМ ЖЕ заблокированном потоке и не вызывается — качать нечем.
    // Прямое подтверждение: как только SayKit заглушили в редакторе (SayKitInit,
    // SAYKIT_SMOKE_DISABLE_RUNTIME без `&& !UNITY_EDITOR`), оба таймаута LoadingGame сработали ровно за срок,
    // а бут пошёл дальше — БЕЗ всякого пампа. Так что фикс — заглушка SayKit, а не памп.
    //
    // ПОЧЕМУ ФАЙЛ ОСТАВЛЕН (директива владельца, п.6): памп безвреден (editor-only, гард isPlaying, идемпотентно)
    // и может ещё понадобиться, если однажды всплывёт настоящий idle-stall репэйнта. Как фикс прод-бута он
    // снят — держим только страховкой, не полагаемся на него.
    //
    // ВАЖНОЕ ОГРАНИЧЕНИЕ (замер TASK-134/03, 16:42): EditorApplication.update ЗАДРОССЕЛЕН, когда окно
    // редактора НЕ в фокусе — за 22 c play через мост он выстрелил ОДИН раз. Значит даже там, где памп в
    // принципе применим, он качает play-loop ТОЛЬКО при активном (в фокусе) окне.
    internal static class EditorPlayModeLoopPump
    {
        // Регистрируется на каждый перезагруз домена (в т.ч. на вход в play-mode с domain reload).
        // Идемпотентно: -= перед += гарантирует ровно одну подписку, дублей нет даже при повторных вызовах.
        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;

            // Аккуратная отписка: снимаем подписку перед выгрузкой домена (перед следующим reload'ом
            // Register подпишет заново). Держит подписку ровно одну и не оставляет висящих делегатов.
            AssemblyReloadEvents.beforeAssemblyReload -= Unregister;
            AssemblyReloadEvents.beforeAssemblyReload += Unregister;
        }

        private static void Unregister()
        {
            EditorApplication.update -= Pump;
            AssemblyReloadEvents.beforeAssemblyReload -= Unregister;
        }

        private static void Pump()
        {
            // Форсим кадр play-loop'а только когда игра реально идёт и не на паузе. В edit-mode / на паузе
            // QueuePlayerLoopUpdate дёргать нельзя — это прокрутило бы edit-mode loop и/или обошло Pause.
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                return;

            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
