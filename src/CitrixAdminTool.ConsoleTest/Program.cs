using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CitrixAdminTool.Core.Configuration;
using CitrixAdminTool.Core.Connection;
using CitrixAdminTool.Core.Models;

namespace CitrixAdminTool.ConsoleTest
{
    /// <summary>
    /// フェーズ1の接続検証エントリポイント。
    ///
    /// このアプリの目的は「CVAD管理環境で、設定した複数サイト・複数DDCへ
    /// 統合Windows認証で接続できるかを確かめ、その記録を持ち帰ること」に尽きる。
    /// GUIはフェーズ2で、この検証を通った接続層の上に載せる。
    /// </summary>
    internal static class Program
    {
        // 終了コード。バッチから呼ばれたときに結果を判別できるようにしておく。
        private const int ExitSuccess = 0;          // 全サイト接続成功
        private const int ExitSomeSitesFailed = 1;  // 1つ以上のサイトで全DDCが失敗
        private const int ExitConfigCreated = 2;    // 設定ファイルが無いのでテンプレートを作成した
        private const int ExitConfigInvalid = 3;    // 設定ファイルが読めない/不正
        private const int ExitUnexpected = 4;       // 想定外の例外

        private static int Main(string[] args)
        {
            if (args.Any(a => a == "-h" || a == "--help" || a == "/?"))
            {
                PrintUsage();
                return ExitSuccess;
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var verbose = args.Any(a => a == "-v" || a == "--verbose");

            // 第1引数でsites.jsonのパスを上書きできる。省略時は実行フォルダ直下。
            var pathArg = args.FirstOrDefault(a => !a.StartsWith("-") && !a.StartsWith("/"));
            var configPath = pathArg != null
                ? Path.GetFullPath(pathArg)
                : SiteConfigStore.GetDefaultConsolePath();

            using (var log = TeeWriter.Create(baseDir))
            {
                log.VerboseConsole = verbose;

                try
                {
                    return Run(log, configPath);
                }
                catch (Exception ex)
                {
                    log.WriteLine();
                    log.WriteLine("想定外のエラーで中断しました。");
                    log.WriteLine("  " + ex.GetType().FullName + ": " + ex.Message);
                    log.WriteLine(ex.StackTrace ?? string.Empty);
                    return ExitUnexpected;
                }
                finally
                {
                    log.WriteLine();
                    if (log.LogPath != null)
                        log.WriteLine("ログファイル: " + log.LogPath);
                    else
                        log.WriteLine("※ログファイルを作成できませんでした: " + log.LogFailureReason);
                }
            }
        }

        private static int Run(TeeWriter log, string configPath)
        {
            WriteHeader(log, configPath);

            if (!File.Exists(configPath))
                return CreateTemplateAndExit(log, configPath);

            List<SiteConnection> sites;
            try
            {
                sites = SiteConfigStore.Load(configPath);
            }
            catch (Exception ex)
            {
                log.WriteLine();
                log.WriteLine("設定ファイルを読み込めませんでした。");
                log.WriteLine("  " + ex.Message);
                return ExitConfigInvalid;
            }

            if (sites.Count == 0)
            {
                log.WriteLine();
                log.WriteLine("設定ファイルに接続対象のサイトが1件もありません: " + configPath);
                return ExitConfigInvalid;
            }

            log.WriteLine("対象サイト数: " + sites.Count);
            log.WriteLine();

            var service = new CitrixConnectionService();
            var results = new List<SiteConnectionResult>();

            foreach (var site in sites)
            {
                var result = service.TestSiteConnection(site);
                results.Add(result);
                WriteSiteResult(log, result);

                // SDKがこのマシンに無いと分かった時点で、残りのサイトを試す意味は無い。
                // 同じ環境エラーを並べるより、環境の問題として1度だけ報告する。
                if (result.SdkUnavailable)
                {
                    WriteSdkUnavailableNotice(log, sites.Count - results.Count);
                    break;
                }
            }

            WriteSummary(log, results, sites.Count);

            return results.Count == sites.Count && results.All(r => r.Success)
                ? ExitSuccess
                : ExitSomeSitesFailed;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("CVAD管理ツール 接続検証コンソール（フェーズ1）");
            Console.WriteLine();
            Console.WriteLine("使い方:");
            Console.WriteLine("  CitrixAdminTool.ConsoleTest.exe [sites.jsonのパス] [-v|--verbose]");
            Console.WriteLine();
            Console.WriteLine("  パスを省略すると、実行ファイルと同じフォルダの sites.json を読みます。");
            Console.WriteLine("  sites.json が無い場合は記入例入りのテンプレートを作成して終了します。");
            Console.WriteLine();
            Console.WriteLine("  -v / --verbose");
            Console.WriteLine("      詳細な診断情報を画面にも表示します。");
            Console.WriteLine("      指定しなくても、詳細はログファイルには必ず記録されます。");
            Console.WriteLine();
            Console.WriteLine("終了コード:");
            Console.WriteLine("  0 = 全サイト接続成功 / 1 = 失敗したサイトあり");
            Console.WriteLine("  2 = テンプレート作成 / 3 = 設定ファイル不正 / 4 = 想定外のエラー");
        }

        private static void WriteHeader(TeeWriter log, string configPath)
        {
            log.WriteLine("==========================================================");
            log.WriteLine(" CVAD 接続検証（フェーズ1 / 統合Windows認証）");
            log.WriteLine("==========================================================");
            log.WriteLine("実行日時       : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            log.WriteLine("実行マシン     : " + Environment.MachineName);
            log.WriteLine("実行ユーザー   : " + Environment.UserDomainName + "\\" + Environment.UserName);
            log.WriteLine("OS             : " + Environment.OSVersion.VersionString);
            // PID＋起動時刻でプロセスを一意に特定できるようにする。
            // 接続再利用の問題は同一プロセスかどうかで挙動が変わるため、
            // 複数ログの突き合わせにはこの情報が要る（2026-08-11の分析で判明）。
            try
            {
                using (var proc = System.Diagnostics.Process.GetCurrentProcess())
                {
                    log.WriteLine("プロセス       : PID {0} / {1} / 起動 {2:yyyy-MM-dd HH:mm:ss}",
                        proc.Id,
                        Environment.Is64BitProcess ? "64bit" : "32bit",
                        proc.StartTime);
                }
            }
            catch (Exception ex)
            {
                log.WriteLine("プロセス       : " + (Environment.Is64BitProcess ? "64bit" : "32bit")
                    + " (PID取得失敗: " + ex.Message + ")");
            }
            log.WriteLine("CLRバージョン  : " + Environment.Version);
            log.WriteLine("設定ファイル   : " + configPath);
            log.WriteLine();
        }

        private static int CreateTemplateAndExit(TeeWriter log, string configPath)
        {
            log.WriteLine("設定ファイルが見つかりませんでした: " + configPath);

            try
            {
                SiteConfigStore.WriteTemplate(configPath);
            }
            catch (Exception ex)
            {
                log.WriteLine("テンプレートの作成にも失敗しました: " + ex.Message);
                return ExitConfigInvalid;
            }

            log.WriteLine();
            log.WriteLine("記入例入りのテンプレートを作成しました。");
            log.WriteLine("メモ帳などで開き、実際のDDCのFQDNに書き換えてから、もう一度実行してください。");
            log.WriteLine();
            log.WriteLine("  " + configPath);
            return ExitConfigCreated;
        }

        private static void WriteSiteResult(TeeWriter log, SiteConnectionResult result)
        {
            log.WriteLine("[" + result.SiteDisplayName + "]");

            foreach (var attempt in result.Attempts)
            {
                var ddc = string.IsNullOrWhiteSpace(attempt.DdcAddress) ? "(DDC未設定)" : attempt.DdcAddress;
                var seconds = attempt.Elapsed.TotalSeconds.ToString("0.0");

                if (attempt.Success)
                {
                    log.WriteLine("  → {0} ... OK (Site: {1}, Ver: {2}, {3}s)",
                        ddc,
                        attempt.SiteName ?? "不明",
                        attempt.Version ?? "不明",
                        seconds);
                    // コンソール版は統合Windows認証のみ（別資格情報はGUI版のワーカー方式）。
                    log.WriteLine("    認証: 統合Windows認証（" + (attempt.AuthenticatedAs ?? "不明") + "）");
                    if (attempt.FunctionalLevel != null)
                        log.WriteLine("    機能レベル: " + attempt.FunctionalLevel);
                    if (attempt.SdkLoadMethod != null)
                        log.WriteLine("    SDK : " + attempt.SdkLoadMethod);
                }
                else
                {
                    log.WriteLine("  → {0} ... FAILED ({1}s)", ddc, seconds);
                    log.WriteLine("    理由: " + (attempt.ErrorMessage ?? "(不明)"));
                }

                // 診断情報は持ち帰り分析用。ログファイルには必ず全部残すが、
                // 画面はスタックトレースで埋もれないよう既定では省く（--verbose で表示）。
                foreach (var line in attempt.Diagnostics)
                    log.WriteDetailLine("    | " + line);
            }

            if (result.SdkUnavailable)
                log.WriteLine("  ** Citrix SDKが見つからないため、このサイトの残りのDDCへの試行は打ち切りました。");
            else if (!result.Success)
                log.WriteLine("  ** このサイトはどのDDCにも接続できませんでした。");
            else if (result.Attempts.Count > 1)
                log.WriteLine("  ** プライマリDDCが失敗したため代替DDCへフェイルオーバーしました。");

            log.WriteLine();
        }

        private static void WriteSdkUnavailableNotice(TeeWriter log, int skippedSiteCount)
        {
            log.WriteLine("----------------------------------------------------------");
            log.WriteLine(" Citrix Broker SDK をロードできませんでした。");
            log.WriteLine("----------------------------------------------------------");
            log.WriteLine(" これはDDCへの到達性ではなく、実行しているマシン側の問題です。");
            log.WriteLine(" 本ツールは Citrix Studio がインストールされた管理端末で実行してください。");
            log.WriteLine(" （開発端末やSDK未導入の端末では、DDCの設定が正しくても必ずこの結果になります）");

            if (skippedSiteCount > 0)
                log.WriteLine(" 残り {0} サイトの試行はスキップしました。", skippedSiteCount);

            log.WriteLine();
        }

        private static void WriteSummary(TeeWriter log, List<SiteConnectionResult> results, int totalSites)
        {
            var succeeded = results.Count(r => r.Success);
            var skipped = totalSites - results.Count;

            log.WriteLine("----------------------------------------------------------");
            log.WriteLine(" 集計: {0} サイト中 {1} サイト成功 / {2} サイト失敗{3}",
                totalSites,
                succeeded,
                results.Count - succeeded,
                skipped > 0 ? string.Format(" / {0} サイト未試行", skipped) : string.Empty);
            log.WriteLine("----------------------------------------------------------");

            foreach (var r in results)
            {
                if (r.Success)
                {
                    log.WriteLine("  OK     {0}  ({1})",
                        r.SiteDisplayName, r.SuccessfulAttempt.DdcAddress);
                }
                else
                {
                    var tried = r.Attempts
                        .Select(a => a.DdcAddress)
                        .Where(a => !string.IsNullOrWhiteSpace(a))
                        .ToArray();

                    log.WriteLine("  FAILED {0}  (試行: {1})",
                        r.SiteDisplayName,
                        tried.Length > 0 ? string.Join(", ", tried) : "なし");
                }
            }
        }
    }
}
