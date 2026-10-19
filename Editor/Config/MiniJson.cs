// BuildKit —— 极简 JSON 解析/序列化
// JsonUtility 做不到的事情靠它：profile 深度合并、检查拼错的字段名、报错带行号。
// 对象 → Dictionary<string, object>（保持书写顺序），数组 → List<object>，
// 数字 → long（整数）或 double，另外还有 string / bool / null。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BuildKit
{
    public static class MiniJson
    {
        public class ParseException : Exception
        {
            public readonly int Line;
            public ParseException(string msg, int line) : base($"第 {line} 行：{msg}") { Line = line; }
        }

        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new Parser(text);
            p.SkipWs();
            var v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw p.Error("JSON 结束后还有多余内容");
            return v;
        }

        public static string Serialize(object value, bool pretty = false)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        // ───────────── 解析 ─────────────

        class Parser
        {
            readonly string s;
            int i;
            public Parser(string text)
            {
                s = text;
                if (s.Length > 0 && s[0] == '﻿') i = 1;   // BOM
            }

            public bool End => i >= s.Length;

            int LineAt(int pos)
            {
                int line = 1;
                for (int k = 0; k < pos && k < s.Length; k++) if (s[k] == '\n') line++;
                return line;
            }

            public ParseException Error(string msg) => new ParseException(msg, LineAt(i));

            public void SkipWs()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                    if (c == '/' && i + 1 < s.Length && (s[i + 1] == '/' || s[i + 1] == '*'))
                        throw Error("JSON 不支持注释");
                    break;
                }
            }

            public object ReadValue()
            {
                if (End) throw Error("意外的文件结尾");
                char c = s[i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error($"无法识别的字符 '{c}'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error($"应为 {word}");
                i += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                i++; // {
                SkipWs();
                if (!End && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (End || s[i] != '"') throw Error(!End && s[i] == '}' ? "最后一项后面不能有逗号" : "应为字段名（用双引号括起来）");
                    var key = ReadString();
                    SkipWs();
                    if (End || s[i] != ':') throw Error($"字段 \"{key}\" 后面应为冒号");
                    i++;
                    SkipWs();
                    if (d.ContainsKey(key)) throw Error($"字段 \"{key}\" 重复");
                    d[key] = ReadValue();
                    SkipWs();
                    if (End) throw Error("对象没有闭合，缺少 }");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Error("应为逗号或 }（是不是上一行末尾漏了逗号？）");
                }
            }

            List<object> ReadArray()
            {
                var list = new List<object>();
                i++; // [
                SkipWs();
                if (!End && s[i] == ']') { i++; return list; }
                while (true)
                {
                    SkipWs();
                    if (!End && s[i] == ']') throw Error("最后一项后面不能有逗号");
                    list.Add(ReadValue());
                    SkipWs();
                    if (End) throw Error("数组没有闭合，缺少 ]");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return list; }
                    throw Error("应为逗号或 ]");
                }
            }

            string ReadString()
            {
                var sb = new StringBuilder();
                i++; // "
                while (true)
                {
                    if (End) throw Error("字符串没有闭合，缺少 \"");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\n') throw Error("字符串不能换行");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("字符串没有闭合");
                    char e = s[i++];
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
                            if (i + 4 > s.Length) throw Error("\\u 转义不完整");
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: throw Error($"无效的转义 \\{e}（Windows 路径请写成 / 或 \\\\）");
                    }
                }
            }

            object ReadNumber()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
                var token = s.Substring(start, i - start);
                if (token.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 &&
                    long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
                throw Error($"无效的数字 {token}");
            }
        }

        // ───────────── 序列化 ─────────────

        static void Write(StringBuilder sb, object v, bool pretty, int indent)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string str: WriteString(sb, str); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case IDictionary<string, object> dict:
                {
                    if (dict.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, pretty, indent + 1);
                        WriteString(sb, kv.Key);
                        sb.Append(pretty ? ": " : ":");
                        Write(sb, kv.Value, pretty, indent + 1);
                    }
                    NewLine(sb, pretty, indent);
                    sb.Append('}');
                    break;
                }
                case IList list:
                {
                    if (list.Count == 0) { sb.Append("[]"); break; }
                    sb.Append('[');
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        NewLine(sb, pretty, indent + 1);
                        Write(sb, list[k], pretty, indent + 1);
                    }
                    NewLine(sb, pretty, indent);
                    sb.Append(']');
                    break;
                }
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
                case IFormattable num: sb.Append(num.ToString(null, CultureInfo.InvariantCulture)); break;   // int / long 等
                default: WriteString(sb, v.ToString()); break;
            }
        }

        static void NewLine(StringBuilder sb, bool pretty, int indent)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', indent * 2);
        }

        static void WriteString(StringBuilder sb, string str)
        {
            sb.Append('"');
            foreach (char c in str)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
