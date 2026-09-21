using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using CitrixAdminTool.Core.Configuration.Json;
using CitrixAdminTool.Core.Interop;
using CitrixAdminTool.Core.Models;

namespace CitrixAdminTool.Core.Connection
{
    /// <summary>
    /// 接続テストを**別プロセス（ワーカー）**に委譲して実行する窓口。
    ///
    /// 【なぜ別プロセスか】
    /// 別資格情報モードの不具合（同一プロセス内で認証済み接続が再利用され、
    /// 指定した資格情報が無視される）を根本から断つため。
    /// System.Net/WCFの接続プールはプロセス単位なので、資格情報ごとにプロセスを
    /// 分ければ接続の使い回しは起きない。詳細は docs/SPEC_phase3.md。
    ///
    /// ワーカー自身は常に統合認証（パススルー）で動く。別資格情報のときは
    /// ワーカープロセスを CreateProcessWithLogonW(netonly) で起動し、
    /// プロセス全体を指定資格情報のネットワークIDにするため、
    /// ワーカー内で偽装する必要が無い。
    ///
    /// 【常駐化（フェーズ3+）】
    /// 統合認証ワーカーは <see cref="PersistentWorkerClient"/> で常駐させ、初回
    /// ウォームアップをプロセスごと1回に抑える。別資格情報ワーカーは安全性維持のため
    /// 常駐させず、従来どおり使い捨て（1接続1プロセス／都度資格情報／使用後に消える）。
    ///
    /// このクラスは <see cref="IDisposable"/>。GUI終了時に Dispose して常駐ワーカーを
    /// 停止すること。
    /// </summary>
    public class WorkerConnectionService : IDisposable
    {
        public const string WorkerExeName = "ShelfOps.Worker.exe";

        private readonly string _workerExePath;
        private readonly int _timeoutMs;
        private readonly bool _persistIntegrated;
        private PersistentWorkerClient _integratedWorker;
        private readonly object _integratedLock = new object();

        /// <param name="workerExePath">
        /// ワーカー実行ファイルのパス。null なら実行フォルダ内の既定名を使う。
        /// </param>
        /// <param name="timeoutMs">ワーカー1回あたりのタイムアウト（既定90秒）。</param>
        /// <param name="persistIntegrated">
        /// 統合認証ワーカーを常駐させるか（既定 true）。テスト等で使い捨てに戻せるよう引数化。
        /// </param>
        public WorkerConnectionService(string workerExePath = null, int timeoutMs = 90000, bool persistIntegrated = true)
        {
            _workerExePath = workerExePath ?? DefaultWorkerPath();
            _timeoutMs = timeoutMs;
            _persistIntegrated = persistIntegrated;
        }

