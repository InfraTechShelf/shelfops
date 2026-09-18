namespace CitrixAdminTool.Core.Models
{
    /// <summary>
    /// 一括操作における「対象1件分の結果」。
    ///
    /// 複数マシンのメンテナンスモード切替や、複数セッションのログオフでは、
    /// 権限やマシン状態によって**一部だけ失敗する**ことが現実に起きる。
    /// 「成功 8 / 失敗 2」という集計だけでは、どれが失敗したのか分からず対処できないため、
    /// 対象ごとに1件ずつこの結果を積む。
    ///
    /// 単一対象の操作でも同じ構造で1件だけ積むので、画面・ログの整形は共通に扱える。
    /// </summary>
    public class BrokerTargetResult
    {
        /// <summary>対象の表示名（マシン名、またはセッションの「ユーザー / マシン」）。</summary>
        public string TargetName { get; set; }

        /// <summary>この対象への操作が成功したか。</summary>
        public bool Success { get; set; }

        /// <summary>失敗時の理由。成功時は null。</summary>
        public string ErrorMessage { get; set; }

        public static BrokerTargetResult Ok(string targetName)
        {
            return new BrokerTargetResult { TargetName = targetName, Success = true };
        }

        public static BrokerTargetResult Ng(string targetName, string error)
        {
            return new BrokerTargetResult { TargetName = targetName, Success = false, ErrorMessage = error };
        }
    }
}
