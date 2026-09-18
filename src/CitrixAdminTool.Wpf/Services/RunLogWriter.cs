using System;
using System.IO;
using System.Text;

namespace CitrixAdminTool.Wpf.Services
{
    /// <summary>
    /// 接続結果をタイムスタンプ付きログファイルへ書き出す。
    ///
    /// GUI版でもログを残す理由はコンソール版と同じで、
    /// 検証・障害調査の情報を開発環境へ持ち帰れるようにするため。
    /// 保存先は %APPDATA%\CitrixAdminTool\logs\（設定ファイルと同じ場所に集約する）。
    /// </summary>
    public static class RunLogWriter
    {
        public static string GetLogDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ShelfOps",
                "logs");
        }

        /// <summary>
        /// ログを書き出し、そのパスを返す。書き出せなかった場合は null を返す
        /// （ログが残せないことを理由に接続テストの結果を捨てないため）。
        /// </summary>
        public static string Write(string content, out string failureReason)
        {
            failureReason = null;

            try
            {
                var dir = GetLogDirectory();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // ミリ秒まで含める。同一秒に複数の操作（一覧→メンテナンス切替など）を
                // 行っても、ファイル名が衝突して上書きされないようにするため。
                var path = Path.Combine(dir,
                    string.Format("ShelfOps_{0:yyyyMMdd_HHmmss_fff}.log", DateTime.Now));

                // BOM付きUTF-8。管理端末のメモ帳で開いても日本語が化けないようにする。
                File.WriteAllText(path, content, new UTF8Encoding(true));
                return path;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return null;
            }
        }
    }
}