        public static string DefaultWorkerPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, WorkerExeName);
        }

        public bool WorkerExists()
        {
            return File.Exists(_workerExePath);
        }

        // ==================================================================
        // 接続テスト
        // ==================================================================

        /// <summary>統合Windows認証でサイトの接続テストを行う（常駐ワーカーを使い回す）。</summary>
        public SiteConnectionResult TestSiteIntegrated(SiteConnection site)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));

            try
            {
                var json = SendIntegrated(WorkerProtocol.BuildRequest(WorkerProtocol.OpTest, site, null));
                return WorkerProtocol.DeserializeResult(json);
            }
            catch (Exception ex)
            {
                return FailResult(site, ex.Message);
            }
        }

        /// <summary>
        /// 別資格情報でサイトの接続テストを行う。ワーカーを netonly で起動する（使い捨て）。
        /// password は呼び出し側が所有し、使用後に破棄すること。
        /// </summary>
        public SiteConnectionResult TestSiteWithCredential(
            SiteConnection site, string domain, string userName, SecureString password)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));
            var requested = FormatUser(domain, userName);

            SiteConnectionResult result;
            try
            {
                var json = SendWithCredential(
                    WorkerProtocol.BuildRequest(WorkerProtocol.OpTest, site, null), domain, userName, password);
                result = WorkerProtocol.DeserializeResult(json);
            }
            catch (Exception ex)
            {
                result = FailResult(site, ex.Message);
            }

            // ワーカーは常に統合認証で動くため、結果には「どの資格情報を指定して起動したか」の
            // 情報が入っていない。それを知っているのはここ（GUI側）だけなので各試行に刻む。
            foreach (var attempt in result.Attempts)
            {
                attempt.RequestedIdentity = requested;
                attempt.Diagnostics.Insert(0,
                    "このワーカーは別資格情報 " + requested + " のネットワークIDで起動されました"
                    + "（CreateProcessWithLogonW / netonly / 専用プロセス）。");
            }
            return result;
        }

        // ==================================================================
        // マシン一覧（読み取り）
        // ==================================================================

        public BrokerOperationResult ListMachinesIntegrated(SiteConnection site, string deliveryGroup, int maxCount)
        {
            return RunOperationIntegrated(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpListMachines, site,
                    ListMachinesParams(deliveryGroup, maxCount)));
        }

        public BrokerOperationResult ListMachinesWithCredential(
            SiteConnection site, string deliveryGroup, int maxCount,
            string domain, string userName, SecureString password)
        {
            return RunOperationWithCredential(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpListMachines, site,
                    ListMachinesParams(deliveryGroup, maxCount)),
                domain, userName, password);
        }

        private static Dictionary<string, object> ListMachinesParams(string deliveryGroup, int maxCount)
        {
            return new Dictionary<string, object>
            {
                { "deliveryGroup", deliveryGroup },
                { "maxCount", maxCount }
            };
        }

        // ==================================================================
        // メンテナンスモード切替（書き込み）
        // ==================================================================

        public BrokerOperationResult SetMaintenanceIntegrated(SiteConnection site, string machineName, bool enabled)
        {
            return RunOperationIntegrated(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpSetMaintenance, site,
                    MaintenanceParams(machineName, enabled)));
        }

        public BrokerOperationResult SetMaintenanceWithCredential(
            SiteConnection site, string machineName, bool enabled,
            string domain, string userName, SecureString password)
        {
            return RunOperationWithCredential(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpSetMaintenance, site,
                    MaintenanceParams(machineName, enabled)),
                domain, userName, password);
        }

        private static Dictionary<string, object> MaintenanceParams(string machineName, bool enabled)
        {
            return new Dictionary<string, object>
            {
                { "machineName", machineName },
                { "inMaintenanceMode", enabled }
            };
        }

        // ------------------------------------------------------------------
        // 一括操作
        //
        // 1台ずつ単発opを投げると、ワーカー往復とCitrix SDKのロードが台数分発生し、
        // 数十台でも現実的な時間で終わらない。対象リストをまとめて1リクエストで送り、
        // ワーカー側は1つのRunspace内でループする。
        // ------------------------------------------------------------------

        public BrokerOperationResult SetMaintenanceBulkIntegrated(
            SiteConnection site, IList<string> machineNames, bool enabled)
        {
            return RunOperationIntegrated(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpSetMaintenanceBulk, site,
                    MaintenanceBulkParams(machineNames, enabled)),
                BulkTimeout(machineNames == null ? 0 : machineNames.Count));
        }

        public BrokerOperationResult SetMaintenanceBulkWithCredential(
            SiteConnection site, IList<string> machineNames, bool enabled,
            string domain, string userName, SecureString password)
        {
            return RunOperationWithCredential(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpSetMaintenanceBulk, site,
                    MaintenanceBulkParams(machineNames, enabled)),
                domain, userName, password,
                BulkTimeout(machineNames == null ? 0 : machineNames.Count));
        }

        private static Dictionary<string, object> MaintenanceBulkParams(IList<string> machineNames, bool enabled)
        {
            var names = new List<object>();
            if (machineNames != null)
            {
                foreach (var n in machineNames)
                    if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
            }

            return new Dictionary<string, object>
            {
                { "machineNames", names },
                { "inMaintenanceMode", enabled }
            };
        }

        /// <summary>
        /// 対象件数に応じたタイムアウト。SDKロード分の固定費に、1件あたりの処理時間を積む。
        /// 既定（90秒）を下回らないようにし、暴走を避けるため上限も設ける。
        /// </summary>
        private int BulkTimeout(int targetCount)
        {
            if (targetCount <= 1) return _timeoutMs;

            var estimated = 60000 + targetCount * 3000;
            if (estimated > 1800000) estimated = 1800000; // 上限30分
            return estimated > _timeoutMs ? estimated : _timeoutMs;
        }

        // ==================================================================
        // デリバリーグループ一覧（読み取り）
        // ==================================================================

        public BrokerOperationResult ListDesktopGroupsIntegrated(SiteConnection site)
        {
            return RunOperationIntegrated(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpListDesktopGroups, site, null));
        }

        public BrokerOperationResult ListDesktopGroupsWithCredential(
            SiteConnection site, string domain, string userName, SecureString password)
        {
            return RunOperationWithCredential(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpListDesktopGroups, site, null),
                domain, userName, password);
        }

        // ==================================================================
        // セッション操作（一覧＝読み取り / ログオフ・切断＝書き込み・破壊的）
        // ==================================================================

        public BrokerOperationResult ListSessionsIntegrated(SiteConnection site, string deliveryGroup, string userName, int maxCount)
        {
            return RunOperationIntegrated(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpListSessions, site,
                    ListSessionsParams(deliveryGroup, userName, maxCount)));
        }

        public BrokerOperationResult ListSessionsWithCredential(
            SiteConnection site, string deliveryGroup, string userName, int maxCount,
            string domain, string credUserName, SecureString password)
        {
            return RunOperationWithCredential(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpListSessions, site,
                    ListSessionsParams(deliveryGroup, userName, maxCount)),
                domain, credUserName, password);
        }

        private static Dictionary<string, object> ListSessionsParams(string deliveryGroup, string userName, int maxCount)
        {
            return new Dictionary<string, object>
            {
                { "deliveryGroup", deliveryGroup },
                { "userName", userName },
                { "maxCount", maxCount }
            };
        }

        public BrokerOperationResult LogoffSessionIntegrated(SiteConnection site, long uid)
        {
            return RunOperationIntegrated(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpLogoffSession, site, SessionUidParams(uid)));
        }

        public BrokerOperationResult LogoffSessionWithCredential(
            SiteConnection site, long uid, string domain, string userName, SecureString password)
        {
            return RunOperationWithCredential(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpLogoffSession, site, SessionUidParams(uid)),
                domain, userName, password);
        }

        public BrokerOperationResult DisconnectSessionIntegrated(SiteConnection site, long uid)
        {
            return RunOperationIntegrated(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpDisconnectSession, site, SessionUidParams(uid)));
        }

        public BrokerOperationResult DisconnectSessionWithCredential(
            SiteConnection site, long uid, string domain, string userName, SecureString password)
        {
            return RunOperationWithCredential(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpDisconnectSession, site, SessionUidParams(uid)),
                domain, userName, password);
        }

        private static Dictionary<string, object> SessionUidParams(long uid)
        {
            return new Dictionary<string, object> { { "uid", uid } };
        }

        public BrokerOperationResult LogoffSessionsBulkIntegrated(SiteConnection site, IList<long> uids)
        {
            return RunOperationIntegrated(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpLogoffSessionsBulk, site, SessionUidsParams(uids)),
                BulkTimeout(uids == null ? 0 : uids.Count));
        }

        public BrokerOperationResult LogoffSessionsBulkWithCredential(
            SiteConnection site, IList<long> uids, string domain, string userName, SecureString password)
        {
            return RunOperationWithCredential(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpLogoffSessionsBulk, site, SessionUidsParams(uids)),
                domain, userName, password,
                BulkTimeout(uids == null ? 0 : uids.Count));
        }

        public BrokerOperationResult DisconnectSessionsBulkIntegrated(SiteConnection site, IList<long> uids)
        {
            return RunOperationIntegrated(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpDisconnectSessionsBulk, site, SessionUidsParams(uids)),
                BulkTimeout(uids == null ? 0 : uids.Count));
        }

        public BrokerOperationResult DisconnectSessionsBulkWithCredential(
            SiteConnection site, IList<long> uids, string domain, string userName, SecureString password)
        {
            return RunOperationWithCredential(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpDisconnectSessionsBulk, site, SessionUidsParams(uids)),
                domain, userName, password,
                BulkTimeout(uids == null ? 0 : uids.Count));
        }

        // ------------------------------------------------------------------
        // 電源操作（破壊的）
        // ------------------------------------------------------------------

        public BrokerOperationResult PowerActionIntegrated(
            SiteConnection site, IList<string> machineNames, BrokerPowerAction action)
        {
            return RunOperationIntegrated(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpPowerActionBulk, site,
                    PowerActionParams(machineNames, action)),
                BulkTimeout(machineNames == null ? 0 : machineNames.Count));
        }

        public BrokerOperationResult PowerActionWithCredential(
            SiteConnection site, IList<string> machineNames, BrokerPowerAction action,
            string domain, string userName, SecureString password)
        {
            return RunOperationWithCredential(site,
                WorkerProtocol.BuildRequest(WorkerProtocol.OpPowerActionBulk, site,
                    PowerActionParams(machineNames, action)),
                domain, userName, password,
                BulkTimeout(machineNames == null ? 0 : machineNames.Count));
        }

        private static Dictionary<string, object> PowerActionParams(
            IList<string> machineNames, BrokerPowerAction action)
        {
            var names = new List<object>();
            if (machineNames != null)
            {
                foreach (var n in machineNames)
                    if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
            }

            return new Dictionary<string, object>
            {
                { "machineNames", names },
                { "powerAction", action.ToString() }
            };
        }

        private static Dictionary<string, object> SessionUidsParams(IList<long> uids)
        {
            var list = new List<object>();
            if (uids != null)
            {
                foreach (var uid in uids)
                    if (uid > 0) list.Add(uid);
            }

            return new Dictionary<string, object> { { "uids", list } };
        }

        // ==================================================================
        // 操作の共通実行（統合認証 / 別資格情報）
        // ==================================================================

        private BrokerOperationResult RunOperationIntegrated(
            SiteConnection site, Dictionary<string, object> request, int timeoutMs = 0)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));
            try
            {
                return WorkerProtocol.DeserializeOperationResult(SendIntegrated(request, timeoutMs));
            }
            catch (Exception ex)
            {
                return BrokerOperationResult.Fail(WorkerProtocol.GetRequestOp(request), ex.Message);
            }
        }

        private BrokerOperationResult RunOperationWithCredential(
            SiteConnection site, Dictionary<string, object> request,
            string domain, string userName, SecureString password, int timeoutMs = 0)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));
            var requested = FormatUser(domain, userName);

            BrokerOperationResult result;
            try
            {
                result = WorkerProtocol.DeserializeOperationResult(
                    SendWithCredential(request, domain, userName, password, timeoutMs));
            }
            catch (Exception ex)
            {
                result = BrokerOperationResult.Fail(WorkerProtocol.GetRequestOp(request), ex.Message);
            }

            result.RequestedIdentity = requested;
            result.Diagnostics.Insert(0,
                "このワーカーは別資格情報 " + requested + " のネットワークIDで起動されました"
                + "（CreateProcessWithLogonW / netonly / 専用プロセス）。");
            return result;
        }

        // ==================================================================
        // トランスポート（生JSONの送受信）
        // ==================================================================

        /// <summary>統合認証: 常駐ワーカー（既定）または使い捨てで送受信する。失敗時は例外。</summary>
        private string SendIntegrated(Dictionary<string, object> request, int timeoutMs = 0)
        {
            EnsureWorkerExists();

            var effective = timeoutMs > 0 ? timeoutMs : _timeoutMs;

            if (_persistIntegrated)
                return GetIntegratedWorker().SendRequest(request, effective);

            return SendOneShot(request, args =>
                ProcessLauncher.RunIntegrated(_workerExePath, args, effective));
        }

        /// <summary>別資格情報: netonly で使い捨てワーカーを起動して送受信する。失敗時は例外。</summary>
        private string SendWithCredential(
            Dictionary<string, object> request, string domain, string userName, SecureString password,
            int timeoutMs = 0)
        {
            EnsureWorkerExists();

            var effective = timeoutMs > 0 ? timeoutMs : _timeoutMs;

            return SendOneShot(request, args =>
                ProcessLauncher.RunWithCredential(_workerExePath, args, domain, userName, password, effective));
        }

        /// <summary>使い捨てワーカーに一時ファイルでリクエスト／レスポンスを渡す。失敗時は例外。</summary>
        private string SendOneShot(Dictionary<string, object> request, Func<string, ProcessRunResult> launch)
        {
            var dir = GetTempDir();
            var token = Guid.NewGuid().ToString("N");
            var inPath = Path.Combine(dir, token + ".in.json");
            var outPath = Path.Combine(dir, token + ".out.json");

            try
            {
                File.WriteAllText(inPath, JsonWriter.Write(request) + "\r\n", new UTF8Encoding(false));

                var args = string.Format("--in \"{0}\" --out \"{1}\"", inPath, outPath);
                var run = launch(args);

                if (run.TimedOut)
                    throw new TimeoutException("ワーカーが時間内に応答しませんでした（タイムアウト）。");

                if (!File.Exists(outPath))
                    throw new IOException(string.Format(
                        "ワーカーが結果を返しませんでした（終了コード {0}）。", run.ExitCode));

                return File.ReadAllText(outPath, Encoding.UTF8);
            }
            finally
            {
                TryDelete(inPath);
                TryDelete(outPath);
            }
        }

        private void EnsureWorkerExists()
        {
            if (!WorkerExists())
                throw new FileNotFoundException(
                    "接続ワーカー（" + WorkerExeName + "）が見つかりません: " + _workerExePath);
        }

        private PersistentWorkerClient GetIntegratedWorker()
        {
            lock (_integratedLock)
            {
                if (_integratedWorker == null)
                    _integratedWorker = new PersistentWorkerClient(_workerExePath, _timeoutMs);
                return _integratedWorker;
            }
        }

        private static string FormatUser(string domain, string userName)
        {
            return string.IsNullOrWhiteSpace(domain) ? userName : domain + "\\" + userName;
        }

        private static string GetTempDir()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ShelfOps", "tmp");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        private static SiteConnectionResult FailResult(SiteConnection site, string message)
        {
            var result = new SiteConnectionResult
            {
                SiteId = site.Id,
                SiteDisplayName = site.Label,
                Success = false
            };
            result.Attempts.Add(ConnectionResult.Fail(site.PrimaryDdc, message, TimeSpan.Zero));
            return result;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* 一時ファイルの削除失敗は致命的でない */ }
        }

        /// <summary>常駐ワーカーを停止する。GUI終了時に必ず呼ぶこと。</summary>
        public void Dispose()
        {
            PersistentWorkerClient worker;
            lock (_integratedLock)
            {
                worker = _integratedWorker;
                _integratedWorker = null;
            }

            if (worker != null) worker.Dispose();
        }
    }
}
