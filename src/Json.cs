using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EdgeReminder {
    internal sealed class JMember {
        public string Name; public int Start; public JNode Value;
    }
    internal sealed class JNode {
        public char Kind; public int Start, End; public string Text;
        public List<JMember> Members = new List<JMember>();
        public JMember Member(string name) { return Members.Find(delegate(JMember m) { return m.Name == name; }); }
        public JNode Get(string name) { JMember m = Member(name); return m == null ? null : m.Value; }
    }
    // A span parser: numbers and unrelated bytes are never re-serialized.
    internal sealed class Json {
        readonly string s; int p;
        static readonly Regex Number = new Regex(@"\G-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?", RegexOptions.CultureInvariant);
        Json(string value) { s = value; }
        public static JNode Parse(string value) {
            Json parser = new Json(value); JNode n = parser.Value(0); parser.White();
            if (parser.p != value.Length) throw new FormatException("配置中存在多余的 JSON 内容。"); return n;
        }
        void White() { while (p < s.Length && (s[p] == ' ' || s[p] == '\r' || s[p] == '\n' || s[p] == '\t')) p++; }
        void Expect(char c) { if (p >= s.Length || s[p++] != c) throw new FormatException("配置 JSON 格式无效。"); }
        string ReadString() {
            Expect('"'); StringBuilder b = new StringBuilder();
            while (p < s.Length) {
                char c = s[p++]; if (c == '"') return b.ToString();
                if (c < 32) throw new FormatException("JSON 字符串含无效字符。");
                if (c != '\\') { b.Append(c); continue; }
                if (p == s.Length) break;
                c = s[p++];
                switch(c) {
                    case '"': case '\\': case '/': b.Append(c); break;
                    case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break;
                    case 'n': b.Append('\n'); break; case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break;
                    case 'u':
                        if (p + 4 > s.Length) throw new FormatException("无效 Unicode 转义。");
                        ushort code; if (!ushort.TryParse(s.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) throw new FormatException("无效 Unicode 转义。");
                        b.Append((char)code); p += 4; break;
                    default: throw new FormatException("JSON 转义无效。");
                }
            }
            throw new FormatException("JSON 字符串未结束。");
        }
        JNode Value(int depth) {
            if (depth > 128) throw new FormatException("配置嵌套层数过深。");
            White(); if (p == s.Length) throw new FormatException("配置 JSON 不完整。");
            JNode n = new JNode { Start = p, Kind = s[p] };
            if (s[p] == '{') {
                p++; White(); HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
                if (p < s.Length && s[p] != '}') while (true) {
                    White(); int start = p; string key = ReadString();
                    if (!names.Add(key)) throw new FormatException("配置含重复字段，已停止修改：" + key);
                    White(); Expect(':'); JNode child = Value(depth + 1);
                    n.Members.Add(new JMember { Name = key, Start = start, Value = child });
                    White(); if (p < s.Length && s[p] == ',') { p++; continue; } break;
                }
                Expect('}');
            } else if (s[p] == '[') {
                p++; White(); if (p < s.Length && s[p] != ']') while (true) {
                    Value(depth + 1); White(); if (p < s.Length && s[p] == ',') { p++; continue; } break;
                } Expect(']');
            } else if (s[p] == '"') { n.Text = ReadString(); }
            else if (s[p] == 't' || s[p] == 'f' || s[p] == 'n') {
                string literal = s[p] == 't' ? "true" : s[p] == 'f' ? "false" : "null";
                if (p + literal.Length > s.Length || s.Substring(p, literal.Length) != literal) throw new FormatException("JSON 值无效。");
                n.Text = literal; p += literal.Length;
            } else {
                Match m = Number.Match(s, p); if (!m.Success || m.Index != p) throw new FormatException("JSON 数字无效。");
                n.Kind = '#'; n.Text = m.Value; p += m.Length;
            }
            n.End = p; return n;
        }
        public static string Quote(string text) {
            StringBuilder b = new StringBuilder("\""); foreach (char c in text) {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c);
            } return b.Append('"').ToString();
        }
        public static JNode At(JNode node, params string[] path) {
            foreach (string part in path) { if (node == null) return null; if (node.Kind != '{') throw new FormatException("配置字段类型异常：" + part); node = node.Get(part); }
            return node;
        }
        public static string Set(string text, string[] path, string rawValue) {
            Parse(rawValue); JNode root = Parse(text); string result = SetAt(text, root, path, 0, rawValue); Parse(result); return result;
        }
        static string SetAt(string s, JNode node, string[] path, int i, string raw) {
            if (node.Kind != '{') throw new FormatException("配置字段不是对象：" + path[i]);
            JMember member = node.Member(path[i]);
            if (member != null) {
                if (i + 1 < path.Length) return SetAt(s, member.Value, path, i + 1, raw);
                return s.Remove(member.Value.Start, member.Value.End - member.Value.Start).Insert(member.Value.Start, raw);
            }
            for (int j = path.Length - 1; j > i; j--) raw = "{" + Quote(path[j]) + ":" + raw + "}";
            string add = (node.Members.Count == 0 ? "" : ",") + Quote(path[i]) + ":" + raw;
            return s.Insert(node.End - 1, add);
        }
        public static string Remove(string text, string[] path) {
            JNode node = Parse(text);
            for (int i = 0; i < path.Length - 1; i++) { if (node.Kind != '{') throw new FormatException("字段类型异常。"); node = node.Get(path[i]); if (node == null) return text; }
            if (node.Kind != '{') throw new FormatException("字段类型异常。");
            int index = node.Members.FindIndex(delegate(JMember m) { return m.Name == path[path.Length - 1]; });
            if (index < 0) return text; JMember target = node.Members[index]; int start, end;
            if (index < node.Members.Count - 1) { start = target.Start; end = node.Members[index + 1].Start; }
            else if (index > 0) { start = node.Members[index - 1].Value.End; end = target.Value.End; }
            else { start = target.Start; end = target.Value.End; }
            string result = text.Remove(start, end - start); Parse(result); return result;
        }
    }
}
