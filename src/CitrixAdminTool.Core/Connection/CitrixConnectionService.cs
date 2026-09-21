using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using CitrixAdminTool.Core.Authentication;
using CitrixAdminTool.Core.Models;

namespace CitrixAdminTool.Core.Connection
{
    /// <summary>
    /// Citrix Broker SDK（Windows PowerShell 5.1ホスト）への接続を担うサービス。
    ///
    /// このフェーズでの責務は「疎通確認」に限定する:
    ///   - サイトに設定されたDDC群（プライマリ→代替の順）に対して
    ///   - Get-BrokerSite を -AdminAddress 指定で実行し
    ///   - 成功すればサイト名/バージョンを取得する
    ///
    /// 設計上の判断:
    ///   - Runspaceは試行ごとに生成・破棄する（使い捨て）。常駐管理は後フェーズ。
    ///   - 認証は <see cref="IAuthenticationContext"/> 越しに行う。フェーズ1の実装は
    ///     統合Windows認証（パススルー）のみ。別資格情報（偽装）は後フェーズで差し込む。
    ///   - CVAD SDKはWindows PowerShell 5.1（.NET Framework）前提のため、
    ///     本プロジェクトは .NET Framework 4.8 でビルドすること。
    /// </summary>
    public class CitrixConnectionService
    {
        /// <summary>モジュール提供版のCitrix Brokerモジュール名。</summary>
        public const string BrokerModuleName = "Citrix.Broker.Commands";

        /// <summary>スナップイン提供版のCitrix Brokerスナップイン名。</summary>
        public const string BrokerSnapinName = "Citrix.Broker.Admin.V2";

        /// <summary>
        /// SDKが見つからないときに付け足す案内文。
        /// 開発端末で誤って実行したケースが最も多いはずなので、
        /// 「壊れている」ではなく「実行する場所が違う」と分かる文面にする。
        /// </summary>
        private const string SdkMissingHint =
            " ／ このマシンにCitrix SDKが入っていない可能性があります。"
            + "本ツールはCitrix Studioがインストールされた管理端末で実行してください。";

        /// <summary>
        /// サイトに対して接続テストを行う。プライマリDDCから順に試行し、
        /// 最初に成功したDDCで確定する（フェイルオーバー）。
        /// </summary>
        public SiteConnectionResult TestSiteConnection(SiteConnection site)
        {
            return TestSiteConnection(site, null);
        }

