using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using CitrixAdminTool.Core.Models;

namespace CitrixAdminTool.Core.Connection
{
    /// <summary>
    /// Broker管理操作（マシン一覧・メンテナンスモード切替など）を実行するサービス。
    ///
    /// 接続テストと同様に、サイトのDDC群をプライマリ→代替の順に試し、
    /// 最初に応答したDDCに対して操作を実行する（フェイルオーバー）。
    /// SDKロードは <see cref="BrokerSdkHelpers"/> と共通。
    ///
    /// このサービスはワーカープロセス内で動く。統合認証ならGUIと同じユーザー、
    /// 別資格情報ならワーカープロセス自体が指定アカウントのネットワークIDを持つ
    /// （呼び出し側の起動方法で決まる）。したがってここでは常に統合認証として実行する。
    /// </summary>
    public class BrokerOperationService
    {
        /// <summary>マシン一覧を取得する（読み取り）。</summary>
        /// <param name="maxCount">最大取得件数。0以下なら無制限（全件取得）。</param>
        public BrokerOperationResult ListMachines(SiteConnection site, string deliveryGroup, int maxCount)
        {
            return RunOnFirstReachableDdc(site, "listMachines", (ps, ddc, result) =>
            {
                ps.Commands.Clear();
                ps.Streams.ClearStreams();

                ps.AddCommand("Get-BrokerMachine").AddParameter("AdminAddress", ddc);
                if (!string.IsNullOrWhiteSpace(deliveryGroup))
                    ps.AddParameter("DesktopGroupName", deliveryGroup);
                // Get-Broker* の既定は250件で黙って打ち切られる。明示しない限り全件取る
                // （本番検証で「500台しか出ない」問題になった箇所。上限は指定時のみ効かせる）。
                ps.AddParameter("MaxRecordCount", maxCount > 0 ? maxCount : int.MaxValue);

                var output = ps.Invoke();
                BrokerSdkHelpers.CollectStreams(ps, result.Diagnostics);
                BrokerSdkHelpers.ThrowIfHadErrors(ps);

                foreach (var obj in output)
                {
                    if (obj == null) continue;
                    result.Machines.Add(ToMachine(obj));
                }

                result.Success = true;
                result.Message = string.Format("{0} 台のマシンを取得しました。", result.Machines.Count);
            });
        }

        /// <summary>
        /// マシンのメンテナンスモードを切り替える（書き込み）。
        /// 呼び出し側は事前にユーザーへ確認を取ること。
        /// </summary>
        public BrokerOperationResult SetMaintenanceMode(SiteConnection site, string machineName, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(machineName))
                return BrokerOperationResult.Fail("setMaintenance", "対象マシン名が指定されていません。");

            return SetMaintenanceModeBulk(site, new List<string> { machineName }, enabled);
        }

