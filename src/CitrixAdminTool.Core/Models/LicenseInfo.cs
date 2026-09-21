using System.Collections.Generic;

namespace CitrixAdminTool.Core.Models
{
    /// <summary>
    /// サイトのライセンス構成と、ライセンスサーバーとの接続状況。
    ///
    /// 接続テストが既に呼んでいる Get-BrokerSite / Get-BrokerController から取り出す。
    /// 新しいコマンドレットは使わない（権限・往復回数を増やさないため）。
    ///
    /// 値は SDK の英語表記（"UserDevice" / "OK" など）をそのまま持つ。
    /// 訳すと Citrix の文書やサポート情報と突き合わせにくくなるため、画面でもそのまま出す。
    /// </summary>
    public class LicenseInfo
    {
        /// <summary>ライセンスサーバーの FQDN（Get-BrokerSite の LicenseServerName）。</summary>
        public string LicenseServerName { get; set; }

        /// <summary>ライセンスサーバーのポート（同 LicenseServerPort。通常 27000）。</summary>
        public string LicenseServerPort { get; set; }

        /// <summary>製品コード（XDT = Virtual Apps and Desktops / MPS = Virtual Apps）。</summary>
        public string ProductCode { get; set; }

        /// <summary>エディション（PLT / ENT / ADV / STD など）。</summary>
        public string ProductEdition { get; set; }

        /// <summary>ライセンスモデル（UserDevice / Concurrent）。</summary>
        public string LicensingModel { get; set; }

        /// <summary>
        /// 猶予期間中か（Get-BrokerSite の LicensingGracePeriodActive）。
        /// true は「ライセンスサーバーと通信できていない」ことを意味し、
        /// 猶予が切れると新規セッションが拒否される。接続テストの成否より先に気づくべき状態。
        /// 取得できなければ null。
        /// </summary>
        public bool? GracePeriodActive { get; set; }

        /// <summary>猶予期間の残り時間（同 LicensingGraceHoursLeft）。取得できなければ null。</summary>
        public int? GraceHoursLeft { get; set; }

        /// <summary>
        /// DDC ごとのライセンスサーバー接続状況。
        /// サイト全体で1つの値ではなく、「DDC-02 だけ繋がっていない」が起きるため個別に持つ。
        /// </summary>
        public List<ControllerLicenseStatus> Controllers { get; set; } = new List<ControllerLicenseStatus>();

        /// <summary>1台でも「OK」以外の DDC があるか。画面で警告を出す判定に使う。</summary>
        public bool HasControllerProblem
        {
            get
            {
                foreach (var c in Controllers)
                {
                    if (!string.IsNullOrEmpty(c.LicensingServerState) &&
                        !c.LicensingServerState.Equals("OK", System.StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
        }
    }

    /// <summary>DDC 1台分のライセンス関連の状態（Get-BrokerController）。</summary>
    public class ControllerLicenseStatus
    {
        /// <summary>DDC の FQDN（DNSName）。</summary>
        public string DnsName { get; set; }

        /// <summary>
        /// ライセンスサーバーとの接続状態（LicensingServerState）。
        /// OK / NotConnected / ServerNotSpecified / LicenseNotInstalled / LicenseExpired / Incompatible / Failed。
        /// </summary>
        public string LicensingServerState { get; set; }

        /// <summary>
        /// 猶予状態（LicensingGraceState）。
        /// NotActive / InOutOfBoxGracePeriod / InSupplementalGracePeriod / InEmergencyGracePeriod / GracePeriodExpired。
        /// </summary>
        public string LicensingGraceState { get; set; }

        /// <summary>DDC の状態（State。Active など）。</summary>
        public string State { get; set; }
    }
}
