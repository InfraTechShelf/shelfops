using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CitrixAdminTool.Core.Configuration.Json;
using CitrixAdminTool.Core.Models;

namespace CitrixAdminTool.Core.Interop
{
    /// <summary>
    /// GUIプロセスと接続ワーカープロセスの間でやり取りする内容の
    /// JSONシリアライズ／デシリアライズ。
    ///
    /// 依存ライブラリを持たない方針は本ツール共通なので、
    /// 既存の <see cref="JsonWriter"/> / <see cref="JsonParser"/> を使う。
    ///
    /// 入力（GUI → Worker）: テストするサイト1件。資格情報は含めない。
    /// 出力（Worker → GUI）: そのサイトの接続結果（全試行の記録つき）。
    /// </summary>
    public static class WorkerProtocol
    {
        // ------------------------------------------------------------------
        // 文字列シリアライズ（ファイル授受・パイプ授受の両方が使う共通の核）
        // ------------------------------------------------------------------

        public static string SerializeResult(SiteConnectionResult result)
        {
            return JsonWriter.Write(ResultToDict(result));
        }

        public static SiteConnectionResult DeserializeResult(string json)
        {
            var root = JsonParser.Parse(json) as IDictionary<string, object>;
            if (root == null)
                throw new InvalidDataException("ワーカー結果の形式が不正です。");
            return ResultFromDict(root);
        }

        // ------------------------------------------------------------------
        // dict 変換（SerializeXxx / DeserializeXxx とパイプ封筒が共用）
        // ------------------------------------------------------------------

