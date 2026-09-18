using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using CitrixAdminTool.Core.Interop;

namespace CitrixAdminTool.Core.Connection
{
    /// <summary>
    /// 常駐する統合認証ワーカーへの接続クライアント。
    ///
    /// 【なぜ常駐か】
    /// ワーカーはプロセス起動のたびに初回ウォームアップ（アセンブリロード・JIT・
    /// WCFスタック初期化。実測1.8〜3.3秒）を払う。統合認証は最も使う経路なので、
    /// ワーカーを1つ起動したまま複数リクエストを処理させ、ウォームアップを
    /// プロセスごと1回に抑える。2回目以降は実測0.3秒程度。
    ///
    /// 【役割分担】
    /// セキュリティ上、パイプは信頼できるGUI側がサーバとして所有する
    /// （ワーカーがサーバだと名前の横取りの余地がある）。ワーカーはクライアントとして
    /// 接続してくる。パイプは現在のユーザーのみアクセス可に制限する。
    /// なお本クライアントが扱うのは統合認証ワーカーのみ。別資格情報ワーカーは
    /// 安全性維持のため常駐させず使い捨て（WorkerConnectionService 側）。
    ///
    /// スレッド安全性: <see cref="Test"/> はロックで直列化する。1ワーカー＝1接続なので
    /// 同時に複数のリクエストは流さない。
    /// </summary>
    public sealed class PersistentWorkerClient : IDisposable
    {
        private readonly string _workerExePath;
        private readonly int _timeoutMs;
        private readonly object _lock = new object();

        private Process _worker;
        private NamedPipeServerStream _pipe;
        private bool _disposed;

        public PersistentWorkerClient(string workerExePath, int timeoutMs)
        {
            _workerExePath = workerExePath;
            _timeoutMs = timeoutMs;
        }

        /// <summary>
        /// 常駐ワーカーにリクエストを送り、レスポンスJSONを返す。
        /// 通信に失敗した場合は例外を投げる（呼び出し側が型に応じた失敗結果を組み立てる）。
        /// </summary>
        /// <param name="timeoutMs">
        /// この1リクエストに使うタイムアウト。0以下なら構築時の既定値。
        /// 一括操作は対象件数に比例して時間がかかるため、呼び出し側が延ばせるようにしてある。
        /// </param>
        public string SendRequest(IDictionary<string, object> request, int timeoutMs = 0)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var effectiveTimeout = timeoutMs > 0 ? timeoutMs : _timeoutMs;

            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PersistentWorkerClient));

                // ワーカーが落ちている可能性に備え、最大2回試す
                // （1回目でパイプが切れていたら作り直して2回目で成功させる）。
                Exception last = null;
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var requestSent = false;
                    try
                    {
                        EnsureStarted(effectiveTimeout);
                        return Exchange(request, effectiveTimeout, ref requestSent);
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        StopWorker(); // 壊れた接続は捨てて作り直す

                        // 【重要】リクエストを送った後の失敗は、ワーカー側で処理が
                        // 走ってしまった可能性がある。ここで再送すると、ログオフや
                        // メンテナンスモード切替を二度実行しかねないため再試行しない。
                        // 再試行してよいのは「送る前に接続が壊れていた」場合だけ。
                        if (requestSent) break;
                    }
                }

                throw new IOException("常駐ワーカーとの通信に失敗しました: "
                    + (last == null ? "不明" : last.Message), last);
            }
        }

        private string Exchange(IDictionary<string, object> request, int timeoutMs, ref bool requestSent)
        {
            // タイムアウト監視: 制限時間を超えたらワーカーを強制終了する。
            // これによりブロック中の Read が例外で抜け、リトライへ回る。
            using (var watchdog = new Timer(_ => KillWorkerProcess(), null, timeoutMs, Timeout.Infinite))
            {
                PipeMessaging.WriteMessage(_pipe, Json(request));
                requestSent = true;

                var responseJson = PipeMessaging.ReadMessage(_pipe);
                if (responseJson == null)
                    throw new IOException("ワーカーがパイプを閉じました（応答なし）。");

                return responseJson;
            }
        }

        private void EnsureStarted(int timeoutMs)
        {
            if (_worker != null && !_worker.HasExited && _pipe != null && _pipe.IsConnected)
                return;

            StopWorker();

            if (!File.Exists(_workerExePath))
                throw new FileNotFoundException("接続ワーカーが見つかりません: " + _workerExePath);

            var pipeName = "ShelfOps_" + Guid.NewGuid().ToString("N");

            _pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0, 0,
                BuildCurrentUserOnlySecurity());

            var connectTask = _pipe.WaitForConnectionAsync();

            var psi = new ProcessStartInfo
            {
                FileName = _workerExePath,
                Arguments = "--serve " + pipeName,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            _worker = Process.Start(psi);
            if (_worker == null)
                throw new InvalidOperationException("常駐ワーカーを起動できませんでした。");

            if (!connectTask.Wait(timeoutMs))
            {
                StopWorker();
                throw new TimeoutException("常駐ワーカーがパイプに接続しませんでした。");
            }
        }

        /// <summary>パイプを現在のユーザーだけがアクセスできるように制限する。</summary>
        private static PipeSecurity BuildCurrentUserOnlySecurity()
        {
            var security = new PipeSecurity();

            using (var identity = WindowsIdentity.GetCurrent())
            {
                security.AddAccessRule(new PipeAccessRule(
                    identity.User,
                    PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                    AccessControlType.Allow));
            }

            return security;
        }

        private void StopWorker()
        {
            // まずは行儀よく quit を送ってみる（接続が生きていれば）。
            if (_pipe != null && _pipe.IsConnected)
            {
                try
                {
                    PipeMessaging.WriteMessage(_pipe, Json(new Dictionary<string, object> { { "command", "quit" } }));
                }
                catch { /* 送れなければ後で強制終了する */ }
            }

            if (_pipe != null)
            {
                try { _pipe.Dispose(); } catch { }
                _pipe = null;
            }

            if (_worker != null)
            {
                try
                {
                    if (!_worker.WaitForExit(1500))
                        KillWorkerProcess();
                }
                catch { }
                try { _worker.Dispose(); } catch { }
                _worker = null;
            }
        }

        private void KillWorkerProcess()
        {
            var w = _worker;
            if (w == null) return;
            try { if (!w.HasExited) w.Kill(); }
            catch { /* 既に終了している等は無視 */ }
        }

        private static string Json(IDictionary<string, object> dict)
        {
            return CitrixAdminTool.Core.Configuration.Json.JsonWriter.Write(dict);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                StopWorker();
            }
        }
    }
}
