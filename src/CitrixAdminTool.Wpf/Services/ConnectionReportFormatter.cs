using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CitrixAdminTool.Core.Models;
using CitrixAdminTool.Wpf.Services.Localization;

namespace CitrixAdminTool.Wpf.Services
{
    /// <summary>
    /// 接続結果を人が読めるテキストに整形する。
    ///
    /// GUIでも整形済みテキストを表示・記録する方針にしている。
    /// 検証環境から情報を持ち帰る手段はログファイルであり、
    /// 画面に出ているものとログの内容が一致していた方が突き合わせやすいため。
    /// </summary>
    public static class ConnectionReportFormatter
    {
        /// <summary>
        /// ログの項目見出しを一定幅に揃えて "見出し : " の形にする。
        ///
        /// 見出しの文字数は言語で変わる（「サイト」/ "Site"）ため、
        /// 元のように空白を直接書き込むと英語で桁が崩れる。ここで幅を揃える。
        /// 全角は2文字幅として数える。
        /// </summary>
        private static string Field(string key)
        {
            const int width = 14;

            var label = Loc.T(key);
            var visualWidth = 0;
            foreach (var c in label) visualWidth += c < 0x100 ? 1 : 2;

            var pad = width - visualWidth;
            return label + (pad > 0 ? new string(' ', pad) : string.Empty) + ": ";
        }


        /// <summary>
        /// Broker管理操作（マシン一覧・メンテナンスモード切替）の結果をログ用テキストに整形する。
        /// 特にメンテナンス切替は書き込み操作なので、監査記録として何を・どのアカウントで
        /// 行い、成否がどうだったかを残す。
        /// </summary>
        public static string FormatOperationReport(string siteLabel, BrokerOperationResult r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));

            var sb = new StringBuilder();
            sb.Append(FormatHeader());
            sb.AppendLine();
            sb.AppendLine(Field("Log_Site") + (siteLabel ?? Loc.T("Common_Unknown")));
            sb.AppendLine(Field("Log_Operation") + OperationLabel(r.Operation));
            sb.AppendLine(Field("Log_Auth") + (r.RequestedIdentity != null
                ? Loc.F("Log_AuthCredential", r.RequestedIdentity)
                : Loc.T("Log_AuthIntegrated")));
            if (r.WorkingDdc != null)
                sb.AppendLine(Field("Log_Ddc") + r.WorkingDdc);

            if (r.Success)
            {
                sb.AppendLine(Field("Log_Result") + Loc.T("Log_ResultSuccess"));
                if (!string.IsNullOrEmpty(r.Message))
                    sb.AppendLine("  " + r.Message);
                if (r.Machines != null && r.Machines.Count > 0)
                    AppendMachineTable(sb, r.Machines);
                if (r.Sessions != null && r.Sessions.Count > 0)
                    AppendSessionTable(sb, r.Sessions);
                if (r.DesktopGroups != null && r.DesktopGroups.Count > 0)
                    AppendDesktopGroupTable(sb, r.DesktopGroups);
            }
            else if (r.SdkUnavailable)
            {
                sb.AppendLine(Field("Log_Result") + Loc.T("Log_ResultSdkMissing"));
                sb.AppendLine("  " + Loc.T("Log_SdkHint"));
            }
            else
            {
                sb.AppendLine(Field("Log_Result") + Loc.T("Log_ResultFailed"));
                sb.AppendLine("  " + Loc.T("Log_Reason") + ": " + (r.ErrorMessage ?? Loc.T("Common_UnknownError")));
            }

