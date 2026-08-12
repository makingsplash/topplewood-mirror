using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Iliai.UiBuild
{
    /// <summary>
    /// Точка входа для агента: файл-команда в <c>Temp/</c> ЭТОГО проекта → результат текстом.
    ///
    /// Почему файлы, а не порт: у студийного Unity-моста один жёстко заданный порт, и батч-Unity из
    /// чужого worktree его перехватывает — ответ приходит УСПЕШНЫМ из другого дерева. Файл в
    /// <c>Temp/</c> адресует конкретный проект по самой своей природе, поэтому спутать инстансы
    /// нечем, и параллельные проекты не мешают друг другу.
    ///
    /// Команда (JSON, одна на файл):
    ///   { "cmd": "build",    "spec": "screens/no_lives.json" }
    ///   { "cmd": "buildAll" }
    ///   { "cmd": "render",   "spec": "no_lives", "flat": true, "out": "design/.render/no_lives.wire.png" }
    ///   { "cmd": "catalog" }
    /// </summary>
    [InitializeOnLoad]
    public static class UiBuildAgentEndpoint
    {
        const string CommandFile = "Temp/uibuild_cmd.json";
        const string ResultFile = "Temp/uibuild_result.txt";
        const int PollEveryFrames = 20;

        static int _frames;
        static string _root;

        static UiBuildAgentEndpoint()
        {
            _root = UiBuildConfig.FindProjectRoot();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static string Abs(string rel) => Path.Combine(_root, rel).Replace('\\', '/');

        static void Tick()
        {
            if (++_frames % PollEveryFrames != 0) return;
            // Во время компиляции/импорта/плеймода сборка префабов небезопасна — ждём спокойного редактора.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            string cmdPath = Abs(CommandFile);
            if (!File.Exists(cmdPath)) return;

            string raw;
            try { raw = File.ReadAllText(cmdPath); }
            catch (IOException) { return; }                    // файл ещё пишется — попробуем на следующем тике

            // Удаляем команду ДО исполнения: если сборка упадёт насмерть, команда не будет
            // перезапускаться в бесконечном цикле на каждом тике.
            try { File.Delete(cmdPath); } catch (IOException) { }

            string result;
            try { result = Execute(raw); }
            catch (Exception e) { result = $"FAILED · эндпоинт упал: {e.GetType().Name}: {e.Message}\n"; }

            try { File.WriteAllText(Abs(ResultFile), result); }
            catch (IOException e) { Debug.LogError($"[uibuild] результат не записан: {e.Message}"); }
        }

        static string Execute(string raw)
        {
            object parsed;
            try { parsed = UiJson.Parse(raw); }
            catch (UiJsonException e) { return $"FAILED · битая команда: {e.Message}\n"; }

            if (!(parsed is Dictionary<string, object> map)) return "FAILED · команда должна быть JSON-объектом\n";
            string cmd = map.TryGetValue("cmd", out object c) ? c as string : null;
            string spec = map.TryGetValue("spec", out object s) ? s as string : null;
            string outPath = map.TryGetValue("out", out object o) ? o as string : null;
            bool flat = map.TryGetValue("flat", out object f) && f is bool b && b;

            switch (cmd)
            {
                case "build": return UiBuildApi.BuildOne(spec).ToText();
                case "buildAll": return UiBuildApi.BuildAll().ToText();
                case "render": return UiBuildApi.Render(spec, flat, outPath).ToText();
                case "catalog": return "OK · каталог\n" + UiBuildApi.CatalogText();
                default: return $"FAILED · неизвестная команда '{cmd}'. Есть: build, buildAll, render, catalog\n";
            }
        }
    }
}
