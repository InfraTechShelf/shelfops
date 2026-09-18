using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace CitrixAdminTool.Wpf.Services
{
    /// <summary>
    /// 一覧1列分の定義（見出しと、行から値を取り出す方法）。
    /// マシン一覧とセッション一覧の違いは、この列定義の配列だけに閉じ込める。
    /// </summary>
    public class ExportColumn<T>
    {
        public ExportColumn(string header, Func<T, string> getValue)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (getValue == null) throw new ArgumentNullException(nameof(getValue));

            Header = header;
            GetValue = getValue;
        }

        public string Header { get; private set; }
        public Func<T, string> GetValue { get; private set; }
    }

    /// <summary>
    /// 一覧を CSV / TSV に整形して書き出す共通処理。
    ///
    /// マシン一覧・セッション一覧の両方から使う。整形の作法（エスケープ、文字コード、
    /// 数式インジェクション対策）を1箇所に集めることで、画面ごとに実装がぶれないようにする。
    ///
    /// 外部ライブラリには依存しない（本ツール共通の方針）。
    /// </summary>
    public static class TableExporter
    {
        /// <summary>
        /// CSV は UTF-8（BOM付き）。BOM無しだと Excel で開いたときに日本語が化けるため。
        /// ログ出力（<see cref="RunLogWriter"/>）と同じ理由・同じ方針。
        /// </summary>
        private static readonly Encoding CsvEncoding = new UTF8Encoding(true);

        /// <summary>
        /// RFC 4180 に沿った CSV を作る。改行は CRLF、1行目は見出し。
        /// </summary>
        public static string ToCsv<T>(IEnumerable<T> rows, IList<ExportColumn<T>> columns)
        {
            return Build(rows, columns, ",", EscapeCsv);
        }

        /// <summary>
        /// タブ区切り（TSV）を作る。Excel へそのまま貼り付けるための形式。
        /// </summary>
        public static string ToTsv<T>(IEnumerable<T> rows, IList<ExportColumn<T>> columns)
        {
            return Build(rows, columns, "\t", EscapeTsv);
        }

        private static string Build<T>(
            IEnumerable<T> rows, IList<ExportColumn<T>> columns, string separator, Func<string, string> escape)
        {
            if (columns == null || columns.Count == 0)
                throw new ArgumentException("列定義が空です。", nameof(columns));

            var sb = new StringBuilder();

            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0) sb.Append(separator);
                sb.Append(escape(columns[i].Header));
            }
            sb.Append("\r\n");

            if (rows != null)
            {
                foreach (var row in rows)
                {
                    for (var i = 0; i < columns.Count; i++)
                    {
                        if (i > 0) sb.Append(separator);
                        sb.Append(escape(Neutralize(columns[i].GetValue(row))));
                    }
                    sb.Append("\r\n");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Excel の数式として解釈される先頭文字を無害化する。
        ///
        /// ユーザー名・クライアント端末名は利用者が自由に付けられる外部由来の文字列なので、
        /// "=..." のような値がそのまま入ると、開いた側で数式が実行されうる（CSVインジェクション）。
        /// 先頭にアポストロフィを付けて文字列として扱わせる。
        /// </summary>
        private static string Neutralize(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var head = value[0];
            if (head == '=' || head == '+' || head == '-' || head == '@')
                return "'" + value;

            return value;
        }

        /// <summary>
        /// CSV の1フィールドをエスケープする。区切り・引用符・改行を含むなら
        /// 全体を "" で囲み、内部の " は "" に二重化する（RFC 4180）。
        /// </summary>
        private static string EscapeCsv(string value)
        {
            if (value == null) return string.Empty;

            var needsQuote = value.IndexOf(',') >= 0
                             || value.IndexOf('"') >= 0
                             || value.IndexOf('\r') >= 0
                             || value.IndexOf('\n') >= 0;

            if (!needsQuote) return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// TSV の1フィールドを整える。タブや改行が混じると列・行がずれるため、
        /// 引用ではなく半角空白へ置換して1セルに収める（貼り付け先で崩れない方を優先）。
        /// </summary>
        private static string EscapeTsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value.Replace("\t", " ").Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
        }

        /// <summary>
        /// CSV をファイルへ書き出す。成功なら true、失敗なら false と理由を返す
        /// （例外を握りつぶさず、呼び出し側がステータスに出せるようにする）。
        /// </summary>
        public static bool TryWriteCsvFile(string path, string content, out string error)
        {
            error = null;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(path, content, CsvEncoding);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// クリップボードへ文字列を置く。成功なら true、失敗なら false と理由を返す。
        ///
        /// 【リトライする理由】
        /// クリップボードは一度に1プロセスしか開けず、他プロセスが掴んでいると
        /// CLIPBRD_E_CANT_OPEN で失敗する。本ツールは RDP／ICA セッション内の管理端末で
        /// 動かすことが多く、クリップボードリダイレクトが絡んで実際に発生する。
        ///
        /// WPFの <see cref="Clipboard.SetDataObject(object, bool)"/> には
        /// WinForms版のようなリトライ引数が無いため、ここで自前に取り直す。
        /// </summary>
        public static bool TrySetClipboard(string text, out string error)
        {
            const int maxAttempts = 10;
            const int retryDelayMs = 100;

            error = null;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    // copy: true = このアプリを閉じてもクリップボードに残す
                    Clipboard.SetDataObject(text ?? string.Empty, true);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    if (attempt >= maxAttempts) return false;

                    // 掴んでいる相手が離すのを待つ。最大でも1秒程度で諦める。
                    Thread.Sleep(retryDelayMs);
                }
            }
        }

        /// <summary>
        /// 既定のファイル名を作る。メタ情報（種別・サイト・日時）はCSVの中身ではなく
        /// ファイル名側に持たせる（先頭行に入れると Excel で表として扱えなくなるため）。
        /// </summary>
        public static string BuildFileName(string kind, string siteId)
        {
            return string.Format("ShelfOps_{0}_{1}_{2:yyyyMMdd_HHmmss}.csv",
                kind, SanitizeForFileName(siteId), DateTime.Now);
        }

        private static string SanitizeForFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "site";

            var sb = new StringBuilder(value.Length);
            var invalid = Path.GetInvalidFileNameChars();

            foreach (var c in value)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

            return sb.ToString();
        }
    }
}
