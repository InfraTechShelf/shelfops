using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CitrixAdminTool.Core.Configuration.Json
{
    /// <summary>
    /// 依存ライブラリを持たない最小限のJSONライタ。
    /// sites.json は人が手編集する設定ファイルなので、常にインデント付きで書き出す。
    /// </summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private int _depth;

        public static string Write(object value)
        {
            var w = new JsonWriter();
            w.WriteValue(value);
            return w._sb.ToString();
        }

        private void WriteValue(object value)
        {
            if (value == null) { _sb.Append("null"); return; }

            var dict = value as IDictionary<string, object>;
            if (dict != null) { WriteObject(dict); return; }

            var list = value as IEnumerable<object>;
            if (list != null) { WriteArray(list); return; }

            var str = value as string;
            if (str != null) { WriteString(str); return; }

            if (value is bool) { _sb.Append((bool)value ? "true" : "false"); return; }

            if (value is int || value is long)
            {
                _sb.Append(((System.IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                return;
            }

            if (value is double || value is float || value is decimal)
            {
                _sb.Append(((System.IFormattable)value).ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            // enum などは文字列として書き出す（authMode を "IntegratedWindows" と表現するため）。
            WriteString(value.ToString());
        }

        private void WriteObject(IDictionary<string, object> dict)
        {
            if (dict.Count == 0) { _sb.Append("{}"); return; }

            _sb.Append('{');
            _depth++;

            bool first = true;
            foreach (var kv in dict)
            {
                if (!first) _sb.Append(',');
                first = false;

                NewLineIndent();
                WriteString(kv.Key);
                _sb.Append(": ");
                WriteValue(kv.Value);
            }

            _depth--;
            NewLineIndent();
            _sb.Append('}');
        }

        private void WriteArray(IEnumerable<object> items)
        {
            var buffer = new List<object>(items);
            if (buffer.Count == 0) { _sb.Append("[]"); return; }

            // 文字列だけの短い配列（alternateDdcs など）は1行に収めた方が読みやすい。
            bool inline = true;
            foreach (var item in buffer)
            {
                if (!(item is string)) { inline = false; break; }
            }

            if (inline)
            {
                _sb.Append('[');
                for (int i = 0; i < buffer.Count; i++)
                {
                    if (i > 0) _sb.Append(", ");
                    WriteString((string)buffer[i]);
                }
                _sb.Append(']');
                return;
            }

            _sb.Append('[');
            _depth++;

            for (int i = 0; i < buffer.Count; i++)
            {
                if (i > 0) _sb.Append(',');
                NewLineIndent();
                WriteValue(buffer[i]);
            }

            _depth--;
            NewLineIndent();
            _sb.Append(']');
        }

        private void WriteString(string value)
        {
            _sb.Append('"');

            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        // 制御文字のみエスケープする。日本語はUTF-8でそのまま出す
                        // （管理端末でメモ帳等から読める方が実務上有利なため）。
                        if (ch < 0x20)
                            _sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            _sb.Append(ch);
                        break;
                }
            }

            _sb.Append('"');
        }

        private void NewLineIndent()
        {
            _sb.Append("\r\n");
            _sb.Append(' ', _depth * 2);
        }
    }
}
