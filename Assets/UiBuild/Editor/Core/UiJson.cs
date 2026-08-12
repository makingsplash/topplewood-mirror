using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Iliai.UiBuild
{
    /// <summary>
    /// Минимальный JSON-ридер. Своя реализация НАМЕРЕННО: пакет обязан быть переносимым без
    /// внешних зависимостей — с Newtonsoft пришлось бы тянуть com.unity.nuget.newtonsoft-json
    /// и настраивать precompiledReferences в каждом новом проекте.
    ///
    /// Возвращает Dictionary&lt;string, object&gt; / List&lt;object&gt; / string / double / bool / null.
    /// Числа — всегда double (в спеке нет чисел, где это важно: целочисленность проверяет валидация).
    /// </summary>
    public static class UiJson
    {
        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new UiJsonException("пустой файл");
            var p = new Cursor(text);
            p.SkipWs();
            var value = ReadValue(p);
            p.SkipWs();
            if (!p.Eof) throw p.Error("лишние символы после значения");
            return value;
        }

        static object ReadValue(Cursor p)
        {
            if (p.Eof) throw p.Error("оборвалось на середине значения");
            char c = p.Peek;
            switch (c)
            {
                case '{': return ReadObject(p);
                case '[': return ReadArray(p);
                case '"': return ReadString(p);
                case 't': p.Expect("true"); return true;
                case 'f': p.Expect("false"); return false;
                case 'n': p.Expect("null"); return null;
                default: return ReadNumber(p);
            }
        }

        static Dictionary<string, object> ReadObject(Cursor p)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            p.Next();                                   // '{'
            p.SkipWs();
            if (p.Peek == '}') { p.Next(); return map; }
            while (true)
            {
                p.SkipWs();
                if (p.Peek != '"') throw p.Error("ключ объекта должен быть строкой в кавычках");
                string key = ReadString(p);
                p.SkipWs();
                if (p.Peek != ':') throw p.Error($"после ключа \"{key}\" ожидалось ':'");
                p.Next();
                p.SkipWs();
                map[key] = ReadValue(p);                 // дубль ключа: побеждает последний
                p.SkipWs();
                char c = p.Peek;
                if (c == ',') { p.Next(); continue; }
                if (c == '}') { p.Next(); return map; }
                throw p.Error("ожидалось ',' или '}'");
            }
        }

        static List<object> ReadArray(Cursor p)
        {
            var list = new List<object>();
            p.Next();                                   // '['
            p.SkipWs();
            if (p.Peek == ']') { p.Next(); return list; }
            while (true)
            {
                p.SkipWs();
                list.Add(ReadValue(p));
                p.SkipWs();
                char c = p.Peek;
                if (c == ',') { p.Next(); continue; }
                if (c == ']') { p.Next(); return list; }
                throw p.Error("ожидалось ',' или ']'");
            }
        }

        static string ReadString(Cursor p)
        {
            p.Next();                                   // '"'
            var sb = new StringBuilder();
            while (true)
            {
                if (p.Eof) throw p.Error("незакрытая строка");
                char c = p.Next();
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (p.Eof) throw p.Error("незакрытый escape");
                char e = p.Next();
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (p.Remaining < 4) throw p.Error("оборванный \\u escape");
                        sb.Append((char)Convert.ToInt32(p.Take(4), 16));
                        break;
                    default: throw p.Error($"неизвестный escape \\{e}");
                }
            }
        }

        static double ReadNumber(Cursor p)
        {
            int start = p.Index;
            if (p.Peek == '-' || p.Peek == '+') p.Next();
            while (!p.Eof && (char.IsDigit(p.Peek) || p.Peek == '.' || p.Peek == 'e' || p.Peek == 'E' || p.Peek == '+' || p.Peek == '-')) p.Next();
            string raw = p.Slice(start, p.Index);
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            throw p.Error($"не число: '{raw}'");
        }

        sealed class Cursor
        {
            readonly string _s;
            int _i;

            public Cursor(string s) { _s = s; _i = 0; }

            public bool Eof => _i >= _s.Length;
            public int Index => _i;
            public int Remaining => _s.Length - _i;
            public char Peek => Eof ? '\0' : _s[_i];

            public char Next() => _s[_i++];
            public string Slice(int from, int to) => _s.Substring(from, to - from);

            public string Take(int n)
            {
                string r = _s.Substring(_i, n);
                _i += n;
                return r;
            }

            public void SkipWs()
            {
                while (!Eof)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { _i++; continue; }
                    // Комментарии в JSON нестандартны, но спеку читают и правят люди — пропускаем // и /* */
                    if (c == '/' && _i + 1 < _s.Length)
                    {
                        char n = _s[_i + 1];
                        if (n == '/') { while (!Eof && _s[_i] != '\n') _i++; continue; }
                        if (n == '*')
                        {
                            _i += 2;
                            while (_i + 1 < _s.Length && !(_s[_i] == '*' && _s[_i + 1] == '/')) _i++;
                            _i = Math.Min(_s.Length, _i + 2);
                            continue;
                        }
                    }
                    return;
                }
            }

            public void Expect(string literal)
            {
                if (Remaining < literal.Length || string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
                    throw Error($"ожидалось '{literal}'");
                _i += literal.Length;
            }

            public UiJsonException Error(string message)
            {
                int line = 1, col = 1;
                for (int k = 0; k < _i && k < _s.Length; k++)
                {
                    if (_s[k] == '\n') { line++; col = 1; }
                    else col++;
                }
                return new UiJsonException($"{message} (строка {line}, символ {col})");
            }
        }
    }

    public sealed class UiJsonException : Exception
    {
        public UiJsonException(string message) : base(message) { }
    }
}
