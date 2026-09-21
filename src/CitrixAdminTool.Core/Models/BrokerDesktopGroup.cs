namespace CitrixAdminTool.Core.Models
{
    /// <summary>
    /// Get-BrokerDesktopGroup が返すデリバリーグループ1件分の情報（一覧表示用）。
    ///
    /// フェーズ5の第1段階（0.8）は一覧と書き出しのみ。
    /// 構成のスナップショット（全プロパティ）は 0.9 で別のモデルとして扱う。
    /// </summary>
    public class BrokerDesktopGroup
    {
        /// <summary>デリバリーグループの一意ID。後続の操作で対象を指定するために使う。</summary>
        public int Uid { get; set; }

        /// <summary>名前。</summary>
        public string Name { get; set; }

        /// <summary>Studio で利用者向けに表示される名前（PublishedName）。</summary>
        public string PublishedName { get; set; }

        /// <summary>説明。</summary>
        public string Description { get; set; }

        /// <summary>有効か。無効だと利用者はこのグループのリソースを起動できない。</summary>
        public bool Enabled { get; set; }

        /// <summary>メンテナンスモードか。</summary>
        public bool InMaintenanceMode { get; set; }

        /// <summary>種別（Private = 専有 / Shared = 共有）。</summary>
        public string DesktopKind { get; set; }

        /// <summary>配信形態（DesktopsOnly / AppsOnly / DesktopsAndApps）。</summary>
        public string DeliveryType { get; set; }

        /// <summary>セッションのサポート形態（SingleSession / MultiSession）。</summary>
        public string SessionSupport { get; set; }

        /// <summary>所属マシン数（TotalDesktops）。</summary>
        public int TotalDesktops { get; set; }

        /// <summary>利用可能なマシン数（DesktopsAvailable）。</summary>
        public int DesktopsAvailable { get; set; }

        /// <summary>使用中のマシン数（DesktopsInUse）。</summary>
        public int DesktopsInUse { get; set; }

        /// <summary>
        /// 未登録のマシン数（DesktopsUnregistered）。
        /// 0 でなければ、VDA が DDC に登録できていないマシンがある（起動しない・接続できないの主因）。
        /// </summary>
        public int DesktopsUnregistered { get; set; }

        /// <summary>セッション数（Sessions）。</summary>
        public int Sessions { get; set; }

        /// <summary>画面のメンテ列用。マシン一覧と同じ ON/OFF 表記。</summary>
        public string MaintenanceLabel
        {
            get { return InMaintenanceMode ? "ON" : "OFF"; }
        }
    }
}