            // 対象ごとの結果（一括操作の監査記録）。
            // 画面の「内訳」と同じ情報をログにも必ず残す。失敗を先に並べる。
            if (r.Targets != null && r.Targets.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  " + Loc.F("Log_TargetResults",
                    r.SuccessCount, r.FailureCount, r.Targets.Count) + ":");

                foreach (var t in r.Targets.OrderBy(t => t.Success))
                {
                    sb.AppendLine(string.Format("  [{0}] {1}{2}",
                        Loc.T(t.Success ? "Log_ResultSuccess" : "Log_ResultFailed"),
                        t.TargetName ?? Loc.T("Common_Unknown"),
                        t.Success ? string.Empty : " : " + (t.ErrorMessage ?? Loc.T("Common_UnknownError"))));
                }
            }

            if (r.Diagnostics != null)
            {
                foreach (var line in r.Diagnostics)
                    sb.AppendLine("    | " + line);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 一覧の持ち出し（CSV保存・クリップボードコピー）の記録。
        ///
        /// 「誰がいつ何件持ち出したか」を監査記録として残す。
        /// 一覧の中身そのものは書かない（ログが肥大化するため）。
        /// </summary>
        public static string FormatExportReport(
            string siteLabel, string listName, string action, int count, string path)
        {
            var sb = new StringBuilder();
            sb.Append(FormatHeader());
            sb.AppendLine();
            sb.AppendLine(Field("Log_Site") + (siteLabel ?? Loc.T("Common_Unknown")));
            sb.AppendLine(Field("Log_Operation") + Loc.F("Log_ExportTitle", listName, action));
            sb.AppendLine(Field("Log_ExportCount") + count);
            if (!string.IsNullOrEmpty(path))
                sb.AppendLine(Field("Log_ExportPath") + path);

            return sb.ToString();
        }

        private static string OperationLabel(string op)
        {
            switch (op)
            {
                case "listMachines": return Loc.T("Op_ListMachines");
                case "setMaintenance": return Loc.T("Op_SetMaintenance");
                case "setMaintenanceBulk": return Loc.T("Op_SetMaintenance");
                case "listSessions": return Loc.T("Op_ListSessions");
                case "listDesktopGroups": return Loc.T("Op_ListDesktopGroups");
                case "logoffSession": return Loc.T("Op_LogoffSession");
                case "logoffSessionsBulk": return Loc.T("Op_LogoffSession");
                case "disconnectSession": return Loc.T("Op_DisconnectSession");
                case "disconnectSessionsBulk": return Loc.T("Op_DisconnectSession");
                case "powerAction": return Loc.T("Op_PowerAction");
                case "powerActionBulk": return Loc.T("Op_PowerAction");
                default: return op ?? Loc.T("Common_Unknown");
            }
        }

        private static void AppendSessionTable(StringBuilder sb, IList<BrokerSession> sessions)
        {
            sb.AppendLine();
            sb.AppendLine("  " + Loc.F("Log_SessionTable", sessions.Count) + ":");
            sb.AppendLine("  " + string.Join(" | ", new[]
            {
                Loc.T("Col_User"), Loc.T("Col_Machine"), Loc.T("Col_DeliveryGroup"), Loc.T("Col_State"),
                Loc.T("Col_Client"), Loc.T("Col_StartTime"), Loc.T("Col_StateChangeTime")
            }));
            foreach (var s in sessions)
            {
                sb.AppendLine(string.Format("  {0} | {1} | {2} | {3} | {4} | {5} | {6}",
                    s.UserName ?? "",
                    s.MachineName ?? "",
                    s.DeliveryGroupName ?? "",
                    s.SessionState ?? "",
                    s.ClientName ?? "",
                    s.StartTime ?? "",
                    s.SessionStateChangeTime ?? ""));
            }
        }

        private static void AppendDesktopGroupTable(StringBuilder sb, IList<BrokerDesktopGroup> groups)
        {
            sb.AppendLine();
            sb.AppendLine("  " + Loc.F("Log_DesktopGroupTable", groups.Count) + ":");
            sb.AppendLine("  " + string.Join(" | ", new[]
            {
                Loc.T("Dg_ColName"), Loc.T("Dg_ColEnabled"), Loc.T("Col_Maint"), Loc.T("Dg_ColKind"),
                Loc.T("Dg_ColDelivery"), Loc.T("Dg_ColTotal"), Loc.T("Dg_ColInUse"),
                Loc.T("Dg_ColUnregistered"), Loc.T("Col_SessionCount")
            }));
            foreach (var g in groups)
            {
                sb.AppendLine(string.Format("  {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8}",
                    g.Name ?? "",
                    g.Enabled ? "Yes" : "No",
                    g.InMaintenanceMode ? "ON" : "-",
                    g.DesktopKind ?? "",
                    g.DeliveryType ?? "",
                    g.TotalDesktops,
                    g.DesktopsInUse,
                    g.DesktopsUnregistered,
                    g.Sessions));
            }
        }

        private static void AppendMachineTable(StringBuilder sb, IList<BrokerMachine> machines)
        {
            sb.AppendLine();
            sb.AppendLine("  " + Loc.F("Log_MachineTable", machines.Count) + ":");
            sb.AppendLine("  " + string.Join(" | ", new[]
            {
                Loc.T("Col_Maint"), Loc.T("Col_MachineName"), Loc.T("Col_DeliveryGroup"), Loc.T("Col_Type"),
                Loc.T("Col_Registration"), Loc.T("Col_Power"), Loc.T("Col_SessionCount")
            }));
            foreach (var m in machines)
            {
                sb.AppendLine(string.Format("  {0,-4} | {1} | {2} | {3} | {4} | {5} | {6}",
                    m.InMaintenanceMode ? "ON" : "-",
                    m.MachineName ?? "",
                    m.DeliveryGroupName ?? "",
                    SessionSupportConverter.ToLabel(m.SessionSupport),
                    m.RegistrationState ?? "",
                    m.PowerState ?? "",
                    m.SessionCount));
            }
        }

        /// <summary>実行環境のヘッダ。ログの先頭に置く。</summary>
        public static string FormatHeader()
        {
            var sb = new StringBuilder();
            sb.AppendLine("==========================================================");
            sb.AppendLine(" " + Loc.T("Log_HeaderTitle"));
            sb.AppendLine("==========================================================");
            sb.AppendLine(Field("Log_RunAt") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(Field("Log_RunOn") + Environment.MachineName);
            sb.AppendLine(Field("Log_RunAs") + Environment.UserDomainName + "\\" + Environment.UserName);
            sb.AppendLine(Field("Log_Os") + Environment.OSVersion.VersionString);
            AppendProcessIdentity(sb);
            return sb.ToString();
        }

        /// <summary>
        /// 1サイト分の結果を整形する。
        /// </summary>
        /// <param name="includeDiagnostics">
        /// 診断情報（スタックトレース等）を含めるか。
        /// 画面表示では省き、ログファイルには含める。
        /// </param>
        public static string FormatSiteResult(SiteConnectionResult result, bool includeDiagnostics)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var sb = new StringBuilder();
            sb.AppendLine("[" + result.SiteDisplayName + "]");

            foreach (var attempt in result.Attempts)
            {
                var ddc = string.IsNullOrWhiteSpace(attempt.DdcAddress) ? Loc.T("Log_NoDdc") : attempt.DdcAddress;
                var seconds = attempt.Elapsed.TotalSeconds.ToString("0.0");

                if (attempt.Success)
                {
                    sb.AppendLine(string.Format("  → {0} ... OK (Site: {1}, Ver: {2}, {3}s)",
                        ddc,
                        attempt.SiteName ?? Loc.T("Common_Unknown"),
                        attempt.Version ?? Loc.T("Common_Unknown"),
                        seconds));
                    AppendIdentityLines(sb, attempt);

                    if (attempt.FunctionalLevel != null)
                        sb.AppendLine("    " + Loc.T("Log_FunctionalLevel") + ": " + attempt.FunctionalLevel);
                    if (attempt.SdkLoadMethod != null)
                        sb.AppendLine("    SDK : " + attempt.SdkLoadMethod);

                    AppendLicenseLines(sb, attempt.License);
                }
                else
                {
                    sb.AppendLine(string.Format("  → {0} ... FAILED ({1}s)", ddc, seconds));
                    sb.AppendLine("    " + Loc.T("Log_Reason") + ": " + (attempt.ErrorMessage ?? Loc.T("Common_Unknown")));
                    // 失敗時こそ「どの資格情報で試したか」が要る（認証エラーの切り分けに直結）。
                    AppendIdentityLines(sb, attempt);
                }

                if (includeDiagnostics)
                {
                    foreach (var line in attempt.Diagnostics)
                        sb.AppendLine("    | " + line);
                }
            }

            if (result.SdkUnavailable)
            {
                sb.AppendLine("  ** " + Loc.T("Log_SdkAborted"));
                sb.AppendLine("     " + Loc.T("Log_SdkNotReachability"));
                sb.AppendLine("     " + Loc.T("Log_SdkHint"));
            }
            else if (!result.Success)
            {
                sb.AppendLine("  ** " + Loc.T("Log_NoDdcReachable"));
            }
            else if (result.Attempts.Count > 1)
            {
                sb.AppendLine("  ** " + Loc.T("Log_FailedOver"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// ライセンス構成とライセンスサーバーとの接続状況を出す。
        ///
        /// 接続状況は DDC ごとに並べる。「DDC-02 だけライセンスサーバーに繋がっていない」が
        /// 実際に起きるため、サイトで1行にまとめると見落とす。
        /// 猶予期間中は目立つ印を付ける。猶予が切れると新規セッションが拒否されるので、
        /// 接続テストが成功していても先に対処すべき状態だから。
        /// </summary>
        private static void AppendLicenseLines(StringBuilder sb, LicenseInfo license)
        {
            if (license == null) return;

            sb.AppendLine("    " + Loc.T("Lic_Header"));

            var server = license.LicenseServerName ?? Loc.T("Common_Unknown");
            if (!string.IsNullOrEmpty(license.LicenseServerPort)) server += ":" + license.LicenseServerPort;
            sb.AppendLine("      " + Loc.T("Lic_Server") + ": " + server);

            sb.AppendLine("      " + Loc.T("Lic_Product") + ": "
                + Join(" / ", license.ProductCode, license.ProductEdition, license.LicensingModel));

            if (license.GracePeriodActive == true)
            {
                sb.AppendLine("      ** " + Loc.F("Lic_GraceActive",
                    license.GraceHoursLeft.HasValue ? license.GraceHoursLeft.Value.ToString() : "?"));
            }

            foreach (var c in license.Controllers)
            {
                var mark = string.IsNullOrEmpty(c.LicensingServerState)
                           || c.LicensingServerState.Equals("OK", StringComparison.OrdinalIgnoreCase)
                    ? "  " : "**";
                sb.AppendLine(string.Format("      {0} {1}: {2}{3}",
                    mark,
                    c.DnsName ?? Loc.T("Common_Unknown"),
                    c.LicensingServerState ?? Loc.T("Common_Unknown"),
                    string.IsNullOrEmpty(c.LicensingGraceState)
                        || c.LicensingGraceState.Equals("NotActive", StringComparison.OrdinalIgnoreCase)
                        ? string.Empty
                        : " (" + c.LicensingGraceState + ")"));
            }
        }

        private static string Join(string separator, params string[] parts)
        {
            var list = new List<string>();
            foreach (var p in parts)
                if (!string.IsNullOrWhiteSpace(p)) list.Add(p);
            return list.Count == 0 ? Loc.T("Common_Unknown") : string.Join(separator, list.ToArray());
        }

        /// <summary>
        /// プロセスIDと起動時刻をヘッダに出す。
        ///
        /// 【なぜ必要か】
        /// 接続再利用の問題は「同一プロセス内かどうか」で挙動が変わるが、
        /// 以前のログにはPIDが無く、複数のログがどのプロセスから出たのかを
        /// タイムスタンプからの推定に頼るしかなかった（2026-08-11の分析で露呈）。
        /// PID＋起動時刻の組でプロセスを一意に特定できるようにする。
        /// </summary>
        private static void AppendProcessIdentity(StringBuilder sb)
        {
            var bitness = Environment.Is64BitProcess ? "64bit" : "32bit";

            try
            {
                using (var proc = System.Diagnostics.Process.GetCurrentProcess())
                {
                    sb.AppendLine(Field("Log_Process") + Loc.F("Log_ProcessInfo", proc.Id, bitness, proc.StartTime));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine(Field("Log_Process") + bitness + " (" + ex.Message + ")");
            }
        }

        /// <summary>
        /// 認証まわりの行を出す。
        ///
        /// フェーズ3以降、別資格情報は専用ワーカープロセス（netonly）で実行するため、
        /// 統合認証か別資格情報かは <see cref="ConnectionResult.RequestedIdentity"/> の
        /// 有無で判定できる（GUI側が指定アカウントを刻む）。
        /// フェーズ2の偽装／接続再利用に関する注記はこの方式では不要になった。
        /// </summary>
        private static void AppendIdentityLines(StringBuilder sb, ConnectionResult attempt)
        {
            if (attempt.RequestedIdentity == null)
            {
                // 統合Windows認証。GUIと同じユーザーのワーカーでDDCに認証される。
                sb.AppendLine("    " + Loc.T("Log_Auth") + ": "
                    + Loc.F("Log_AuthIntegratedAs", attempt.AuthenticatedAs ?? Loc.T("Common_Unknown")));
                return;
            }

            // 別資格情報。指定アカウントのネットワークIDを持つ専用プロセスで実行された。
            sb.AppendLine("    " + Loc.T("Log_Auth") + ": " + Loc.F("Log_AuthCredential", attempt.RequestedIdentity));
            sb.AppendLine("          " + Loc.T("Log_AuthCredentialNote"));
        }

        /// <summary>複数サイトの集計。</summary>
        public static string FormatSummary(IList<SiteConnectionResult> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));

            var succeeded = 0;
            foreach (var r in results)
            {
                if (r.Success) succeeded++;
            }

            var sb = new StringBuilder();
            sb.AppendLine("----------------------------------------------------------");
            sb.AppendLine(" " + Loc.F("Log_Summary", results.Count, succeeded, results.Count - succeeded));
            sb.AppendLine("----------------------------------------------------------");
            return sb.ToString();
        }
    }
}
