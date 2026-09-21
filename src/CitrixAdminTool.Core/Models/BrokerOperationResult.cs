using System.Collections.Generic;

namespace CitrixAdminTool.Core.Models
{
    /// <summary>
    /// Broker管理操作（マシン一覧・メンテナンスモード切替など）1回分の結果。
    ///
    /// 接続テスト（<see cref="SiteConnectionResult"/>）とは別に、汎用の操作結果として持つ。
    /// 読み取り操作では <see cref="Machines"/> にデータを載せ、
    /// 書き込み操作では <see cref="Message"/> に結果概要を載せる。
    /// </summary>
    public class BrokerOperationResult
    {
        /// <summary>操作名（"listMachines" / "setMaintenance" など）。</summary>
        public string Operation { get; set; }

        /// <summary>操作が成功したか。</summary>
        public bool Success { get; set; }

        /// <summary>実際に操作を実行したDDCのFQDN（フェイルオーバーの結果）。</summary>
        public string WorkingDdc { get; set; }

        /// <summary>成功時の概要メッセージ（例「メンテナンスモードをONにしました」）。</summary>
        public string Message { get; set; }

        /// <summary>失敗時のエラーメッセージ（生メッセージ）。</summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// このマシンにCitrix SDKが無いために失敗した場合に true。
        /// DDCの到達性ではなく実行環境側の問題。
        /// </summary>
        public bool SdkUnavailable { get; set; }

        /// <summary>
        /// 別資格情報モードで接続に使うことを指定されたアカウント名（GUIが刻む）。
        /// 統合認証の場合は null。「誰の権限で操作したか」をログに残すため。
        /// </summary>
        public string RequestedIdentity { get; set; }

        /// <summary>読み取り操作（マシン一覧）の結果。</summary>
        public List<BrokerMachine> Machines { get; set; } = new List<BrokerMachine>();

        /// <summary>読み取り操作（セッション一覧）の結果。</summary>
        public List<BrokerSession> Sessions { get; set; } = new List<BrokerSession>();

        /// <summary>読み取り操作（デリバリーグループ一覧）の結果。</summary>
        public List<BrokerDesktopGroup> DesktopGroups { get; set; } = new List<BrokerDesktopGroup>();

        /// <summary>
        /// 対象ごとの結果。一括操作では対象の数だけ積まれ、単一操作でも1件積む。
        /// 「どの対象が失敗したか」を画面とログの両方で示すために使う。
        /// 読み取り操作（一覧取得）では空のまま。
        /// </summary>
        public List<BrokerTargetResult> Targets { get; set; } = new List<BrokerTargetResult>();

        /// <summary>持ち帰り分析用の診断情報。</summary>
        public List<string> Diagnostics { get; set; } = new List<string>();

        /// <summary><see cref="Targets"/> のうち成功した件数。</summary>
        public int SuccessCount
        {
            get { return Targets == null ? 0 : Targets.FindAll(t => t.Success).Count; }
        }

        /// <summary><see cref="Targets"/> のうち失敗した件数。</summary>
        public int FailureCount
        {
            get { return Targets == null ? 0 : Targets.FindAll(t => !t.Success).Count; }
        }

        /// <summary>一括操作（対象が2件以上）だったか。画面の文言を切り替えるために使う。</summary>
        public bool IsBulk
        {
            get { return Targets != null && Targets.Count > 1; }
        }

        public static BrokerOperationResult Fail(string operation, string error)
        {
            return new BrokerOperationResult
            {
                Operation = operation,
                Success = false,
                ErrorMessage = error
            };
        }
    }
}