        public static Dictionary<string, object> SiteToDict(SiteConnection site)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));

            return new Dictionary<string, object>
            {
                { "id", site.Id },
                { "displayName", site.DisplayName },
                { "primaryDdc", site.PrimaryDdc },
                { "alternateDdcs", new List<object>((site.AlternateDdcs ?? new List<string>()).Cast<object>()) },
                // ワーカー内は常に統合認証（プロセス自体が正しいネットワークIDを持つ）。
                // 参考情報として元のモードも入れておくが、ワーカーの動作は変えない。
                { "authMode", site.AuthMode.ToString() }
            };
        }

        public static SiteConnection SiteFromDict(IDictionary<string, object> root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            return new SiteConnection
            {
                Id = GetString(root, "id"),
                DisplayName = GetString(root, "displayName"),
                PrimaryDdc = GetString(root, "primaryDdc"),
                AlternateDdcs = GetStringList(root, "alternateDdcs"),
                // ワーカーは統合認証で動くため、PromptForCredential を復元しても使わない。
                AuthMode = AuthMode.IntegratedWindows
            };
        }

        public static Dictionary<string, object> ResultToDict(SiteConnectionResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            return new Dictionary<string, object>
            {
                { "siteId", result.SiteId },
                { "siteDisplayName", result.SiteDisplayName },
                { "success", result.Success },
                { "sdkUnavailable", result.SdkUnavailable },
                { "attempts", new List<object>(result.Attempts.Select(ToDict)) }
            };
        }

        public static SiteConnectionResult ResultFromDict(IDictionary<string, object> root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var result = new SiteConnectionResult
            {
                SiteId = GetString(root, "siteId"),
                SiteDisplayName = GetString(root, "siteDisplayName"),
                Success = GetBool(root, "success"),
                SdkUnavailable = GetBool(root, "sdkUnavailable")
            };

            object attemptsValue;
            if (root.TryGetValue("attempts", out attemptsValue) && attemptsValue is List<object>)
            {
                foreach (var item in (List<object>)attemptsValue)
                {
                    var dict = item as IDictionary<string, object>;
                    if (dict != null) result.Attempts.Add(FromDict(dict));
                }
            }

            // 成功した試行を復元する（Attempts のうち Success のもの）。
            result.SuccessfulAttempt = result.Attempts.FirstOrDefault(a => a.Success);

            return result;
        }

        // ------------------------------------------------------------------
        // 操作リクエスト（op / site / params）— 接続テストも管理操作もこの形で送る
        // ------------------------------------------------------------------

        public const string OpTest = "test";
        public const string OpListMachines = "listMachines";
        public const string OpSetMaintenance = "setMaintenance";
        public const string OpListSessions = "listSessions";
        public const string OpLogoffSession = "logoffSession";
        public const string OpDisconnectSession = "disconnectSession";

        // 一括操作。1件ずつ単発opを投げるとワーカー往復とSDKロードが件数分発生し、
        // 現実的な時間で終わらないため、対象リストをまとめて1リクエストで送る。
        public const string OpSetMaintenanceBulk = "setMaintenanceBulk";
        public const string OpLogoffSessionsBulk = "logoffSessionsBulk";
        public const string OpDisconnectSessionsBulk = "disconnectSessionsBulk";

        // マシンの電源操作（New-BrokerHostingPowerAction）。対象と操作種別をまとめて送る。
        public const string OpPowerActionBulk = "powerActionBulk";

        public static Dictionary<string, object> BuildRequest(string op, SiteConnection site, Dictionary<string, object> parameters)
        {
            return new Dictionary<string, object>
            {
                { "op", op },
                { "site", SiteToDict(site) },
                { "params", parameters ?? new Dictionary<string, object>() }
            };
        }

        public static string GetRequestOp(IDictionary<string, object> request)
        {
            return GetString(request, "op");
        }

        public static SiteConnection GetRequestSite(IDictionary<string, object> request)
        {
            object siteObj;
            if (!request.TryGetValue("site", out siteObj)) return null;
            var dict = siteObj as IDictionary<string, object>;
            return dict == null ? null : SiteFromDict(dict);
        }

        public static IDictionary<string, object> GetRequestParams(IDictionary<string, object> request)
        {
            object p;
            if (!request.TryGetValue("params", out p)) return new Dictionary<string, object>();
            return p as IDictionary<string, object> ?? new Dictionary<string, object>();
        }

        // ------------------------------------------------------------------
        // 操作結果: BrokerOperationResult
        // ------------------------------------------------------------------

        public static string SerializeOperationResult(BrokerOperationResult result)
        {
            return JsonWriter.Write(OperationResultToDict(result));
        }

        public static BrokerOperationResult DeserializeOperationResult(string json)
        {
            var root = JsonParser.Parse(json) as IDictionary<string, object>;
            if (root == null)
                throw new InvalidDataException("操作結果の形式が不正です。");
            return OperationResultFromDict(root);
        }

        private static Dictionary<string, object> OperationResultToDict(BrokerOperationResult r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));

            return new Dictionary<string, object>
            {
                { "operation", r.Operation },
                { "success", r.Success },
                { "workingDdc", r.WorkingDdc },
                { "message", r.Message },
                { "errorMessage", r.ErrorMessage },
                { "sdkUnavailable", r.SdkUnavailable },
                { "requestedIdentity", r.RequestedIdentity },
                { "targets", new List<object>((r.Targets ?? new List<BrokerTargetResult>()).Select(TargetToDict)) },
                { "machines", new List<object>((r.Machines ?? new List<BrokerMachine>()).Select(MachineToDict)) },
                { "sessions", new List<object>((r.Sessions ?? new List<BrokerSession>()).Select(SessionToDict)) },
                { "diagnostics", new List<object>((r.Diagnostics ?? new List<string>()).Cast<object>()) }
            };
        }

        private static BrokerOperationResult OperationResultFromDict(IDictionary<string, object> d)
        {
            var result = new BrokerOperationResult
            {
                Operation = GetString(d, "operation"),
                Success = GetBool(d, "success"),
                WorkingDdc = GetString(d, "workingDdc"),
                Message = GetString(d, "message"),
                ErrorMessage = GetString(d, "errorMessage"),
                SdkUnavailable = GetBool(d, "sdkUnavailable"),
                RequestedIdentity = GetString(d, "requestedIdentity"),
                Diagnostics = GetStringList(d, "diagnostics")
            };

            object targetsValue;
            if (d.TryGetValue("targets", out targetsValue) && targetsValue is List<object>)
            {
                foreach (var item in (List<object>)targetsValue)
                {
                    var td = item as IDictionary<string, object>;
                    if (td != null) result.Targets.Add(TargetFromDict(td));
                }
            }

            object machinesValue;
            if (d.TryGetValue("machines", out machinesValue) && machinesValue is List<object>)
            {
                foreach (var item in (List<object>)machinesValue)
                {
                    var md = item as IDictionary<string, object>;
                    if (md != null) result.Machines.Add(MachineFromDict(md));
                }
            }

            object sessionsValue;
            if (d.TryGetValue("sessions", out sessionsValue) && sessionsValue is List<object>)
            {
                foreach (var item in (List<object>)sessionsValue)
                {
                    var sd = item as IDictionary<string, object>;
                    if (sd != null) result.Sessions.Add(SessionFromDict(sd));
                }
            }

            return result;
        }

        private static Dictionary<string, object> TargetToDict(BrokerTargetResult t)
        {
            return new Dictionary<string, object>
            {
                { "targetName", t.TargetName },
                { "success", t.Success },
                { "errorMessage", t.ErrorMessage }
            };
        }

        private static BrokerTargetResult TargetFromDict(IDictionary<string, object> d)
        {
            return new BrokerTargetResult
            {
                TargetName = GetString(d, "targetName"),
                Success = GetBool(d, "success"),
                ErrorMessage = GetString(d, "errorMessage")
            };
        }

        private static Dictionary<string, object> SessionToDict(BrokerSession s)
        {
            return new Dictionary<string, object>
            {
                { "uid", s.Uid },
                { "userName", s.UserName },
                { "machineName", s.MachineName },
                { "deliveryGroupName", s.DeliveryGroupName },
                { "sessionState", s.SessionState },
                { "clientName", s.ClientName },
                { "startTime", s.StartTime },
                { "sessionStateChangeTime", s.SessionStateChangeTime }
            };
        }

        private static BrokerSession SessionFromDict(IDictionary<string, object> d)
        {
            return new BrokerSession
            {
                Uid = (long)GetDouble(d, "uid"),
                UserName = GetString(d, "userName"),
                MachineName = GetString(d, "machineName"),
                DeliveryGroupName = GetString(d, "deliveryGroupName"),
                SessionState = GetString(d, "sessionState"),
                ClientName = GetString(d, "clientName"),
                StartTime = GetString(d, "startTime"),
                SessionStateChangeTime = GetString(d, "sessionStateChangeTime")
            };
        }

        private static Dictionary<string, object> MachineToDict(BrokerMachine m)
        {
            return new Dictionary<string, object>
            {
                { "machineName", m.MachineName },
                { "dnsName", m.DnsName },
                { "catalogName", m.CatalogName },
                { "deliveryGroupName", m.DeliveryGroupName },
                { "registrationState", m.RegistrationState },
                { "powerState", m.PowerState },
                { "summaryState", m.SummaryState },
                { "sessionSupport", m.SessionSupport },
                { "associatedUserNames", m.AssociatedUserNames },
                { "inMaintenanceMode", m.InMaintenanceMode },
                { "sessionCount", m.SessionCount }
            };
        }

        private static BrokerMachine MachineFromDict(IDictionary<string, object> d)
        {
            return new BrokerMachine
            {
                MachineName = GetString(d, "machineName"),
                DnsName = GetString(d, "dnsName"),
                CatalogName = GetString(d, "catalogName"),
                DeliveryGroupName = GetString(d, "deliveryGroupName"),
                RegistrationState = GetString(d, "registrationState"),
                PowerState = GetString(d, "powerState"),
                SummaryState = GetString(d, "summaryState"),
                SessionSupport = GetString(d, "sessionSupport"),
                AssociatedUserNames = GetString(d, "associatedUserNames"),
                InMaintenanceMode = GetBool(d, "inMaintenanceMode"),
                SessionCount = (int)GetDouble(d, "sessionCount")
            };
        }

        private static Dictionary<string, object> ToDict(ConnectionResult r)
        {
            return new Dictionary<string, object>
            {
                { "success", r.Success },
                { "ddcAddress", r.DdcAddress },
                { "siteName", r.SiteName },
                { "version", r.Version },
                { "functionalLevel", r.FunctionalLevel },
                { "authenticatedAs", r.AuthenticatedAs },
                { "requestedIdentity", r.RequestedIdentity },
                { "impersonationActive", r.ImpersonationActive.HasValue ? (object)r.ImpersonationActive.Value : null },
                { "errorMessage", r.ErrorMessage },
                { "sdkLoadMethod", r.SdkLoadMethod },
                { "isSdkUnavailable", r.IsSdkUnavailable },
                { "elapsedMs", r.Elapsed.TotalMilliseconds },
                { "diagnostics", new List<object>((r.Diagnostics ?? new List<string>()).Cast<object>()) }
            };
        }

        private static ConnectionResult FromDict(IDictionary<string, object> d)
        {
            return new ConnectionResult
            {
                Success = GetBool(d, "success"),
                DdcAddress = GetString(d, "ddcAddress"),
                SiteName = GetString(d, "siteName"),
                Version = GetString(d, "version"),
                FunctionalLevel = GetString(d, "functionalLevel"),
                AuthenticatedAs = GetString(d, "authenticatedAs"),
                RequestedIdentity = GetString(d, "requestedIdentity"),
                ImpersonationActive = GetNullableBool(d, "impersonationActive"),
                ErrorMessage = GetString(d, "errorMessage"),
                SdkLoadMethod = GetString(d, "sdkLoadMethod"),
                IsSdkUnavailable = GetBool(d, "isSdkUnavailable"),
                Elapsed = TimeSpan.FromMilliseconds(GetDouble(d, "elapsedMs")),
                Diagnostics = GetStringList(d, "diagnostics")
            };
        }

        // ------------------------------------------------------------------
        // 値の取り出し（JsonParser は数値を double、真偽値を bool で返す）
        // ------------------------------------------------------------------

        private static string GetString(IDictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return null;
            return v as string ?? v.ToString();
        }

        private static bool GetBool(IDictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return false;
            return v is bool && (bool)v;
        }

        private static bool? GetNullableBool(IDictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return null;
            return v is bool ? (bool?)(bool)v : null;
        }

        private static double GetDouble(IDictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return 0;
            if (v is double) return (double)v;
            double parsed;
            return double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed : 0;
        }

        private static List<string> GetStringList(IDictionary<string, object> d, string key)
        {
            var result = new List<string>();

            object v;
            if (!d.TryGetValue(key, out v) || !(v is List<object>)) return result;

            foreach (var item in (List<object>)v)
            {
                if (item == null) continue;
                result.Add(item as string ?? item.ToString());
            }

            return result;
        }
    }
}
