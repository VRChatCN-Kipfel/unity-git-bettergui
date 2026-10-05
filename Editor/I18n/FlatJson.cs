using System;
using System.Collections.Generic;
using System.Text;

namespace KF.GitUI
{
    /// <summary>
    /// 极简扁平 JSON 读写：只处理 <c>{ "key": "value", ... }</c> 这一种形状。
    ///
    /// 为什么要自己写：Unity 的 <c>JsonUtility</c> 不支持任意键的字典（只能反序列化固定的
    /// [Serializable] 字段），而语言包天然是"键 → 文案"的开放映射；引入 Newtonsoft 又违反
    /// 本包零依赖（package.json dependencies 为空）的约束。
    ///
    /// 严格程度：**拒绝**尾逗号、重复键、非字符串值、对象外的多余内容——语言包是贡献者手写的，
    /// 宁可在解析时报错，也不要把半个文件静默读成半门语言。
    /// </summary>
    public static class FlatJson
    {
        /// <summary>解析扁平字符串映射；结构非法时抛 FormatException（消息含字符位置）。</summary>
        public static Dictionary<string, string> Parse(string text)
        {
            if (text == null) throw new FormatException("json: input is null");

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var i = 0;

            SkipWhitespace(text, ref i);
            Expect(text, ref i, '{');
            SkipWhitespace(text, ref i);

            if (Peek(text, i) == '}')
            {
                i++;
                SkipWhitespace(text, ref i);
                RequireEnd(text, i);
                return map;
            }

            while (true)
            {
                SkipWhitespace(text, ref i);
                var key = ReadString(text, ref i);
                SkipWhitespace(text, ref i);
                Expect(text, ref i, ':');
                SkipWhitespace(text, ref i);
                var value = ReadString(text, ref i);

                if (map.ContainsKey(key))
                    throw Error("json: duplicate key \"" + key + "\"", i);
                map[key] = value;

                SkipWhitespace(text, ref i);
                var c = Peek(text, i);
                if (c == ',')
                {
                    i++;
                    continue;
                }
                if (c == '}')
                {
                    i++;
                    break;
                }
                throw Error("json: expected ',' or '}'", i);
            }

            SkipWhitespace(text, ref i);
            RequireEnd(text, i);
            return map;
        }

        /// <summary>按给定顺序序列化为缩进 JSON（用于导出骨架，键序稳定）。</summary>
        public static string Serialize(IEnumerable<KeyValuePair<string, string>> entries)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            var first = true;
            if (entries != null)
            {
                foreach (var kv in entries)
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append("  ").Append(Quote(kv.Key)).Append(": ").Append(Quote(kv.Value));
                }
            }
            sb.Append("\n}\n");
            return sb.ToString();
        }

        // ---- 内部 ----

        private const string HexDigits = "0123456789abcdefABCDEF";

        private static void SkipWhitespace(string text, ref int i)
        {
            while (i < text.Length)
            {
                var c = text[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }

        private static char Peek(string text, int i)
        {
            return i < text.Length ? text[i] : '\0';
        }

        private static void Expect(string text, ref int i, char expected)
        {
            if (Peek(text, i) != expected)
                throw Error("json: expected '" + expected + "'", i);
            i++;
        }

        private static void RequireEnd(string text, int i)
        {
            if (i != text.Length)
                throw Error("json: unexpected trailing content", i);
        }

        private static FormatException Error(string message, int position)
        {
            return new FormatException(message + " (at char " + position + ")");
        }

        private static string ReadString(string text, ref int i)
        {
            Expect(text, ref i, '"');
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= text.Length) throw Error("json: unterminated string", i);
                var c = text[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i >= text.Length) throw Error("json: unterminated escape", i);
                var esc = text[i++];
                switch (esc)
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
                        {
                            if (i + 4 > text.Length) throw Error("json: truncated \\u escape", i);
                            var hex = text.Substring(i, 4);
                            foreach (var h in hex)
                                if (HexDigits.IndexOf(h) < 0)
                                    throw Error("json: invalid \\u escape", i);
                            sb.Append((char)Convert.ToInt32(hex, 16));
                            i += 4;
                            break;
                        }
                    default:
                        throw Error("json: unknown escape \\" + esc, i);
                }
            }
        }

        private static string Quote(string value)
        {
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (var c in value ?? string.Empty)
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
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
