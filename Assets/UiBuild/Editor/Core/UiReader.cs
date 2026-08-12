using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Iliai.UiBuild
{
    /// <summary>
    /// Строгий читатель JSON-объекта: помнит, какие ключи прочли, и на <see cref="Finish"/> ругается
    /// на всё остальное. Это прямая реализация правила контракта «поля, которого нет в схеме,
    /// не существует»: опечатка `anhcor` не должна молча превращаться в дефолтную раскладку.
    /// </summary>
    public sealed class UiReader
    {
        readonly Dictionary<string, object> _map;
        readonly HashSet<string> _read = new HashSet<string>();
        readonly UiBuildReport _report;
        readonly string _where;

        public UiReader(Dictionary<string, object> map, UiBuildReport report, string where)
        {
            _map = map ?? new Dictionary<string, object>();
            _report = report;
            _where = where;
        }

        public string Where => _where;
        public bool Has(string key) => _map.ContainsKey(key);

        object Raw(string key)
        {
            _read.Add(key);
            return _map.TryGetValue(key, out object v) ? v : null;
        }

        /// <summary>Значение как есть, без проверки типа. Для полей, где схема допускает РАЗНЫЕ типы
        /// (`fit`: true/false либо "w"/"h") — типизированные геттеры там дали бы ложную ошибку.</summary>
        public object RawValue(string key) => Raw(key);

        public string Str(string key, string fallback = null)
        {
            object v = Raw(key);
            if (v == null) return fallback;
            if (v is string s) return s;
            _report.Error(_where, $"'{key}' должен быть строкой");
            return fallback;
        }

        public bool Bool(string key, bool fallback = false)
        {
            object v = Raw(key);
            if (v == null) return fallback;
            if (v is bool b) return b;
            _report.Error(_where, $"'{key}' должен быть true/false");
            return fallback;
        }

        public float Float(string key, float fallback)
        {
            object v = Raw(key);
            if (v == null) return fallback;
            if (v is double d) return (float)d;
            _report.Error(_where, $"'{key}' должен быть числом");
            return fallback;
        }

        /// <summary>Целое из спеки. Дробное — предупреждение + округление: см. правило «только целые координаты».</summary>
        public int Int(string key, int fallback)
        {
            object v = Raw(key);
            if (v == null) return fallback;
            if (!(v is double d))
            {
                _report.Error(_where, $"'{key}' должен быть числом");
                return fallback;
            }
            int rounded = Mathf.RoundToInt((float)d);
            if (Mathf.Abs((float)d - rounded) > 0.0001f)
                _report.Warn(_where, $"'{key}' = {d.ToString(CultureInfo.InvariantCulture)} — дробные координаты дают размытые края; округлил до {rounded}");
            return rounded;
        }

        public int? IntOpt(string key)
        {
            if (!_map.ContainsKey(key)) { _read.Add(key); return null; }
            return Int(key, 0);
        }

        public Dictionary<string, object> Map(string key)
        {
            object v = Raw(key);
            if (v == null) return null;
            if (v is Dictionary<string, object> m) return m;
            _report.Error(_where, $"'{key}' должен быть объектом");
            return null;
        }

        public UiReader Sub(string key, string whereSuffix = null)
        {
            Dictionary<string, object> m = Map(key);
            return m == null ? null : new UiReader(m, _report, whereSuffix == null ? _where + "." + key : whereSuffix);
        }

        public List<object> List(string key)
        {
            object v = Raw(key);
            if (v == null) return null;
            if (v is List<object> l) return l;
            _report.Error(_where, $"'{key}' должен быть массивом");
            return null;
        }

        public Vector2Int? SizeOpt(string key)
        {
            UiReader r = Sub(key, _where + "." + key);
            if (r == null) return null;
            var size = new Vector2Int(r.Int("w", 0), r.Int("h", 0));
            r.Finish();
            return size;
        }

        public Vector2? Vec2Opt(string key)
        {
            List<object> l = List(key);
            if (l == null) return null;
            if (l.Count != 2 || !(l[0] is double) || !(l[1] is double))
            {
                _report.Error(_where, $"'{key}' должен быть массивом из двух чисел, например [0, 0.5]");
                return null;
            }
            return new Vector2((float)(double)l[0], (float)(double)l[1]);
        }

        /// <summary>Ругается на непрочитанные ключи. Вызывать ОБЯЗАТЕЛЬНО после разбора объекта.</summary>
        public void Finish(string hint = null)
        {
            foreach (KeyValuePair<string, object> kv in _map)
            {
                if (_read.Contains(kv.Key)) continue;
                if (kv.Key.StartsWith("_")) continue;              // `_comment` и прочие заметки автора — легальны
                string msg = $"неизвестное поле '{kv.Key}'";
                if (hint != null) msg += " — " + hint;
                _report.Error(_where, msg + ". Схема: Assets/UiBuild/docs/spec-schema.md");
            }
        }
    }
}
