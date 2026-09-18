using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using CitrixAdminTool.Core.Configuration.Json;
using CitrixAdminTool.Core.Connection;
using CitrixAdminTool.Core.Interop;
using CitrixAdminTool.Core.Models;

namespace CitrixAdminTool.Worker
{
    /// <summary>
    /// 接続／操作ワーカー。DDCへの接続と管理操作を、GUIとは別プロセスで行う。
    ///
    /// このプロセスは、別資格情報モードのときは GUI 側が
    /// CreateProcessWithLogonW(netonly) で起動する。つまり**プロセス自体が
    /// 指定資格情報のネットワークIDを持っている**ため、ここでは偽装を一切行わず
    /// 統合認証（現在のプロセスのID）として接続すればよい。
    /// これにより、フェーズ2で問題になったプロセス内の接続再利用が起きない。
    ///
    /// 2つの動作モード（どちらも「操作リクエスト {op, site, params}」を処理する）:
    ///   --in/--out … リクエストファイルを読んで1回処理し、結果ファイルを書いて終了（使い捨て）。
    ///                別資格情報ワーカーはこちら（安全性維持のため常駐させない）。
    ///   --serve    … 名前付きパイプに接続し、複数リクエストを処理し続ける（常駐）。
    ///                統合認証ワーカーはこちら。初回ウォームアップをプロセスごと1回に抑える。
    ///
    /// 対応する操作 op:
    ///   "test"           … 接続テスト（Get-BrokerSite）。結果は SiteConnectionResult。
    ///   "listMachines"   … マシン一覧（Get-BrokerMachine）。結果は BrokerOperationResult。
    ///   "setMaintenance" … メンテナンスモード切替（Set-BrokerMachine）。結果は BrokerOperationResult。
    ///   "listSessions" / "logoffSession" / "disconnectSession" … セッションの一覧・ログオフ・切断。
    ///   "powerActionBulk" … マシンの電源操作（New-BrokerHostingPowerAction）。
    ///   "setMaintenanceBulk" / "logoffSessionsBulk" / "disconnectSessionsBulk"
    ///                    … 複数対象の一括操作。対象ごとの成否を Targets に積んで返す。
    ///                      SDKロードを1回で済ませるため、1リクエストで全対象を処理する。
    ///
    /// 使い捨てモードの終了コード:
    ///   0 = 成功 / 1 = 失敗（結果ファイルは書く）/ 2 = 引数不正 / 3 = 想定外のエラー
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string servePipe = null;
            string inPath = null;
            string outPath = null;

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--serve" && i + 1 < args.Length) servePipe = args[i + 1];
                else if (args[i] == "--in" && i + 1 < args.Length) inPath = args[i + 1];
                else if (args[i] == "--out" && i + 1 < args.Length) outPath = args[i + 1];
            }

            if (!string.IsNullOrWhiteSpace(servePipe))
                return Serve(servePipe);

            if (string.IsNullOrWhiteSpace(inPath) || string.IsNullOrWhiteSpace(outPath))
            {
                Console.Error.WriteLine("使い方: ShelfOps.Worker.exe (--in <入力> --out <結果> | --serve <パイプ名>)");
                return 2;
            }

            IDictionary<string, object> request;
            try
            {
                request = JsonParser.Parse(File.ReadAllText(inPath, Encoding.UTF8)) as IDictionary<string, object>;
                if (request == null) throw new InvalidDataException("リクエストがオブジェクトではありません。");
            }
            catch (Exception ex)
            {
                WriteErrorFile(outPath, "入力ファイルを読み込めませんでした: " + ex.Message);
                return 3;
            }

            try
            {
                bool success;
                var responseJson = Dispatch(request, out success);
                File.WriteAllText(outPath, responseJson + "\r\n", new UTF8Encoding(false));
                return success ? 0 : 1;
            }
            catch (Exception ex)
            {
                WriteErrorFile(outPath, "処理中に想定外のエラー: " + ex.GetType().FullName + ": " + ex.Message);
                return 3;
            }
        }

        /// <summary>
        /// 常駐モード。GUIが立てた名前付きパイプにクライアントとして接続し、
        /// リクエストを受けるたびに処理して結果を返す。パイプが閉じられるまで動き続ける。
        /// </summary>
        private static int Serve(string pipeName)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipe.Connect(15000);

                    while (true)
                    {
                        var requestJson = PipeMessaging.ReadMessage(pipe);
                        if (requestJson == null) break; // GUIがパイプを閉じた

                        var request = JsonParser.Parse(requestJson) as IDictionary<string, object>;
                        if (request == null) continue;

                        // 制御コマンド（終了）
                        object command;
                        request.TryGetValue("command", out command);
                        if ((command as string) == "quit") break;

                        bool success;
                        var responseJson = Dispatch(request, out success);
                        PipeMessaging.WriteMessage(pipe, responseJson);
                    }
                }
                return 0;
            }
            catch (Exception)
            {
                // パイプ切断・GUIの異常終了などは正常終了として扱う（孤児プロセスを残さない）。
                return 0;
            }
        }

        /// <summary>
        /// 操作リクエストを処理し、レスポンスJSONを返す。
        /// op によって結果の型（SiteConnectionResult / BrokerOperationResult）が変わる。
        /// </summary>
        private static string Dispatch(IDictionary<string, object> request, out bool success)
        {
            var op = WorkerProtocol.GetRequestOp(request);
            var site = WorkerProtocol.GetRequestSite(request);
            var parameters = WorkerProtocol.GetRequestParams(request);

            if (site == null)
            {
                success = false;
                return OperationError(op, "リクエストにサイト定義がありません。");
            }

            switch (op)
            {
                case WorkerProtocol.OpTest:
                    {
                        var result = new CitrixConnectionService().TestSiteConnection(site);
                        StampConnection(result);
                        success = result.Success;
                        return WorkerProtocol.SerializeResult(result);
                    }

                case WorkerProtocol.OpListMachines:
                    {
                        var deliveryGroup = GetString(parameters, "deliveryGroup");
                        // 0 は「無制限（全件取得）」。GUIは既定で 0 を送る。
                        var maxCount = GetInt(parameters, "maxCount", 0);
                        var result = new BrokerOperationService().ListMachines(site, deliveryGroup, maxCount);
                        StampOperation(result);
                        success = result.Success;
                        return WorkerProtocol.SerializeOperationResult(result);
                    }

                case WorkerProtocol.OpSetMaintenance:
                    {
                        var machineName = GetString(parameters, "machineName");
                        var enabled = GetBool(parameters, "inMaintenanceMode");
                        var result = new BrokerOperationService().SetMaintenanceMode(site, machineName, enabled);
                        StampOperation(result);
                        success = result.Success;
                        return WorkerProtocol.SerializeOperationResult(result);
                    }

                case WorkerProtocol.OpListSessions:
                    {
                        var deliveryGroup = GetString(parameters, "deliveryGroup");
                        var userName = GetString(parameters, "userName");
                        // 0 は「無制限（全件取得）」。GUIは既定で 0 を送る。
                        var maxCount = GetInt(parameters, "maxCount", 0);
                        var result = new BrokerOperationService().ListSessions(site, deliveryGroup, userName, maxCount);
                        StampOperation(result);
                        success = result.Success;
                        return WorkerProtocol.SerializeOperationResult(result);
                    }

                case WorkerProtocol.OpLogoffSession:
                    {
                        var result = new BrokerOperationService().LogoffSession(site, GetLong(parameters, "uid"));
                        StampOperation(result);
                        success = result.Success;
                        return WorkerProtocol.SerializeOperationResult(result);
                    }

                case WorkerProtocol.OpDisconnectSession:
                    {
                        var result = new BrokerOperationService().DisconnectSession(site, GetLong(parameters, "uid"));
                        StampOperation(result);
                        success = result.Success;
                        return WorkerProtocol.SerializeOperationResult(result);
                    }

                case WorkerProtocol.OpSetMaintenanceBulk:
                    {
                        var machineNames = GetStringList(parameters, "machineNames");
                        var enabled = GetBool(parameters, "inMaintenanceMode");
                        var result = new BrokerOperationService().SetMaintenanceModeBulk(site, machineNames, enabled);
                        StampOperation(result);
                        success = result.Success;
                        return WorkerProtocol.SerializeOperationResult(result);
                    }

                case WorkerProtocol.OpLogoffSessionsBulk:
                    {
                        var result = new BrokerOperationService()
                            .LogoffSessionsBulk(site, GetLongList(parameters, "uids"));
                        StampOperation(result);
                        success = result.Success;
                        return WorkerProtocol.SerializeOperationResult(result);
                    }

                case WorkerProtocol.OpDisconnectSessionsBulk:
                    {
                        var result = new BrokerOperationService()
                            .DisconnectSessionsBulk(site, GetLongList(parameters, "uids"));
                        StampOperation(result);
                        success = result.Success;
                        return WorkerProtocol.SerializeOperationResult(result);
                    }

                case WorkerProtocol.OpPowerActionBulk:
                    {
                        var machineNames = GetStringList(parameters, "machineNames");
                        var raw = GetString(parameters, "powerAction");

                        BrokerPowerAction action;
                        if (!Enum.TryParse(raw, true, out action))
                        {
                            success = false;
                            return OperationError(op, "電源操作の種別が不正です: " + (raw ?? "(なし)"));
                        }

                        var result = new BrokerOperationService().PowerActionBulk(site, machineNames, action);
                        StampOperation(result);
                        success = result.Success;
                        return WorkerProtocol.SerializeOperationResult(result);
                    }

                default:
                    success = false;
                    return OperationError(op, "未知の操作です: " + (op ?? "(なし)"));
            }
        }

        private static string OperationError(string op, string message)
        {
            var result = BrokerOperationResult.Fail(op, message);
            StampOperation(result);
            return WorkerProtocol.SerializeOperationResult(result);
        }

        // ------------------------------------------------------------------
        // ワーカーPIDの記録（どのプロセスが接続を行ったかをログに残す）
        // ------------------------------------------------------------------

        private static string WorkerNote()
        {
            try
            {
                using (var proc = System.Diagnostics.Process.GetCurrentProcess())
                    return string.Format("接続ワーカー: PID {0} / 起動 {1:HH:mm:ss.fff}", proc.Id, proc.StartTime);
            }
            catch (Exception ex)
            {
                return "接続ワーカー: PID取得失敗: " + ex.Message;
            }
        }

        private static void StampConnection(SiteConnectionResult result)
        {
            if (result == null || result.Attempts == null) return;
            var note = WorkerNote();
            foreach (var attempt in result.Attempts)
                attempt.Diagnostics.Insert(0, note);
        }

        private static void StampOperation(BrokerOperationResult result)
        {
            if (result == null) return;
            if (result.Diagnostics == null) result.Diagnostics = new List<string>();
            result.Diagnostics.Insert(0, WorkerNote());
        }

        // ------------------------------------------------------------------
        // params 取り出し
        // ------------------------------------------------------------------

        private static string GetString(IDictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return null;
            return v as string ?? v.ToString();
        }

        private static bool GetBool(IDictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return false;
            return v is bool && (bool)v;
        }

        private static int GetInt(IDictionary<string, object> d, string key, int fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            if (v is double) return (int)(double)v;
            int n;
            return int.TryParse(v.ToString(), out n) ? n : fallback;
        }

        private static long GetLong(IDictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            if (v is double) return (long)(double)v;
            long n;
            return long.TryParse(v.ToString(), out n) ? n : 0;
        }

        private static List<string> GetStringList(IDictionary<string, object> d, string key)
        {
            var result = new List<string>();

            object v;
            if (d == null || !d.TryGetValue(key, out v) || !(v is List<object>)) return result;

            foreach (var item in (List<object>)v)
            {
                if (item == null) continue;
                var text = item as string ?? item.ToString();
                if (!string.IsNullOrWhiteSpace(text)) result.Add(text);
            }

            return result;
        }

        private static List<long> GetLongList(IDictionary<string, object> d, string key)
        {
            var result = new List<long>();

            object v;
            if (d == null || !d.TryGetValue(key, out v) || !(v is List<object>)) return result;

            foreach (var item in (List<object>)v)
            {
                if (item == null) continue;

                // JsonParser は数値を double で返す。
                if (item is double) { result.Add((long)(double)item); continue; }

                long n;
                if (long.TryParse(item.ToString(), out n)) result.Add(n);
            }

            return result;
        }

        private static void WriteErrorFile(string outPath, string message)
        {
            try
            {
                var result = BrokerOperationResult.Fail(null, message);
                StampOperation(result);
                File.WriteAllText(outPath, WorkerProtocol.SerializeOperationResult(result) + "\r\n",
                    new UTF8Encoding(false));
            }
            catch
            {
                // 結果ファイルすら書けない場合は終了コードだけで伝える。
            }
        }
    }
}