        /// <summary>
        /// 複数マシンのメンテナンスモードをまとめて切り替える（書き込み）。
        /// 呼び出し側は事前にユーザーへ確認を取ること。
        ///
        /// 【途中で失敗しても止めない】
        /// 権限やマシン状態によって一部だけ失敗するのは現実に起きる。1台の失敗で
        /// 残りを実行しないと、どこまで反映されたか分からない中途半端な状態になるため、
        /// 全台を試し、対象ごとの成否を <see cref="BrokerOperationResult.Targets"/> に積む。
        ///
        /// 【SDKロードは1回だけ】
        /// 1台ごとに <see cref="RunOnFirstReachableDdc"/> を呼ぶと、その都度
        /// Runspace生成とCitrixモジュールのImportが走り、台数に比例して数分かかる。
        /// ここでは1つのRunspace内で Set-BrokerMachine を台数分ループする。
        /// </summary>
        public BrokerOperationResult SetMaintenanceModeBulk(
            SiteConnection site, IList<string> machineNames, bool enabled)
        {
            var targets = (machineNames ?? new List<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .ToList();

            if (targets.Count == 0)
                return BrokerOperationResult.Fail("setMaintenance", "対象マシンが1台も指定されていません。");

            return RunOnFirstReachableDdc(site, "setMaintenance", (ps, ddc, result) =>
            {
                foreach (var machineName in targets)
                {
                    try
                    {
                        ps.Commands.Clear();
                        ps.Streams.ClearStreams();

                        // -MachineName で対象を指定し、InMaintenanceMode を設定する。
                        ps.AddCommand("Set-BrokerMachine")
                          .AddParameter("AdminAddress", ddc)
                          .AddParameter("MachineName", machineName)
                          .AddParameter("InMaintenanceMode", enabled);

                        ps.Invoke();
                        BrokerSdkHelpers.CollectStreams(ps, result.Diagnostics);
                        BrokerSdkHelpers.ThrowIfHadErrors(ps);

                        result.Targets.Add(BrokerTargetResult.Ok(machineName));
                        result.Diagnostics.Add(string.Format("[成功] {0} → {1}", machineName, enabled ? "ON" : "OFF"));
                    }
                    catch (SdkLoadException)
                    {
                        // SDKが無いのは環境の問題。台数分繰り返しても意味がないので投げ直す。
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // この1台は失敗。記録して次へ進む。
                        result.Targets.Add(BrokerTargetResult.Ng(machineName, ex.Message));
                        result.Diagnostics.Add(string.Format("[失敗] {0}: {1}", machineName, ex.Message));
                    }
                }

                // 単一対象のときだけ、反映後の状態を読み直して確認する
                // （書き込みの結果を事実として返すため）。一括では直後に一覧を取り直すので省く。
                var actualNote = string.Empty;
                if (targets.Count == 1 && result.Targets[0].Success)
                {
                    try
                    {
                        ps.Commands.Clear();
                        ps.Streams.ClearStreams();
                        ps.AddCommand("Get-BrokerMachine")
                          .AddParameter("AdminAddress", ddc)
                          .AddParameter("MachineName", targets[0]);
                        var after = ps.Invoke();
                        BrokerSdkHelpers.CollectStreams(ps, result.Diagnostics);

                        var machine = after.FirstOrDefault();
                        if (machine != null) result.Machines.Add(ToMachine(machine));

                        var actual = machine != null && BrokerSdkHelpers.GetBoolProp(machine, "InMaintenanceMode");
                        actualNote = string.Format("（反映後: {0}）", after.Count == 0 ? "確認できず" : (actual ? "ON" : "OFF"));
                    }
                    catch (Exception ex)
                    {
                        result.Diagnostics.Add("反映確認に失敗: " + ex.Message);
                    }
                }

                // 1台でも成功していれば操作自体は成立とみなす。内訳は Targets が持つ。
                result.Success = result.SuccessCount > 0;

                var mode = enabled ? "ON" : "OFF";
                if (targets.Count == 1)
                {
                    result.Message = result.Targets[0].Success
                        ? string.Format("[{0}] のメンテナンスモードを {1} にしました。{2}", targets[0], mode, actualNote)
                        : null;
                    if (!result.Targets[0].Success)
                        result.ErrorMessage = result.Targets[0].ErrorMessage;
                }
                else
                {
                    result.Message = string.Format("メンテナンスモードを {0} にしました: 成功 {1} 台 / 失敗 {2} 台（全 {3} 台）",
                        mode, result.SuccessCount, result.FailureCount, targets.Count);

                    if (result.FailureCount > 0 && result.SuccessCount == 0)
                        result.ErrorMessage = string.Format("{0} 台すべて失敗しました。", result.FailureCount);
                }
            });
        }

        // ==================================================================
        // デリバリーグループ
        // ==================================================================

        /// <summary>デリバリーグループ一覧を取得する（読み取り）。</summary>
        public BrokerOperationResult ListDesktopGroups(SiteConnection site)
        {
            return RunOnFirstReachableDdc(site, "listDesktopGroups", (ps, ddc, result) =>
            {
                ps.Commands.Clear();
                ps.Streams.ClearStreams();

                ps.AddCommand("Get-BrokerDesktopGroup")
                  .AddParameter("AdminAddress", ddc)
                  .AddParameter("MaxRecordCount", int.MaxValue); // 既定の250件打ち切りを外す

                var output = ps.Invoke();
                BrokerSdkHelpers.CollectStreams(ps, result.Diagnostics);
                BrokerSdkHelpers.ThrowIfHadErrors(ps);

                foreach (var obj in output)
                {
                    if (obj == null) continue;
                    result.DesktopGroups.Add(ToDesktopGroup(obj));
                }

                result.Success = true;
                result.Message = string.Format("{0} 件のデリバリーグループを取得しました。", result.DesktopGroups.Count);
            });
        }

        // ==================================================================
        // セッション操作
        // ==================================================================

        /// <summary>セッション一覧を取得する（読み取り）。</summary>
        /// <param name="maxCount">最大取得件数。0以下なら無制限（全件取得）。</param>
        public BrokerOperationResult ListSessions(SiteConnection site, string deliveryGroup, string userName, int maxCount)
        {
            return RunOnFirstReachableDdc(site, "listSessions", (ps, ddc, result) =>
            {
                ps.Commands.Clear();
                ps.Streams.ClearStreams();

                ps.AddCommand("Get-BrokerSession").AddParameter("AdminAddress", ddc);
                if (!string.IsNullOrWhiteSpace(deliveryGroup))
                    ps.AddParameter("DesktopGroupName", deliveryGroup);
                if (!string.IsNullOrWhiteSpace(userName))
                    ps.AddParameter("UserName", userName);
                ps.AddParameter("MaxRecordCount", maxCount > 0 ? maxCount : int.MaxValue);

                var output = ps.Invoke();
                BrokerSdkHelpers.CollectStreams(ps, result.Diagnostics);
                BrokerSdkHelpers.ThrowIfHadErrors(ps);

                foreach (var obj in output)
                {
                    if (obj == null) continue;
                    result.Sessions.Add(ToSession(obj));
                }

                result.Success = true;
                result.Message = string.Format("{0} 件のセッションを取得しました。", result.Sessions.Count);
            });
        }

        /// <summary>セッションをログオフする（Stop-BrokerSession・破壊的）。呼び出し側で確認を取ること。</summary>
        public BrokerOperationResult LogoffSession(SiteConnection site, long uid)
        {
            return SessionActionBulk(site, "logoffSession", new List<long> { uid }, "Stop-BrokerSession", "ログオフ");
        }

        /// <summary>セッションを切断する（Disconnect-BrokerSession）。呼び出し側で確認を取ること。</summary>
        public BrokerOperationResult DisconnectSession(SiteConnection site, long uid)
        {
            return SessionActionBulk(site, "disconnectSession", new List<long> { uid }, "Disconnect-BrokerSession", "切断");
        }

        /// <summary>複数セッションをまとめてログオフする（破壊的）。呼び出し側で確認を取ること。</summary>
        public BrokerOperationResult LogoffSessionsBulk(SiteConnection site, IList<long> uids)
        {
            return SessionActionBulk(site, "logoffSession", uids, "Stop-BrokerSession", "ログオフ");
        }

        /// <summary>複数セッションをまとめて切断する。呼び出し側で確認を取ること。</summary>
        public BrokerOperationResult DisconnectSessionsBulk(SiteConnection site, IList<long> uids)
        {
            return SessionActionBulk(site, "disconnectSession", uids, "Disconnect-BrokerSession", "切断");
        }

        /// <summary>
        /// Uidでセッションを引き当て、指定のアクションコマンドレットを実行する共通処理。
        /// 対象を取り違えないよう、必ず Uid で1件取得してから -InputObject で渡す。
        ///
        /// マシンの一括操作と同じ方針で、**途中で失敗しても止めず**に全件試し、
        /// 対象ごとの成否を <see cref="BrokerOperationResult.Targets"/> に積む。
        /// SDKロードとRunspace生成は1回だけ（件数分繰り返すと現実的な時間で終わらない）。
        /// </summary>
        private BrokerOperationResult SessionActionBulk(
            SiteConnection site, string operation, IList<long> uids, string actionCmdlet, string actionLabel)
        {
            var targets = (uids ?? new List<long>()).Where(u => u > 0).Distinct().ToList();

            if (targets.Count == 0)
                return BrokerOperationResult.Fail(operation, "対象セッションが1件も指定されていません。");

            return RunOnFirstReachableDdc(site, operation, (ps, ddc, result) =>
            {
                foreach (var uid in targets)
                {
                    // 対象名は「ユーザー / マシン」。引き当て前に失敗した場合に備えて Uid で仮置きする。
                    var targetName = "Uid " + uid;

                    try
                    {
                        // 1) 対象セッションを Uid で取得する。
                        ps.Commands.Clear();
                        ps.Streams.ClearStreams();
                        ps.AddCommand("Get-BrokerSession").AddParameter("AdminAddress", ddc).AddParameter("Uid", uid);
                        var found = ps.Invoke();
                        BrokerSdkHelpers.CollectStreams(ps, result.Diagnostics);
                        BrokerSdkHelpers.ThrowIfHadErrors(ps);

                        var session = found.FirstOrDefault();
                        if (session == null)
                        {
                            var reason = string.Format("対象セッション（Uid {0}）が見つかりません。既に終了した可能性があります。", uid);
                            result.Targets.Add(BrokerTargetResult.Ng(targetName, reason));
                            result.Diagnostics.Add("[失敗] " + targetName + ": " + reason);
                            continue;
                        }

                        var target = ToSession(session);
                        targetName = string.Format("{0} / {1}", target.UserName ?? "不明", target.MachineName ?? "不明");

                        // 2) 取得したセッションオブジェクトを -InputObject で渡してアクションを実行する。
                        ps.Commands.Clear();
                        ps.Streams.ClearStreams();
                        ps.AddCommand(actionCmdlet).AddParameter("AdminAddress", ddc).AddParameter("InputObject", session);
                        ps.Invoke();
                        BrokerSdkHelpers.CollectStreams(ps, result.Diagnostics);
                        BrokerSdkHelpers.ThrowIfHadErrors(ps);

                        result.Targets.Add(BrokerTargetResult.Ok(targetName));
                        result.Diagnostics.Add(string.Format("[成功] {0} を{1}", targetName, actionLabel));
                    }
                    catch (SdkLoadException)
                    {
                        // SDKが無いのは環境の問題。件数分繰り返しても意味がないので投げ直す。
                        throw;
                    }
                    catch (Exception ex)
                    {
                        result.Targets.Add(BrokerTargetResult.Ng(targetName, ex.Message));
                        result.Diagnostics.Add(string.Format("[失敗] {0}: {1}", targetName, ex.Message));
                    }
                }

                // 1件でも成功していれば操作自体は成立とみなす。内訳は Targets が持つ。
                result.Success = result.SuccessCount > 0;

                if (targets.Count == 1)
                {
                    var only = result.Targets[0];
                    if (only.Success)
                        result.Message = string.Format("セッション（{0}）を{1}しました。", only.TargetName, actionLabel);
                    else
                        result.ErrorMessage = only.ErrorMessage;
                }
                else
                {
                    result.Message = string.Format("{0}: 成功 {1} 件 / 失敗 {2} 件（全 {3} 件）",
                        actionLabel, result.SuccessCount, result.FailureCount, targets.Count);

                    if (result.FailureCount > 0 && result.SuccessCount == 0)
                        result.ErrorMessage = string.Format("{0} 件すべて失敗しました。", result.FailureCount);
                }
            });
        }

        // ==================================================================
        // 電源操作
        // ==================================================================

        /// <summary>
        /// マシンの電源操作を要求する（破壊的）。呼び出し側で確認を取ってから呼ぶこと。
        ///
        /// 【要求であって完了ではない】
        /// <c>New-BrokerHostingPowerAction</c> は電源操作を**キューに積む**コマンドレットで、
        /// 成功しても「受け付けられた」ことしか意味しない。実際の停止・再起動は
        /// ハイパーバイザー側で非同期に進むため、直後にマシンの状態を読んでも変わっていない。
        /// メッセージでもその旨を明示する（利用者が「効いていない」と誤解しないため）。
        ///
        /// 【電源管理されていないマシンでは失敗する】
        /// ホスティング接続に紐づいていないマシン（物理端末など）は電源操作の対象にできない。
        /// その場合はこのコマンドレットがエラーを返すので、対象ごとの失敗として記録する。
        ///
        /// マシン・セッションの一括操作と同じく、途中で失敗しても止めず、
        /// SDKロードは1回だけ（件数分のRunspace生成は現実的な時間で終わらない）。
        /// </summary>
        public BrokerOperationResult PowerActionBulk(
            SiteConnection site, IList<string> machineNames, BrokerPowerAction action)
        {
            var targets = (machineNames ?? new List<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (targets.Count == 0)
                return BrokerOperationResult.Fail("powerAction", "対象マシンが1台も指定されていません。");

            return RunOnFirstReachableDdc(site, "powerAction", (ps, ddc, result) =>
            {
                foreach (var machineName in targets)
                {
                    try
                    {
                        ps.Commands.Clear();
                        ps.Streams.ClearStreams();

                        // -Action は SDK の列挙名をそのまま渡す（enum の名前を一致させてある）。
                        ps.AddCommand("New-BrokerHostingPowerAction")
                          .AddParameter("AdminAddress", ddc)
                          .AddParameter("MachineName", machineName)
                          .AddParameter("Action", action.ToString());

                        ps.Invoke();
                        BrokerSdkHelpers.CollectStreams(ps, result.Diagnostics);
                        BrokerSdkHelpers.ThrowIfHadErrors(ps);

                        result.Targets.Add(BrokerTargetResult.Ok(machineName));
                        result.Diagnostics.Add(string.Format("[受付] {0} → {1}", machineName, action));
                    }
                    catch (SdkLoadException)
                    {
                        // SDKが無いのは環境の問題。台数分繰り返しても意味がないので投げ直す。
                        throw;
                    }
                    catch (Exception ex)
                    {
                        result.Targets.Add(BrokerTargetResult.Ng(machineName, ex.Message));
                        result.Diagnostics.Add(string.Format("[失敗] {0}: {1}", machineName, ex.Message));
                    }
                }

                // 1台でも受け付けられていれば操作自体は成立とみなす。内訳は Targets が持つ。
                result.Success = result.SuccessCount > 0;

                if (targets.Count == 1)
                {
                    var only = result.Targets[0];
                    if (only.Success)
                        result.Message = string.Format(
                            "[{0}] に {1} を要求しました。実際の反映にはしばらくかかります。", targets[0], action);
                    else
                        result.ErrorMessage = only.ErrorMessage;
                }
                else
                {
                    result.Message = string.Format(
                        "{0} を要求しました: 受付 {1} 台 / 失敗 {2} 台（全 {3} 台）。実際の反映にはしばらくかかります。",
                        action, result.SuccessCount, result.FailureCount, targets.Count);

                    if (result.FailureCount > 0 && result.SuccessCount == 0)
                        result.ErrorMessage = string.Format("{0} 台すべて失敗しました。", result.FailureCount);
                }
            });
        }

        // ------------------------------------------------------------------
        // DDCフェイルオーバー + SDKロードの共通処理
        // ------------------------------------------------------------------

        private delegate void OperationBody(PowerShell ps, string ddc, BrokerOperationResult result);

        private BrokerOperationResult RunOnFirstReachableDdc(SiteConnection site, string operation, OperationBody body)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));

