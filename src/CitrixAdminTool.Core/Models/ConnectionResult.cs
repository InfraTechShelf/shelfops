using System;
using System.Collections.Generic;

namespace CitrixAdminTool.Core.Models
{
    /// <summary>
    /// 1つのDDCに対する接続試行の結果。
    /// </summary>
    public class ConnectionResult
    {
        /// <summary>接続に成功したかどうか。</summary>
        public bool Success { get; set; }

        /// <summary>実際に接続に成功した（または最後に試行した）DDCのFQDN。</summary>
        public string DdcAddress { get; set; }

        /// <summary>Get-BrokerSiteから取得したサイト名（成功時）。</summary>
        public string SiteName { get; set; }

        /// <summary>
        /// 取得したCVADのバージョン（成功時、取得できれば）。
        /// Get-BrokerSite ではなく Get-BrokerController の ControllerVersion から取る。
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// サイトの機能レベル（例: "L7_20"）。Get-BrokerSite の DefaultMinimumFunctionalLevel。
        /// 製品バージョンとは別物で、どの世代のBroker機能が使えるかを示す。
        /// </summary>
        public string FunctionalLevel { get; set; }

        /// <summary>
        /// プロセスを実行しているWindowsユーザー名。**確認済みの事実**。
        /// 統合Windows認証ではこのアカウントでDDCに認証される。
        /// </summary>
        public string AuthenticatedAs { get; set; }

        /// <summary>
        /// 別資格情報モードで**接続に使うことを指定された**アカウント名。
        /// 統合認証の場合は null。
        ///
        /// 【重要】これは確認済みの事実ではない。DDCが実際にどちらのアカウントを
        /// 認証したかをツールから知る手段が無いため、報告では
        /// <see cref="AuthenticatedAs"/> と区別して「指定値」として扱うこと。
        /// </summary>
        public string RequestedIdentity { get; set; }

        /// <summary>
        /// コマンドレット実行時にスレッド偽装トークンが有効だったか。
        ///
        /// 偽装が「かかっていたか」だけを示す。かかっていても、HTTP接続の再利用などで
        /// 実際のネットワーク認証が別のアカウントになる可能性は排除できない。
        /// </summary>
        public bool? ImpersonationActive { get; set; }

        /// <summary>失敗時のエラーメッセージ（PowerShellの生メッセージ）。</summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 失敗時の追加診断情報（例外の型名・スタックトレース・SDKロード経路など）。
        /// 検証環境にはClaude Codeが無く、ログを持ち帰って分析する必要があるため、
        /// 生メッセージだけでは足りない情報をここに積む。
        /// </summary>
        public List<string> Diagnostics { get; set; } = new List<string>();

        /// <summary>
        /// Citrix SDKをどの方式でロードできたか（"Module" / "PSSnapin" / null）。
        /// 環境ごとのSDK提供形態の違いを持ち帰り分析するための情報。
        /// </summary>
        public string SdkLoadMethod { get; set; }

        /// <summary>
        /// 失敗の原因が「このマシンにCitrix SDKが無い」ことである場合に true。
        /// DDCを変えても結果は変わらないため、呼び出し側は以降の試行を打ち切ってよい。
        /// </summary>
        public bool IsSdkUnavailable { get; set; }

        /// <summary>試行にかかった時間。</summary>
        public TimeSpan Elapsed { get; set; }

        /// <summary>
        /// ライセンス構成とライセンスサーバーとの接続状況（成功時、取得できれば）。
        /// 疎通確認の副産物として Get-BrokerSite / Get-BrokerController から取る。
        /// 取れなくても接続失敗にはしない（補助情報）。
        /// </summary>
        public LicenseInfo License { get; set; }

        public static ConnectionResult Ok(string ddc, string siteName, string version, string authAs, TimeSpan elapsed)
        {
            return new ConnectionResult
            {
                Success = true,
                DdcAddress = ddc,
                SiteName = siteName,
                Version = version,
                AuthenticatedAs = authAs,
                Elapsed = elapsed
            };
        }

        public static ConnectionResult Fail(string ddc, string error, TimeSpan elapsed)
        {
            return new ConnectionResult
            {
                Success = false,
                DdcAddress = ddc,
                ErrorMessage = error,
                Elapsed = elapsed
            };
        }
    }

    /// <summary>
    /// サイト全体（複数DDC）に対する接続試行の集約結果。
    /// </summary>
    public class SiteConnectionResult
    {
        public string SiteId { get; set; }
        public string SiteDisplayName { get; set; }

        /// <summary>いずれかのDDCに接続できたか。</summary>
        public bool Success { get; set; }

        /// <summary>成功した試行（成功時）。</summary>
        public ConnectionResult SuccessfulAttempt { get; set; }

        /// <summary>
        /// Citrix SDKがこのマシンに無いために試行を打ち切った場合に true。
        /// 「DDCに繋がらなかった」のではなく「実行環境が違う」ことを示す。
        /// </summary>
        public bool SdkUnavailable { get; set; }

        /// <summary>各DDCへの試行結果（フェイルオーバーの記録）。</summary>
        public List<ConnectionResult> Attempts { get; set; } = new List<ConnectionResult>();
    }
}
