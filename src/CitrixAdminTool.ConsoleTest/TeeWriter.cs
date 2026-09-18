using System;
using System.IO;
using System.Text;

namespace CitrixAdminTool.ConsoleTest
{
    /// <summary>
    /// コンソールとログファイルの両方へ同じ内容を書き出す。
    ///
    /// 検証環境（CVAD管理環境）にはClaude Codeも開発ツールも無いため、
    /// 画面で見た内容をそのままファイルとして開発環境へ持ち帰れることが重要。
    /// そのため出力は必ずこのクラス経由で行う。
    ///
    /// ログの書き込み先が作れない場合（実行フォルダが読み取り専用の共有上にある等）は
    /// %TEMP% にフォールバックし、それも無理ならコンソール出力だけを続行する。
    /// ログが取れないことを理由に接続検証そのものを止めてしまわない方針。
    /// </summary>
    public sealed class TeeWriter : IDisposable
    {
        private StreamWriter _file;

        /// <summary>実際に書き出しているログファイルのパス。取得できなければ null。</summary>
        public string LogPath { get; private set; }

        /// <summary>ログファイルを開けなかった場合の理由。開けていれば null。</summary>
        public string LogFailureReason { get; private set; }

        /// <summary>
        /// true にすると <see cref="WriteDetailLine"/> の内容もコンソールへ出す。
        /// 既定では詳細（スタックトレース等）はログファイルにのみ記録し、
        /// 画面は結果が読み取れる粒度に保つ。
        /// </summary>
        public bool VerboseConsole { get; set; }

        private TeeWriter(StreamWriter file, string logPath, string failureReason)
        {
            _file = file;
            LogPath = logPath;
            LogFailureReason = failureReason;
        }

        /// <summary>
        /// タイムスタンプ付きのログファイルを作って TeeWriter を返す。
        /// </summary>
        /// <param name="preferredDirectory">第一候補のフォルダ（通常は実行フォルダ）。</param>
        public static TeeWriter Create(string preferredDirectory)
        {
            var fileName = string.Format("ShelfOps_{0:yyyyMMdd_HHmmss}.log", DateTime.Now);

            var candidates = new[]
            {
                preferredDirectory,
                Path.GetTempPath()
            };

            string lastError = null;

            foreach (var dir in candidates)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;

                try
                {
                    var path = Path.Combine(dir, fileName);

                    // UTF-8 BOM付き。管理端末のメモ帳で開いても日本語が化けないようにする。
                    var writer = new StreamWriter(path, false, new UTF8Encoding(true));
                    writer.AutoFlush = true;

                    return new TeeWriter(writer, path, null);
                }
                catch (Exception ex)
                {
                    lastError = dir + " : " + ex.Message;
                }
            }

            return new TeeWriter(null, null, lastError ?? "書き込み可能なフォルダが見つかりませんでした。");
        }

        public void WriteLine()
        {
            WriteLine(string.Empty);
        }

        /// <summary>
        /// 詳細情報を書く。ログファイルには必ず残り、コンソールには
        /// <see cref="VerboseConsole"/> が true のときだけ出る。
        /// 持ち帰り分析用の情報は落とさず、画面の可読性は保つための区別。
        /// </summary>
        public void WriteDetailLine(string text)
        {
            WriteLine(text, VerboseConsole);
        }

        public void WriteLine(string text)
        {
            WriteLine(text, true);
        }

        private void WriteLine(string text, bool toConsole)
        {
            if (toConsole) Console.WriteLine(text);

            if (_file == null) return;

            try
            {
                _file.WriteLine(text);
            }
            catch (Exception ex)
            {
                // 書き込みが途中で失敗しても検証は続ける。以降はコンソールのみ。
                LogFailureReason = "ログ書き込み中にエラー: " + ex.Message;
                LogPath = null;

                var broken = _file;
                _file = null;
                try { broken.Dispose(); } catch { /* 破棄失敗は無視 */ }
            }
        }

        public void WriteLine(string format, params object[] args)
        {
            WriteLine(string.Format(format, args));
        }

        public void Dispose()
        {
            if (_file == null) return;

            try { _file.Dispose(); } catch { /* 破棄失敗は無視 */ }
        }
    }
}