            var result = new BrokerOperationResult { Operation = operation, Success = false };

            var ddcs = site.GetDdcsInTryOrder().ToList();
            if (ddcs.Count == 0)
            {
                result.ErrorMessage = "DDCが1つも設定されていません。";
                return result;
            }

            var lastError = "不明なエラー";

            foreach (var ddc in ddcs)
            {
                result.Diagnostics.Add("--- DDC: " + ddc + " ---");

                // 前のDDCで途中まで積んだ結果を持ち越さない。
                // 持ち越すと、フェイルオーバー時に一覧が二重になったり、
                // 対象ごとの結果が同じ対象について複数並んだりする。
                result.Machines.Clear();
                result.Sessions.Clear();
                result.Targets.Clear();

                try
                {
                    var iss = InitialSessionState.CreateDefault();
                    iss.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;

                    using (var runspace = RunspaceFactory.CreateRunspace(iss))
                    {
                        runspace.Open();
                        using (var ps = PowerShell.Create())
                        {
                            ps.Runspace = runspace;

                            var sdk = BrokerSdkHelpers.LoadBrokerSdk(ps, result.Diagnostics);
                            result.Diagnostics.Add("SDKロード方式: " + sdk);

                            body(ps, ddc, result);
                        }
                    }

                    if (result.Success)
                    {
                        result.WorkingDdc = ddc;
                        return result;
                    }

                    // 全対象が失敗した書き込み操作は、DDCに届いて実行された結果である
                    // （届かなければ例外になり、この行には来ない）。別のDDCで
                    // 再実行すると同じ書き込みを二度行うことになるため、ここで打ち切る。
                    if (result.Targets.Count > 0)
                    {
                        result.WorkingDdc = ddc;
                        return result;
                    }
                }
                catch (SdkLoadException ex)
                {
                    // SDKがこのマシンに無い。DDCを変えても同じなので打ち切る。
                    result.SdkUnavailable = true;
                    result.ErrorMessage = ex.Message;
                    result.Diagnostics.Add("例外の型: " + ex.GetType().FullName);
                    return result;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    result.Diagnostics.Add("例外の型: " + ex.GetType().FullName);
                    for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                        result.Diagnostics.Add("内部例外: " + inner.GetType().FullName + ": " + inner.Message);
                    // 次のDDCへフェイルオーバー
                }
            }

