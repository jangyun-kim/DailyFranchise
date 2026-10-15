using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DailyFranchise.App.Data
{
    /// <summary>헤더가 있는 CSV 한 행. 열 이름으로 값을 꺼낸다.</summary>
    public sealed class CsvRow
    {
        readonly Dictionary<string, int> _columns;
        readonly string[] _values;

        internal CsvRow(Dictionary<string, int> columns, string[] values, int line)
        {
            _columns = columns;
            _values = values;
            Line = line;
        }

        /// <summary>원본 파일의 행 번호 (1부터, 오류 메시지용).</summary>
        public int Line { get; }

        /// <summary>열 값. 열이 없으면 FormatException, 빈 칸은 "".</summary>
        public string this[string column] =>
            _columns.TryGetValue(column, out int i) ? _values[i]
                : throw new FormatException($"{Line}행: '{column}' 열이 없다.");

        public bool Has(string column) => _columns.ContainsKey(column);

        /// <summary>빈 칸이면 null.</summary>
        public string Optional(string column) =>
            Has(column) && this[column].Length > 0 ? this[column] : null;

        public string Required(string column)
        {
            var v = this[column];
            if (v.Length == 0) throw new FormatException($"{Line}행: '{column}' 값이 비어 있다.");
            return v;
        }

        /// <summary>빈 칸이면 0.</summary>
        public int Int(string column)
        {
            var v = Optional(column);
            if (v == null) return 0;
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                throw new FormatException($"{Line}행: '{column}' 값 '{v}'은 정수가 아니다.");
            return n;
        }

        /// <summary>빈 칸이면 0. ".312"처럼 앞 0을 뺀 표기도 받는다.</summary>
        public double Double(string column)
        {
            var v = Optional(column);
            if (v == null) return 0;
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw new FormatException($"{Line}행: '{column}' 값 '{v}'은 숫자가 아니다.");
            return d;
        }

        /// <summary>후보 열 이름 중 헤더에 있는 첫 이름 (별칭 대응, 예: "Back Num." / "number"). 없으면 null.</summary>
        public string Column(params string[] candidates)
        {
            foreach (var c in candidates)
                if (Has(c)) return c;
            return null;
        }

        /// <summary>
        /// 크래시 없는 정수 읽기 (표시 전용 기록용). 후보 열 중 첫 열을 읽고, 열이 없거나 빈 칸·"-"·변환 실패면 0.
        /// "476.0"처럼 소수 표기된 정수는 반올림해 받는다.
        /// </summary>
        public int SafeInt(params string[] candidates)
        {
            double d = SafeDouble(candidates);
            return d >= int.MaxValue ? int.MaxValue : d <= int.MinValue ? int.MinValue : (int)Math.Round(d);
        }

        /// <summary>크래시 없는 실수 읽기 (표시 전용 기록용). 열이 없거나 빈 칸·"-"·변환 실패·NaN/∞면 0.</summary>
        public double SafeDouble(params string[] candidates)
        {
            var column = Column(candidates);
            var v = column == null ? null : Optional(column);
            if (v == null) return 0;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                   && !double.IsNaN(d) && !double.IsInfinity(d) ? d : 0;
        }
    }

    /// <summary>
    /// 최소한의 RFC 4180 CSV 리더. 외부 라이브러리 없이 로스터 같은 작은 표를 읽는다.
    /// - 첫 비어 있지 않은 행이 헤더. 열 이름 앞뒤 공백은 무시한다.
    /// - 큰따옴표 칸: 쉼표·줄바꿈 포함 가능, "" 는 따옴표 한 개.
    /// - 따옴표 없는 칸은 앞뒤 공백을 잘라낸다.
    /// - 빈 행과 '#'으로 시작하는 주석 행은 건너뛴다. UTF-8 BOM, CRLF/LF 모두 받는다.
    /// - 열 개수가 헤더와 다른 행, 닫히지 않은 따옴표는 FormatException.
    /// </summary>
    public static class CsvReader
    {
        public static List<CsvRow> Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);

            var rows = new List<CsvRow>();
            Dictionary<string, int> columns = null;
            int headerCount = 0;

            foreach (var (fields, line) in Records(text))
            {
                if (fields.Count == 1 && fields[0].Length == 0) continue; // 빈 행
                if (fields[0].StartsWith("#", StringComparison.Ordinal)) continue; // 주석

                if (columns == null)
                {
                    columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < fields.Count; i++)
                    {
                        var name = fields[i].Trim();
                        if (name.Length == 0) throw new FormatException($"{line}행: 헤더에 빈 열 이름이 있다.");
                        if (columns.ContainsKey(name)) throw new FormatException($"{line}행: 헤더 열 이름 중복 '{name}'");
                        columns[name] = i;
                    }
                    headerCount = fields.Count;
                    continue;
                }

                if (fields.Count != headerCount)
                    throw new FormatException($"{line}행: 열 {fields.Count}개 (헤더는 {headerCount}개)");
                rows.Add(new CsvRow(columns, fields.ToArray(), line));
            }

            if (columns == null) throw new FormatException("CSV에 헤더가 없다.");
            return rows;
        }

        /// <summary>레코드 단위로 자른다. 따옴표 안 줄바꿈은 같은 레코드다. line = 레코드가 시작한 행 번호.</summary>
        static IEnumerable<(List<string> fields, int line)> Records(string text)
        {
            var fields = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false, inQuotes = false;
            int line = 1, recordLine = 1;
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i += 2;
                            continue;
                        }
                        inQuotes = false;
                    }
                    else
                    {
                        if (c == '\n') line++;
                        cell.Append(c);
                    }
                    i++;
                    continue;
                }

                if (c == '"' && cell.ToString().Trim().Length == 0 && !quoted)
                {
                    cell.Clear();
                    quoted = inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(Finish(cell, quoted));
                    quoted = false;
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    fields.Add(Finish(cell, quoted));
                    yield return (fields, recordLine);
                    fields = new List<string>();
                    quoted = false;
                    line++;
                    recordLine = line;
                }
                else if (quoted)
                {
                    if (!char.IsWhiteSpace(c))
                        throw new FormatException($"{line}행: 닫는 따옴표 뒤에 글자가 있다.");
                }
                else
                {
                    cell.Append(c);
                }
                i++;
            }

            if (inQuotes) throw new FormatException($"{recordLine}행: 따옴표가 닫히지 않았다.");
            if (fields.Count > 0 || cell.Length > 0 || quoted)
            {
                fields.Add(Finish(cell, quoted));
                yield return (fields, recordLine);
            }
        }

        static string Finish(StringBuilder cell, bool quoted)
        {
            var v = quoted ? cell.ToString() : cell.ToString().Trim();
            cell.Clear();
            return v;
        }
    }
}
