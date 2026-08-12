using System.Collections.Generic;
using System.Text;

namespace Iliai.UiBuild
{
    /// <summary>
    /// Отчёт сборки. Смысл существования: «собралось» не бывает «почти» — при любой ошибке префаб
    /// НЕ записывается, а роль получает точный список того, что починить (с `файл:id`).
    /// Отчёт — машинный выход команды, поэтому формат стабилен и читается агентом целиком.
    /// </summary>
    public sealed class UiBuildReport
    {
        public sealed class Entry
        {
            public string Where;      // "screens/no_lives.json:cta" — файл и id узла
            public string Message;
        }

        readonly List<Entry> _errors = new List<Entry>();
        readonly List<Entry> _warnings = new List<Entry>();
        readonly List<string> _notes = new List<string>();

        public IReadOnlyList<Entry> Errors => _errors;
        public IReadOnlyList<Entry> Warnings => _warnings;
        public IReadOnlyList<string> Notes => _notes;
        public bool Ok => _errors.Count == 0;

        public void Error(string where, string message) => _errors.Add(new Entry { Where = where, Message = message });
        public void Warn(string where, string message) => _warnings.Add(new Entry { Where = where, Message = message });
        public void Note(string message) => _notes.Add(message);

        public void Absorb(UiBuildReport other)
        {
            if (other == null) return;
            _errors.AddRange(other._errors);
            _warnings.AddRange(other._warnings);
            _notes.AddRange(other._notes);
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append(Ok ? "OK" : "FAILED");
            sb.Append(" · ошибок: ").Append(_errors.Count);
            sb.Append(" · предупреждений: ").Append(_warnings.Count).Append('\n');
            foreach (string n in _notes) sb.Append("  · ").Append(n).Append('\n');
            foreach (Entry e in _errors) sb.Append("  ERROR ").Append(e.Where).Append(" — ").Append(e.Message).Append('\n');
            foreach (Entry w in _warnings) sb.Append("  WARN  ").Append(w.Where).Append(" — ").Append(w.Message).Append('\n');
            return sb.ToString();
        }
    }
}