            result.ErrorMessage = lastError;
            return result;
        }

        private static BrokerSession ToSession(PSObject obj)
        {
            return new BrokerSession
            {
                Uid = BrokerSdkHelpers.GetLongProp(obj, "Uid"),
                UserName = BrokerSdkHelpers.GetProp(obj, "UserName"),
                MachineName = BrokerSdkHelpers.GetProp(obj, "MachineName"),
                DeliveryGroupName = BrokerSdkHelpers.GetProp(obj, "DesktopGroupName"),
                SessionState = BrokerSdkHelpers.GetProp(obj, "SessionState"),
                ClientName = BrokerSdkHelpers.GetProp(obj, "ClientName"),
                StartTime = BrokerSdkHelpers.GetProp(obj, "StartTime"),
                SessionStateChangeTime = BrokerSdkHelpers.GetProp(obj, "SessionStateChangeTime")
            };
        }

        private static BrokerDesktopGroup ToDesktopGroup(PSObject obj)
        {
            return new BrokerDesktopGroup
            {
                Uid = BrokerSdkHelpers.GetIntProp(obj, "Uid"),
                Name = BrokerSdkHelpers.GetProp(obj, "Name"),
                PublishedName = BrokerSdkHelpers.GetProp(obj, "PublishedName"),
                Description = BrokerSdkHelpers.GetProp(obj, "Description"),
                Enabled = BrokerSdkHelpers.GetBoolProp(obj, "Enabled"),
                InMaintenanceMode = BrokerSdkHelpers.GetBoolProp(obj, "InMaintenanceMode"),
                DesktopKind = BrokerSdkHelpers.GetProp(obj, "DesktopKind"),
                DeliveryType = BrokerSdkHelpers.GetProp(obj, "DeliveryType"),
                SessionSupport = BrokerSdkHelpers.GetProp(obj, "SessionSupport"),
                TotalDesktops = BrokerSdkHelpers.GetIntProp(obj, "TotalDesktops"),
                DesktopsAvailable = BrokerSdkHelpers.GetIntProp(obj, "DesktopsAvailable"),
                DesktopsInUse = BrokerSdkHelpers.GetIntProp(obj, "DesktopsInUse"),
                DesktopsUnregistered = BrokerSdkHelpers.GetIntProp(obj, "DesktopsUnregistered"),
                Sessions = BrokerSdkHelpers.GetIntProp(obj, "Sessions")
            };
        }

        private static BrokerMachine ToMachine(PSObject obj)
        {
            return new BrokerMachine
            {
                MachineName = BrokerSdkHelpers.GetProp(obj, "MachineName"),
                DnsName = BrokerSdkHelpers.GetProp(obj, "DNSName"),
                CatalogName = BrokerSdkHelpers.GetProp(obj, "CatalogName"),
                DeliveryGroupName = BrokerSdkHelpers.GetProp(obj, "DesktopGroupName"),
                RegistrationState = BrokerSdkHelpers.GetProp(obj, "RegistrationState"),
                PowerState = BrokerSdkHelpers.GetProp(obj, "PowerState"),
                SummaryState = BrokerSdkHelpers.GetProp(obj, "SummaryState"),
                SessionSupport = BrokerSdkHelpers.GetProp(obj, "SessionSupport"),
                // AssociatedUserNames は string[] で返るため、配列対応の取り出しを使う
                // （GetProp だと "System.String[]" という文字列になってしまう）。
                AssociatedUserNames = BrokerSdkHelpers.GetStringsProp(obj, "AssociatedUserNames"),
                InMaintenanceMode = BrokerSdkHelpers.GetBoolProp(obj, "InMaintenanceMode"),
                SessionCount = BrokerSdkHelpers.GetIntProp(obj, "SessionCount")
            };
        }
    }
}
