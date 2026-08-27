using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Iliai.UiBuild
{
    /// <summary>
    /// Батч-энтрипоинт компилятора UI для worktree-слотов БЕЗ открытого редактора (борд-на-main,
    /// worktree-пул). Зеркало <see cref="UiBuildAgentEndpoint"/>, но транспорт — не файл-команда, а
    /// одноразовый headless-прогон: команду берём из env, исполняем ТУ ЖЕ логику
    /// (<see cref="UiBuildAgentEndpoint.Execute"/>), тот же контракт результата (<c>OK…</c>/<c>FAILED…</c>),
    /// выходим кодом 0/1.
    ///
    /// Зачем отдельный путь: <see cref="UiBuildAgentEndpoint"/> слушает команду тиками ОТКРЫТОГО
    /// редактора; в слоте пула редактора нет, и файл-команда висела бы до таймаута. Батч по образцу
    /// <c>Gameplay.Sim.Editor.LevelSimBatch</c> исполняет её прямо в -executeMethod.
    ///
    /// Вызов (env выставляет <c>.studio/tools/ui-build.sh</c>, когда редактор не обнаружен):
    /// <code>
    /// UIBUILD_CMD='{"cmd":"catalog"}' UIBUILD_RESULT=&lt;слот&gt;/Logs/uibuild-batch-result.txt \
    ///   &lt;Unity&gt; -batchmode -projectPath &lt;слот&gt; \
    ///   -executeMethod Iliai.UiBuild.UiBuildBatch.Run -logFile &lt;слот&gt;/Logs/uibuild-batch.log
    /// </code>
    /// БЕЗ <c>-quit</c>: код выхода ставит сам <see cref="EditorApplication.Exit"/> ниже (как у
    /// LevelSimBatch), иначе -quit оборвал бы процесс раньше и потерял бы вердикт.
    ///
    /// ПОЧЕМУ результат НЕ в <c>Temp/</c> для батча: Unity удаляет весь каталог <c>Temp/</c> при выходе
    /// редактора. Файловый эндпоинт живёт в ОТКРЫТОМ редакторе, где Temp/ жив весь сеанс; батч же пишет
    /// и тут же выходит — Temp/ (вместе с результатом) исчезает раньше, чем обёртка его прочитает.
    /// Поэтому путь результата приходит из env <c>UIBUILD_RESULT</c> — ПЕРСИСТЕНТНЫЙ, рядом с логом
    /// батча. Содержимое-контракт идентично эндпоинту. Без env — прежний Temp-путь (совместимость: для
    /// живого читателя ВНУТРИ сеанса он валиден).
    /// </summary>
    public static class UiBuildBatch
    {
        const string CommandEnv = "UIBUILD_CMD";
        const string ResultEnv = "UIBUILD_RESULT";
        const string ResultFileDefault = "Temp/uibuild_result.txt";

        public static void Run()
        {
            string root = UiBuildConfig.FindProjectRoot();
            string resultPath = Environment.GetEnvironmentVariable(ResultEnv);
            if (string.IsNullOrEmpty(resultPath))
                resultPath = Path.Combine(root, ResultFileDefault).Replace('\\', '/');

            string raw = Environment.GetEnvironmentVariable(CommandEnv);
            string result;
            if (string.IsNullOrEmpty(raw))
            {
                result = $"FAILED · env {CommandEnv} пуст — батчу не передана команда\n";
            }
            else
            {
                // Ровно тот же диспетчер, что и у файлового эндпоинта: build/buildAll/render/catalog.
                try { result = UiBuildAgentEndpoint.Execute(raw); }
                catch (Exception e) { result = $"FAILED · батч-энтрипоинт упал: {e.GetType().Name}: {e.Message}\n"; }
            }

            try
            {
                string dir = Path.GetDirectoryName(resultPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(resultPath, result);
            }
            catch (IOException e)
            {
                // Результат в файл не лёг — печатаем в лог, чтобы обёртка увидела причину, а не пустоту.
                Debug.LogError($"[uibuild] батч: результат не записан в {resultPath} ({e.Message}):\n{result}");
            }

            // Контракт кода выхода тот же, что у shell-обёртки: OK-в-начале → 0, иначе 1.
            bool ok = result.StartsWith("OK", StringComparison.Ordinal);
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
