namespace CitrixAdminTool.Core.Models
{
    /// <summary>
    /// Get-BrokerMachine が返すマシン1台分の情報（UI表示・操作対象）。
    ///
    /// プロパティ名はCVADのバージョンで揺れる可能性があるため、
    /// 取得側（BrokerOperationService）で候補を試して埋める。
    /// </summary>
    public class BrokerMachine
    {
        /// <summary>マシン名（例 "DOMAIN\\MACHINE"）。Set-BrokerMachine の対象キーに使う。</summary>
        public string MachineName { get; set; }

        /// <summary>DNS名（例 "machine.corp.example.com"）。</summary>
        public string DnsName { get; set; }

        /// <summary>所属するマシンカタログ名。</summary>
        public string CatalogName { get; set; }

        /// <summary>所属するデリバリーグループ名（未割り当てなら空）。</summary>
        public string DeliveryGroupName { get; set; }

        /// <summary>登録状態（Registered / Unregistered など）。</summary>
        public string RegistrationState { get; set; }

        /// <summary>電源状態（On / Off / Unknown など）。</summary>
        public string PowerState { get; set; }

        /// <summary>集約状態（Available / InUse / Disconnected など）。</summary>
        public string SummaryState { get; set; }

        /// <summary>
        /// セッションのサポート形態（Get-BrokerMachine の SessionSupport）。
        /// "SingleSession"（VDI等・1ユーザー専有）または "MultiSession"（共有デスクトップ/アプリ）。
        /// </summary>
        public string SessionSupport { get; set; }

        /// <summary>
        /// このマシンに割り当てられているユーザー（Get-BrokerMachine の AssociatedUserNames）。
        /// SDKからは配列で返るため、取得側で "; " 区切りの1つの文字列にまとめている。
        ///
        /// 静的割り当て（専有デスクトップ）でのみ値が入る。
        /// マルチセッションのマシンでは通常 空 になるが、これは正常
        /// （ログオン中のユーザーはセッション一覧側で見る）。
        /// </summary>
        public string AssociatedUserNames { get; set; }

        /// <summary>メンテナンスモードか。</summary>
        public bool InMaintenanceMode { get; set; }

        /// <summary>このマシン上のセッション数。</summary>
        public int SessionCount { get; set; }

        /// <summary>
        /// メンテナンスモードの画面表示。チェックボックスだと「行の選択」と紛らわしいため、
        /// ON / OFF の文字で示す（チェックボックスは複数選択専用に役割を統一する）。
        /// CSV等の書き出しでも同じ表記を使い、画面とファイルで食い違わないようにする。
        /// </summary>
        public string MaintenanceLabel
        {
            get { return InMaintenanceMode ? "ON" : "OFF"; }
        }

    }
}
