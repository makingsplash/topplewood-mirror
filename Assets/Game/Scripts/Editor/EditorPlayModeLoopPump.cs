using UnityEditor;

namespace Game.Editor.Boot
{
    // TASK-134 — памп play-loop'а в интерактивном редактор-play (директива владельца 15:12). EDITOR-ONLY:
    // файл лежит в Assets/Game/Scripts/Editor/ без своего asmdef → компилируется в Assembly-CSharp-Editor
    // (та же сборка, что McpBridgeServer/AndroidDevBuild) и в плеер НЕ попадает. Рантайм-код игры не трогает.
    //
    // Зачем. Прод-бут в редакторе виснет после ~3-5 кадров: грузятся 4/13 конфигов, дальше play-loop, судя по
    // пробнику (EditorBootLoopProbe: injected=True, но mbtick замер на первой же печати), перестаёт
    // прокручиваться. Гипотеза владельца: редактор перестаёт ЗАКАЗЫВАТЬ кадры play-loop'а, когда «нечего
    // делать». Памп форсит кадр из EditorApplication.update (это ДРУГОЙ, editor-loop; он тикает независимо от
    // play-loop'а) вызовом QueuePlayerLoopUpdate() — тем же, что даёт ручной Step, только автоматически.
    //
    // ВАЖНОЕ ОГРАНИЧЕНИЕ (замер TASK-134/03, 16:42): EditorApplication.update ЗАДРОССЕЛЕН, когда окно
    // редактора НЕ в фокусе — за 22 c play через мост он выстрелил ОДИН раз. Значит памп реально качает
    // play-loop ТОЛЬКО при активном (в фокусе) окне редактора. Прогон роли через мост идёт без фокуса, поэтому
    // эффективность пампа мостом НЕ проверяется — мерить нужно с редактором в фокусе. Дроссель без фокуса —
    // отдельная проблема (см. артефакт TASK-134/06).
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
