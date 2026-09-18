using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CitrixAdminTool.Core.Configuration.Json
{
    /// <summary>JSONの構文エラー。行・列を含むメッセージを持つ。</summary>
    public class JsonParseException : Exception
    {
        public int Line { get; private set; }
        public int Column { get; private set; }

        public JsonParseException(string message, int line, int column)
            : base(string.Format(CultureInfo.InvariantCulture,
                   "{0} ({1}行 {2}列目)", message, line, column))
        {
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// 依存ライブラリを持たない最小限のJSONパーサ。
    ///
    /// 【なぜ自前実装か】
    /// 本ツールは「CVAD管理環境でインストール不要・単一フォルダ自己完結」が必須要件であり、
    /// NuGetパッケージ（Json.NET等）を持ち込まない構成にしている。
    /// .NET Framework標準のシリアライザは、要求されたJSON形式
    /// （camelCaseのキー・enumの文字列表現）を素直に扱えないため、
    /// 用途を sites.json に絞った小さなパーサを用意した。
    ///
    /// 【意図的な寛容さ】
    /// sites.json は管理端末上でメモ帳などで手編集される想定のため、
    /// 厳密なJSONより実務的な以下を許容する:
    ///   - 末尾カンマ
    ///   - // 行コメント と /* ブロックコメント */
    ///   - 先頭のBOM
    ///
    /// パース結果の型:
    ///   オブジェクト → Dictionary&lt;string, object&gt;
    ///   配列         → List&lt;object&gt;
    ///   文字列       → string
    ///   数値         → double
    ///   真偽値       → bool
    ///   null         → null
    /// </summary>
    public static class JsonParser
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var reader = new Cursor(text);
            reader.SkipTrivia();
            var value = ParseValue(reader);
            reader.SkipTrivia();

            if (!reader.AtEnd)
                throw reader.Error("JSONの終端より後に余分な文字があります。");

            return value;
        }

        private static object ParseValue(Cursor c)
        {
            if (c.AtEnd) throw c.Error("値が必要な位置で入力が終わりました。");

            char ch = c.Current;
            switch (ch)
            {
                case '{': return ParseObject(c);
                case '[': return ParseArray(c);
                case '"': return ParseString(c);
                case 't': return ParseLiteral(c, "true", true);
                case 'f': return ParseLiteral(c, "false", false);
                case 'n': return ParseLiteral(c, "null", null);
                default:
                    if (ch == '-' || (ch >= '0' && ch <= '9')) return ParseNumber(c);
                    throw c.Error("予期しない文字 '" + ch + "' です。");
            }
        }

        private static Dictionary<string, object> ParseObject(Cursor c)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            c.Advance(); // '{'
            c.SkipTrivia();

            if (c.TryConsume('}')) return result;

            while (true)
            {
                c.SkipTrivia();

                // 末尾カンマの許容: '}' が来たら終了
                if (c.TryConsume('}')) return result;

                if (c.AtEnd || c.Current != '"')
                    throw c.Error("オブジェクトのキー（\"...\"）が必要です。");

                string key = ParseString(c);

                c.SkipTrivia();
                if (!c.TryConsume(':'))
                    throw c.Error("キー \"" + key + "\" の後に ':' が必要です。");

                c.SkipTrivia();
                result[key] = ParseValue(c);

                c.SkipTrivia();
                if (c.TryConsume(',')) continue;
                if (c.TryConsume('}')) return result;

                throw c.Error("オブジェクト内で ',' または '}' が必要です。");
            }
        }

        private static List<object> ParseArray(Cursor c)
        {
            var result = new List<object>();

            c.Advance(); // '['
            c.SkipTrivia();

            if (c.TryConsume(']')) return result;

            while (true)
            {
                c.SkipTrivia();

                // 末尾カンマの許容
                if (c.TryConsume(']')) return result;

                result.Add(ParseValue(c));

                c.SkipTrivia();
                if (c.TryConsume(',')) continue;
                if (c.TryConsume(']')) return result;

                throw c.Error("配列内で ',' または ']' が必要です。");
            }
        }

        private static string ParseString(Cursor c)
        {
            c.Advance(); // 開きクォート

            var sb = new StringBuilder();
            while (true)
            {
                if (c.AtEnd) throw c.Error("文字列が閉じられていません。");

                char ch = c.Current;
                c.Advance();

                if (ch == '"') return sb.ToString();

                if (ch != '\\')
                {
                    sb.Append(ch);
                    continue;
                }

                if (c.AtEnd) throw c.Error("エスケープ文字の後で入力が終わりました。");

                char esc = c.Current;
                c.Advance();
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
                        if (c.Remaining < 4) throw c.Error("\\u エスケープが不完全です。");
                        string hex = c.Take(4);
                        int code;
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            throw c.Error("\\u エスケープの16進数が不正です: " + hex);
                        sb.Append((char)code);
                        break;
                    default:
                        throw c.Error("未知のエスケープシーケンス \\" + esc + " です。");
                }
            }
        }

        private static object ParseNumber(Cursor c)
        {
            int start = c.Position;

            if (c.Current == '-') c.Advance();
            while (!c.AtEnd && IsNumberChar(c.Current)) c.Advance();

            string raw = c.Substring(start, c.Position - start);

            double value;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw c.Error("数値として解釈できません: " + raw);

            return value;
        }

        private static bool IsNumberChar(char ch)
        {
            return (ch >= '0' && ch <= '9') || ch == '.' || ch == 'e' || ch == 'E' || ch == '+' || ch == '-';
        }

        private static object ParseLiteral(Cursor c, string literal, object value)
        {
            if (c.Remaining < literal.Length || c.Substring(c.Position, literal.Length) != literal)
                throw c.Error("'" + literal + "' が必要な位置に別の文字があります。");

            c.Take(literal.Length);
            return value;
        }

        /// <summary>入力文字列上の位置と、行・列の追跡を担う内部カーソル。</summary>
        private sealed class Cursor
        {
            private readonly string _text;
            private int _pos;
            private int _line = 1;
            private int _col = 1;

            public Cursor(string text)
            {
                // BOMが残っていると先頭の値のパースに失敗するため取り除く。
                _text = (text.Length > 0 && text[0] == '\uFEFF') ? text.Substring(1) : text;
            }

            public int Position { get { return _pos; } }
            public bool AtEnd { get { return _pos >= _text.Length; } }
            public int Remaining { get { return _text.Length - _pos; } }
            public char Current { get { return _text[_pos]; } }

            public string Substring(int start, int length) { return _text.Substring(start, length); }

            public void Advance()
            {
                if (AtEnd) return;

                if (_text[_pos] == '\n') { _line++; _col = 1; }
                else { _col++; }

                _pos++;
            }

            public string Take(int count)
            {
                int start = _pos;
                for (int i = 0; i < count; i++) Advance();
                return _text.Substring(start, _pos - start);
            }

            public bool TryConsume(char expected)
            {
                if (AtEnd || _text[_pos] != expected) return false;
                Advance();
                return true;
            }

            /// <summary>空白とコメントを読み飛ばす。</summary>
            public void SkipTrivia()
            {
                while (!AtEnd)
                {
                    char ch = _text[_pos];

                    if (char.IsWhiteSpace(ch)) { Advance(); continue; }

                    if (ch == '/' && Remaining >= 2)
                    {
                        char next = _text[_pos + 1];

                        if (next == '/')
                        {
                            while (!AtEnd && _text[_pos] != '\n') Advance();
                            continue;
                        }

                        if (next == '*')
                        {
                            Advance(); Advance();
                            while (!AtEnd && !(_text[_pos] == '*' && Remaining >= 2 && _text[_pos + 1] == '/'))
                                Advance();
                            if (AtEnd) throw Error("ブロックコメントが閉じられていません。");
                            Advance(); Advance();
                            continue;
                        }
                    }

                    return;
                }
            }

            public JsonParseException Error(string message)
            {
                return new JsonParseException(message, _line, _col);
            }
        }
    }
}