        /// <summary>
        /// 認証コンテキストを指定してサイトの接続テストを行う。
        /// </summary>
        /// <param name="site">対象サイト。</param>
        /// <param name="auth">
        /// 使用する認証コンテキスト。null を渡した場合、サイトの AuthMode が
        /// IntegratedWindows なら統合認証を内部で生成する。
        /// PromptForCredential のサイトで null を渡すのは呼び出し側の誤りであり、
        /// 資格情報を集める責務はUI層にあるため、ここでは失敗として報告する
        /// （黙って統合認証にフォールバックすると、誰の権限で操作したのかを偽ることになる）。
        /// </param>
        public SiteConnectionResult TestSiteConnection(SiteConnection site, IAuthenticationContext auth)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));

            var result = new SiteConnectionResult
            {
                SiteId = site.Id,
                SiteDisplayName = site.Label,
                Success = false
            };

            bool ownsAuth = false;

            if (auth == null)
            {
                if (site.AuthMode != AuthMode.IntegratedWindows)
                {
                    result.Attempts.Add(ConnectionResult.Fail(
                        site.PrimaryDdc,
                        string.Format("認証モード {0} のサイトには資格情報の指定が必要です。"
                                      + "（コンソール版は統合Windows認証のみ対応。GUI版を使用してください）",
                                      site.AuthMode),
                        TimeSpan.Zero));
                    return result;
                }

                auth = new IntegratedWindowsAuthenticationContext();
                ownsAuth = true;
            }

            var ddcs = site.GetDdcsInTryOrder().ToList();
            if (ddcs.Count == 0)
            {
                if (ownsAuth) auth.Dispose();

                result.Attempts.Add(ConnectionResult.Fail(
                    null, "DDCが1つも設定されていません。", TimeSpan.Zero));
                return result;
            }

            try
            {
                foreach (var ddc in ddcs)
                {
                    var attempt = TestSingleDdc(ddc, auth);
                    result.Attempts.Add(attempt);

                    if (attempt.Success)
                    {
                        result.Success = true;
                        result.SuccessfulAttempt = attempt;
                        break; // 最初に成功したDDCで確定
                    }

                    // SDKがこのマシンに無いなら、別のDDCを試しても同じ結果にしかならない。
                    // 同一のエラーをDDCの数だけ並べても診断の役に立たないので打ち切る。
                    if (attempt.IsSdkUnavailable)
                    {
                        result.SdkUnavailable = true;
                        break;
                    }
                }
            }
            finally
            {
                // 呼び出し側から渡されたコンテキストは、こちらで破棄してはいけない
                // （複数サイトの検証で使い回されるため）。
                if (ownsAuth) auth.Dispose();
            }

            return result;
        }

        /// <summary>
        /// 単一DDCに対してGet-BrokerSiteを実行し、疎通を確認する。
        /// </summary>
        /// <param name="ddc">対象DDCのFQDN。</param>
        /// <param name="auth">
        /// 実行するWindowsユーザーコンテキスト。null なら統合Windows認証。
        /// </param>
        public ConnectionResult TestSingleDdc(string ddc, IAuthenticationContext auth = null)
        {
            if (string.IsNullOrWhiteSpace(ddc))
                return ConnectionResult.Fail(ddc, "DDCアドレスが空です。", TimeSpan.Zero);

            bool ownsAuth = auth == null;
            if (ownsAuth) auth = new IntegratedWindowsAuthenticationContext();

            try
            {
                return auth.Run(() => RunSingleDdcTest(ddc, auth));
            }
            finally
            {
                if (ownsAuth) auth.Dispose();
            }
        }

        private ConnectionResult RunSingleDdcTest(string ddc, IAuthenticationContext auth)
        {
            var sw = Stopwatch.StartNew();
            var diagnostics = new List<string>();
            string sdkLoadMethod = null;

            var processUser = GetProcessUserName();
            bool? impersonationActive = null;

            diagnostics.Add("認証コンテキスト: " + auth.Description);
            diagnostics.Add("プロセス実行ユーザー: " + (processUser ?? "(不明)"));

            if (auth.ChangesNetworkIdentity)
                diagnostics.Add("指定された資格情報: " + (auth.GetRequestedUserName() ?? "(不明)"));

            try
            {
                // 実行ポリシーに左右されないよう、このRunspace内ではBypass相当で動かす。
                var iss = InitialSessionState.CreateDefault();
                iss.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;

                using (var runspace = RunspaceFactory.CreateRunspace(iss))
                {
                    // パイプラインを呼び出し元スレッドで実行させる。
                    // 既定では別スレッドで動く。.NETのExecutionContextが偽装を伝播するため
                    // 実測では偽装自体は届いていたが、偽装の有効範囲を推測に頼らないよう
                    // 明示的に同一スレッドへ固定する。
                    runspace.ThreadOptions = PSThreadOptions.UseCurrentThread;

                    runspace.Open();

                    using (var ps = PowerShell.Create())
                    {
                        ps.Runspace = runspace;

                        impersonationActive = ProbeImpersonation(ps, diagnostics);

                        sdkLoadMethod = LoadBrokerSdk(ps, diagnostics);
                        diagnostics.Add("SDKロード方式: " + sdkLoadMethod);

                        ps.Commands.Clear();
                        ps.Streams.ClearStreams();

                        // 疎通確認の本体: Get-BrokerSite -AdminAddress <ddc>
                        ps.AddCommand("Get-BrokerSite")
                          .AddParameter("AdminAddress", ddc);

                        Collection<PSObject> output = ps.Invoke();
                        CollectStreams(ps, diagnostics);
                        ThrowIfHadErrors(ps);

                        sw.Stop();

                        var siteObject = output.FirstOrDefault();
                        if (siteObject == null)
                        {
                            var empty = ConnectionResult.Fail(ddc,
                                "Get-BrokerSiteが結果を返しませんでした。", sw.Elapsed);
                            empty.AuthenticatedAs = processUser;
                            empty.RequestedIdentity = auth.ChangesNetworkIdentity ? auth.GetRequestedUserName() : null;
                            empty.ImpersonationActive = impersonationActive;
                            empty.SdkLoadMethod = sdkLoadMethod;
                            empty.Diagnostics = diagnostics;
                            return empty;
                        }

                        string siteName = GetProp(siteObject, "Name");

                        // サイトの機能レベル（例: L7_20）。製品バージョンとは別物だが、
                        // どの世代の機能が使えるかを示すためBroker操作の実装判断に効く。
                        string functionalLevel = GetProp(siteObject, "DefaultMinimumFunctionalLevel");

                        // 【実環境での確認結果 2026-08-10 / CL01・TEST\administrator】
                        // Get-BrokerSite は製品バージョンを一切返さない。返却プロパティを
                        // 全件ダンプして確認したところ、バージョンらしきものは
                        // InMemorySchemaAppliedVersion（内部スキーマ版）のみで、
                        // ControllerVersion / Version / ProductVersion のいずれも存在しなかった。
                        // 製品バージョンは Get-BrokerController の ControllerVersion から取る。
                        List<ControllerLicenseStatus> controllerStatuses;
                        string version = TryGetControllerVersion(ps, ddc, diagnostics, out controllerStatuses);

                        var ok = ConnectionResult.Ok(ddc, siteName, version,
                            processUser, sw.Elapsed);
                        ok.License = ReadLicenseInfo(siteObject, controllerStatuses);
                        ok.RequestedIdentity = auth.ChangesNetworkIdentity ? auth.GetRequestedUserName() : null;
                        ok.ImpersonationActive = impersonationActive;
                        ok.FunctionalLevel = functionalLevel;
                        ok.SdkLoadMethod = sdkLoadMethod;
                        ok.Diagnostics = diagnostics;
                        return ok;
                    }
                }
            }
            catch (SdkLoadException ex)
            {
                sw.Stop();

                diagnostics.Add("例外の型: " + ex.GetType().FullName);
                if (ex.InnerException != null)
                    diagnostics.Add("内部例外: " + ex.InnerException.GetType().FullName + ": " + ex.InnerException.Message);

                var fail = ConnectionResult.Fail(ddc, ex.Message, sw.Elapsed);
                fail.IsSdkUnavailable = true;
                fail.AuthenticatedAs = processUser;
                fail.RequestedIdentity = auth.ChangesNetworkIdentity ? auth.GetRequestedUserName() : null;
                fail.ImpersonationActive = impersonationActive;
                fail.Diagnostics = diagnostics;
                return fail;
            }
            catch (Exception ex)
            {
                sw.Stop();

                diagnostics.Add("例外の型: " + ex.GetType().FullName);
                for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    diagnostics.Add("内部例外: " + inner.GetType().FullName + ": " + inner.Message);
                if (!string.IsNullOrEmpty(ex.StackTrace))
                    diagnostics.Add("スタックトレース: " + ex.StackTrace);

                var fail = ConnectionResult.Fail(ddc, ex.Message, sw.Elapsed);
                fail.AuthenticatedAs = processUser;
                fail.RequestedIdentity = auth.ChangesNetworkIdentity ? auth.GetRequestedUserName() : null;
                fail.ImpersonationActive = impersonationActive;
                fail.SdkLoadMethod = sdkLoadMethod;
                fail.Diagnostics = diagnostics;
                return fail;
            }
        }

        /// <summary>プロセスを実行しているWindowsユーザー名（確認済みの事実）。</summary>
        private static string GetProcessUserName()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    return identity == null ? null : identity.Name;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// コマンドレットが動く場所でスレッド偽装トークンが有効かを確認する。
        ///
        /// 【なぜ必要か】
        /// 別資格情報モードの不具合は「偽装が効いていないのに成功と表示される」形で現れ、
        /// 誤った資格情報でも接続できてしまうまで気づけなかった。
        /// 偽装が実際にかかっていたかどうかを毎回ログに残し、推測に頼らずに切り分ける。
        ///
        /// なお LOGON32_LOGON_NEW_CREDENTIALS ではローカル識別子が変わらないため、
        /// GetCurrent().Name は偽装の有無にかかわらず元のユーザーを返す。
        /// 偽装の有無は GetCurrent(true) が null か否かでしか判別できない。
        /// </summary>
        private static bool? ProbeImpersonation(PowerShell ps, List<string> diagnostics)
        {
            try
            {
                ps.Commands.Clear();
                ps.Streams.ClearStreams();

                ps.AddScript(@"
                    $impersonating = [System.Security.Principal.WindowsIdentity]::GetCurrent($true) -ne $null
                    [pscustomobject]@{
                        Impersonating = $impersonating
                        ThreadId      = [System.Threading.Thread]::CurrentThread.ManagedThreadId
                        LocalIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
                    }
                ");

                var probe = ps.Invoke().FirstOrDefault();
                if (probe == null)
                {
                    diagnostics.Add("偽装状態の確認: 結果を取得できませんでした。");
                    return null;
                }

                var impersonating = probe.Properties["Impersonating"].Value as bool?;
                diagnostics.Add(string.Format(
                    "偽装状態の確認: スレッド偽装トークン={0} / スレッドID={1} / ローカル識別子={2}",
                    impersonating.HasValue ? (impersonating.Value ? "あり" : "なし") : "不明",
                    probe.Properties["ThreadId"].Value,
                    probe.Properties["LocalIdentity"].Value));

                return impersonating;
            }
            catch (Exception ex)
            {
                diagnostics.Add("偽装状態の確認に失敗: " + ex.Message);
                return null;
            }
            finally
            {
                ps.Commands.Clear();
                ps.Streams.ClearStreams();
            }
        }

        /// <summary>
        /// Get-BrokerSite の結果からライセンス構成を読む。
        ///
        /// プロパティ名は CVAD のバージョンで揺れる可能性があるため、無ければ null のまま
        /// （既存の方針どおり、取れないことを理由に落とさない）。
        /// </summary>
        private static LicenseInfo ReadLicenseInfo(PSObject siteObject, List<ControllerLicenseStatus> controllers)
        {
            var info = new LicenseInfo
            {
                LicenseServerName = GetProp(siteObject, "LicenseServerName"),
                LicenseServerPort = GetProp(siteObject, "LicenseServerPort"),
                ProductCode = GetProp(siteObject, "ProductCode"),
                ProductEdition = GetProp(siteObject, "ProductEdition"),
                LicensingModel = GetProp(siteObject, "LicensingModel"),
                Controllers = controllers ?? new List<ControllerLicenseStatus>()
            };

            bool grace;
            if (bool.TryParse(GetProp(siteObject, "LicensingGracePeriodActive"), out grace))
                info.GracePeriodActive = grace;

            int hours;
            if (int.TryParse(GetProp(siteObject, "LicensingGraceHoursLeft"), out hours))
                info.GraceHoursLeft = hours;

            return info;
        }

        /// <summary>
        /// 接続先DDCのCVAD製品バージョンを Get-BrokerController から取得する。
        ///
        /// 疎通そのものは Get-BrokerSite の成功で確定済みであり、バージョンは補助情報。
        /// そのためここで失敗しても例外にせず、診断ログに理由を残してnullを返す
        /// （Citrix管理者ロールの権限差でこのコマンドだけ通らない可能性があるため、
        ///  バージョンが取れないことを理由に「接続失敗」と報告してはいけない）。
        /// </summary>
        private static string TryGetControllerVersion(
            PowerShell ps, string ddc, List<string> diagnostics, out List<ControllerLicenseStatus> licenseStatuses)
        {
            licenseStatuses = new List<ControllerLicenseStatus>();

            try
            {
                ps.Commands.Clear();
                ps.Streams.ClearStreams();

                ps.AddCommand("Get-BrokerController")
                  .AddParameter("AdminAddress", ddc);

                var controllers = ps.Invoke();
                CollectStreams(ps, diagnostics);

                if (ps.HadErrors && ps.Streams.Error.Count > 0)
                {
                    diagnostics.Add("バージョン取得（Get-BrokerController）に失敗しました: "
                        + ps.Streams.Error[0]);
                    return null;
                }

                if (controllers.Count == 0)
                {
                    diagnostics.Add("Get-BrokerControllerがコントローラを返しませんでした。");
                    return null;
                }

                // 同じ結果からライセンスサーバーとの接続状況も拾う（DDC ごとに異なりうる）。
                // 追加の往復を発生させないため、バージョン取得と同じ呼び出しで済ませる。
                foreach (var c in controllers)
                {
                    if (c == null) continue;
                    licenseStatuses.Add(new ControllerLicenseStatus
                    {
                        DnsName = GetProp(c, "DNSName"),
                        LicensingServerState = GetProp(c, "LicensingServerState"),
                        LicensingGraceState = GetProp(c, "LicensingGraceState"),
                        State = GetProp(c, "State")
                    });
                }

                // サイト内の全コントローラが返るため、いま接続しているDDC自身の
                // バージョンを優先する（混在バージョン環境で誤った値を出さないため）。
                var self = controllers.FirstOrDefault(c => IsSameHost(GetProp(c, "DNSName"), ddc));
                var target = self ?? controllers[0];

                if (self == null)
                    diagnostics.Add("接続先DDCに一致するコントローラを特定できなかったため、"
                        + "先頭のコントローラ（" + (GetProp(target, "DNSName") ?? "不明") + "）のバージョンを使用します。");

                var version = FirstNonEmpty(
                    GetProp(target, "ControllerVersion"),
                    GetProp(target, "Version"));

                if (version == null)
                {
                    // Get-BrokerSite で同じ問題に当たった経緯があるため、ここでも
                    // 実際の返却プロパティを記録して次回の判断材料を残す。
                    diagnostics.Add("バージョン系プロパティを特定できませんでした。"
                        + "Get-BrokerControllerが返したプロパティ一覧: "
                        + string.Join(", ", target.Properties.Select(p => p.Name).ToArray()));
                }

                return version;
            }
            catch (Exception ex)
            {
                diagnostics.Add("バージョン取得（Get-BrokerController）で例外: "
                    + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 2つのホスト名が同じマシンを指すかを判定する。
        /// 設定にFQDNを書いてもコントローラ側が短縮名を返す場合があるため、
        /// 先頭ラベル同士の比較にもフォールバックする。
        /// </summary>
        private static bool IsSameHost(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

            a = a.Trim();
            b = b.Trim();

            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

            var labelA = a.Split('.')[0];
            var labelB = b.Split('.')[0];
            return string.Equals(labelA, labelB, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Citrix Broker SDKをロードする。
        ///
        /// CVADのバージョンによりSDKの提供形態が異なる（新しい版はモジュール、従来はスナップイン）ため、
        /// モジュール → スナップイン の順に試し、両方失敗したらエラーとする。
        /// </summary>
        /// <returns>成功したロード方式（"Module" または "PSSnapin"）。</returns>
        private static string LoadBrokerSdk(PowerShell ps, List<string> diagnostics)
        {
            ps.Commands.Clear();
            ps.Streams.ClearStreams();

            // モジュール版とスナップイン版のどちらでロードできたかを戻り値の文字列で受け取る。
            // C#側で2回Invokeするより、PowerShell側でtry/catchした方が
            // エラーレコードの扱いが素直になる。
            ps.AddScript(@"
                $ErrorActionPreference = 'Stop'

                $moduleError = $null
                try {
                    Import-Module " + BrokerModuleName + @" -ErrorAction Stop
                    'Module'
                    return
                } catch {
                    $moduleError = $_.Exception.Message
                }

                try {
                    Add-PSSnapin " + BrokerSnapinName + @" -ErrorAction Stop
                    'PSSnapin'
                    return
                } catch {
                    throw ('Citrix Broker SDK のロードに失敗しました。' +
                           'モジュール(" + BrokerModuleName + @"): ' + $moduleError +
                           ' / スナップイン(" + BrokerSnapinName + @"): ' + $_.Exception.Message)
                }
            ");

            Collection<PSObject> output;
            try
            {
                output = ps.Invoke();
                CollectStreams(ps, diagnostics);
                ThrowIfHadErrors(ps);
            }
            catch (Exception ex)
            {
                CollectStreams(ps, diagnostics);
                throw new SdkLoadException(ex.Message + SdkMissingHint, ex);
            }

            var method = output.Select(o => o == null ? null : o.BaseObject as string)
                               .FirstOrDefault(s => !string.IsNullOrEmpty(s));

            // Invokeが例外を投げずここまで来たのにマーカーが無いのは想定外。
            // 「ロードできた」と誤って進むより、明示的に失敗させる。
            if (method == null)
                throw new SdkLoadException(
                    "Citrix Broker SDK のロード結果を判定できませんでした"
                    + "（モジュール/スナップインいずれのマーカーも返りませんでした）。" + SdkMissingHint);

            return method;
        }

        /// <summary>
        /// PowerShellの各ストリームの内容を診断情報として回収する。
        /// 検証環境ではデバッガが使えないため、ここで拾えるだけ拾ってログに残す。
        /// </summary>
        private static void CollectStreams(PowerShell ps, List<string> diagnostics)
        {
            foreach (var w in ps.Streams.Warning) diagnostics.Add("[警告] " + w.Message);
            foreach (var e in ps.Streams.Error) diagnostics.Add("[エラー] " + e.ToString());
        }

        private static void ThrowIfHadErrors(PowerShell ps)
        {
            if (ps.HadErrors && ps.Streams.Error.Count > 0)
            {
                var first = ps.Streams.Error[0];
                throw new InvalidOperationException(first.ToString());
            }
        }

        private static string FirstNonEmpty(params string[] candidates)
        {
            return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
        }

        /// <summary>
        /// PSObjectからプロパティ値を安全に文字列として取り出す。
        /// 存在しないプロパティへのアクセスでも例外を投げずnullを返す。
        /// </summary>
        private static string GetProp(PSObject obj, string name)
        {
            try
            {
                var p = obj.Properties[name];
                if (p == null) return null;

                var value = p.Value;
                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
