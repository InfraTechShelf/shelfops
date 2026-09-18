namespace CitrixAdminTool.Core.Models
{
    /// <summary>
    /// Get-BrokerSession が返すセッション1件分の情報（UI表示・操作対象）。
    /// ログオフ・切断の対象は <see cref="Uid"/> で特定する。
    /// </summary>
    public class BrokerSession
    {
        /// <summary>セッションの一意ID。ログオフ／切断の対象指定に使う。</summary>
        public long Uid { get; set; }

        /// <summary>ログオンしているユーザー（例 "DOMAIN\\user"）。</summary>
        public string UserName { get; set; }

        /// <summary>セッションをホストしているマシン名。</summary>
        public string MachineName { get; set; }

        /// <summary>所属するデリバリーグループ名。</summary>
        public string DeliveryGroupName { get; set; }

        /// <summary>セッション状態（Active / Disconnected など）。</summary>
        public string SessionState { get; set; }

        /// <summary>接続元のクライアント端末名。</summary>
        public string ClientName { get; set; }

        /// <summary>セッション開始時刻（文字列表現）。</summary>
        public string StartTime { get; set; }

        /// <summary>
        /// セッション状態が最後に変わった時刻（文字列表現）。
        /// 「いつから切断されたままか」等の判断に使う（Get-BrokerSession の SessionStateChangeTime）。
        /// </summary>
        public string SessionStateChangeTime { get; set; }
    }
}
