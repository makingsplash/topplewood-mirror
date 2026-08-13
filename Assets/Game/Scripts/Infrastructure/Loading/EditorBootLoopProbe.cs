#if UNITY_EDITOR
using System;
using Cysharp.Threading.Tasks;
using Game.Services.LogService;
using UnityEditor;
using UnityEngine;

namespace Infrastructure.Loading
{
    // TASK-134 — ВРЕМЕННАЯ диагностика (editor-only), НЕ фикс. Удаляется тем прогоном, который
    // по её числам выберет ветку фикса (§3 архитектора). Директива владельца 14:36: «только диагностика».
    //
    // Зачем. Прод-бут в редакторе виснет: грузятся 4/13 конфигов, дальше тишина, ни таймаут, ни finished
    // не выходят. Три прогона гадали между тремя мирами — и ни один не размечен ФАКТОМ. Пробник разделяет
    // их одним play-прогоном, печатая ПЕРИОДИЧЕСКИ (директива, п.2) через _logService/LogCategory.LoadLevel —
    // единственный видимый в логе Unity канал (мост play-mode-логи не отдаёт, Debug.* режется filterLogType,
    // см. LoadingGame.InitializeGameSettings) — величины:
    //
    //   injected — PlayerLoopHelper.IsInjectedUniTaskPlayerLoop(): ЕСТЬ ли раннеры UniTask в PlayerLoop;
    //   utick    — ХАРТБИТ раннера UniTask: счётчик резюмирований UniTask.Yield(Update). ТИКАЮТ ли раннеры;
    //   mbtick   — счётчик MonoBehaviour.Update: идёт ли сам play-loop (нативный колбэк, ⊥ раннеру UniTask);
    //   frame    — Time.frameCount.
    //
    // Развилка (её решает владелец по числам, фикс — отдельным прогоном):
    //   injected=false                         → раннеры UniTask ВЫПАЛИ           → ветка A (ре-инъекция);
    //   injected=true, utick заморожен, mbtick растёт → раннеры есть, но НЕ тикают → ветка A (раннер мёртв);
    //   injected=true, utick растёт             → раннеры живы и тикают           → ветка B (провайдер, Plan B);
    //   mbtick не растёт                        → стоит весь play-loop            → противоречит Step-тесту.
    // Пара injected/utick — ровно то, что просил владелец: отличить «раннер есть, но не тикает» от «тикает».
    // mbtick — обязательный якорь: без него «utick стоит» неотличимо от «play-loop вообще встал».
    //
    // Почему принтер сидит на MonoBehaviour.Update (play-loop), а НЕ на EditorApplication.update. Замер 16:42
    // показал: EditorApplication.update без фокуса редактора ЗАДРОСЕЛЕН — за 22 c play через мост он выстрелил
    // ОДИН раз (frame=2), бесполезно для столла. runInBackground=1 держит без фокуса именно PLAY-loop, поэтому
    // MonoBehaviour.Update прокачивается через весь столл. При этом Update — нативный колбэк play-loop, он ⊥
    // раннеру UniTask: если раннер умрёт, принтер выживет и покажет МОМЕНТ, когда utick замер. Хартбит же
    // НАМЕРЕННО сидит на раннере UniTask — он и есть измеряемая величина.
    internal static class EditorBootLoopProbe
    {
        private const float LogIntervalSeconds = 1f;

        private static ILogService _logService;
        private static long _uniTaskTick;
        private static bool _running;
        private static ProbeBehaviour _behaviour;

        // Зовётся из LoadingGame на входе в бут (editor-only). Идемпотентна.
        public static void Begin(ILogService logService)
        {
            if (_running)
                return;

            _logService = logService;
            _uniTaskTick = 0;
            _running = true;

            PumpUniTaskHeartbeatAsync().Forget();

            var go = new GameObject("EditorBootLoopProbe") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _behaviour = go.AddComponent<ProbeBehaviour>();
        }

        // Сброс статики. Зовётся из OnDestroy пробника (единственная точка), чтобы разрушение объекта и
        // очистка состояния были согласованы — иначе после teardown play-mode на застывшем loop'е статика
        // осталась бы «running», и следующий Begin (без domain reload) не поднялся бы.
        private static void Cleanup()
        {
            _running = false;
            _logService = null;
            _behaviour = null;
        }

        // Хартбит НА раннере UniTask. Каждое резюмирование Yield(Update) инкрементит счётчик:
        //   раннер тикает          → utick растёт;
        //   раннер есть, но встал   → Yield не резюмится → utick стоит (injected покажет «есть»);
        //   раннера нет             → Yield не резюмится → utick стоит (injected покажет «нет»).
        // Инкремент ПОСЛЕ await — чтобы utick считал реальные тики раннера, а не один синхронный вход.
        private static async UniTaskVoid PumpUniTaskHeartbeatAsync()
        {
            while (_running && EditorApplication.isPlaying)
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
                _uniTaskTick++;
            }
        }

        // Принтер на play-loop. Живёт весь play-сеанс, самоснимается на выходе из play-mode.
        private sealed class ProbeBehaviour : MonoBehaviour
        {
            // Первые DenseFrameCount кадров печатаем КАЖДЫЙ (без секундного порога) — иначе «столл после
            // ~3 кадров» неотличим от «печать просто ещё не дошла до 1-секундного порога» (грабля прошлого
            // прогона: одна строка mbtick=1 и тишина). Дальше — раз в секунду, чтобы не заваливать лог.
            private const int DenseFrameCount = 120;

            private ILogService _log; // своя ссылка: к моменту OnDestroy статический _logService уже обнулён Cleanup
            private long _mbTick;
            private float _nextLog;

            private void Awake()
            {
                _log = _logService;
            }

            private void Update()
            {
                _mbTick++;

                if (!EditorApplication.isPlaying)
                {
                    // Штатный выход из play-mode: уничтожаем объект → сработает OnDestroy (лог + Cleanup).
                    Destroy(gameObject);
                    return;
                }

                if (_mbTick > DenseFrameCount && Time.realtimeSinceStartup < _nextLog)
                    return;

                _nextLog = Time.realtimeSinceStartup + LogIntervalSeconds;
                Print("loop-probe");
            }

            // Разрешает ключевую неоднозначность (директива владельца 15:12): молчание в логе значит
            // «play-loop встал» ТОЛЬКО если этой строки НЕТ. Если она есть — объект пробника уничтожен, и
            // тишина после неё — не застывший loop, а мёртвый наблюдатель. mbtick/frame фиксируют ПОСЛЕДНИЙ
            // кадр, на котором пробник был жив.
            private void OnDestroy()
            {
                if (_log != null)
                {
                    var now = DateTime.Now;
                    _log.Log(
                        LogLevel.Warning,
                        $"[{now:T}.{now.Millisecond.ToString()}] loop-probe: OnDestroy at mbtick={_mbTick.ToString()} " +
                        $"frame={Time.frameCount.ToString()} (probe destroyed — silence AFTER this is NOT a frozen loop)",
                        LogCategory.LoadLevel);
                }

                Cleanup();
            }

            private void Print(string tag)
            {
                if (_log == null)
                    return;

                var injected = PlayerLoopHelper.IsInjectedUniTaskPlayerLoop();
                var now = DateTime.Now;
                _log.Log(
                    LogLevel.Info,
                    $"[{now:T}.{now.Millisecond.ToString()}] {tag}: injected={injected.ToString()} " +
                    $"utick={_uniTaskTick.ToString()} mbtick={_mbTick.ToString()} frame={Time.frameCount.ToString()}",
                    LogCategory.LoadLevel);
            }
        }
    }
}
#endif
