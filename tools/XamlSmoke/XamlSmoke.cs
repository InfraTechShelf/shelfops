// Copyright 2026 Infra Tech Shelf
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CitrixAdminTool.Core.Connection;
using CitrixAdminTool.Core.Models;
using CitrixAdminTool.Wpf.Services;
using CitrixAdminTool.Wpf.Services.Localization;
using CitrixAdminTool.Wpf.ViewModels;
using CitrixAdminTool.Wpf.Views;

/// <summary>
/// UI のスモークテスト。DDC が無くても確認できる範囲を、ビルド済みの ShelfOps.exe に対して検証する。
///
/// 確認すること:
///   - 各ウィンドウの XAML がパースでき、レイアウトまで通ること（XAML の誤りはコンパイルでは出ない）
///   - 日本語・英語の両方で開けること
///   - 未定義の文言キーが画面に出ていないこと（!!Key_Name!! の形で現れる）
///   - WPF のバインディング警告が無いこと（バインド失敗は例外にならず黙って空欄になる）
///   - 言語を切り替えたとき、DataGrid の列見出しまで追従すること
///     （列はビジュアルツリーの外にあるため、バインドの作りによってはここだけ残る）
///   - 電源操作の対象マシンの数え方
///
/// 実行方法は同じフォルダの run-smoke.ps1 を参照。
/// </summary>
internal static class XamlSmoke
{
    private class StubPrompt : ICredentialPrompt
    {
        public CredentialInput Prompt(string siteLabel) { return null; }
    }

    private class BindingErrorListener : TraceListener
    {
        public readonly List<string> Errors = new List<string>();
        public override void Write(string message) { }
        public override void WriteLine(string message) { Errors.Add(message); }
    }

    private static SiteConnection _site;

    [STAThread]
    private static int Main()
    {
        // App.xaml の共通スタイル（FieldLabel 等）を読むため、素の Application ではなく実物の App を使う。
        // InitializeComponent だけ呼べば OnStartup は走らないので MainWindow は開かない。
        var app = new CitrixAdminTool.Wpf.App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var failures = 0;

        var listener = new BindingErrorListener();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        _site = new SiteConnection
        {
            Id = "site-smoke",
            DisplayName = "Smoke test site",
            PrimaryDdc = "ddc01.example.invalid"
        };

        Loc.SetLanguage(AppLanguage.Japanese);
        failures += CheckAllWindows("ja");

        Loc.SetLanguage(AppLanguage.English);
        failures += CheckAllWindows("en");

        failures += CheckLanguageSwitch();
        failures += CheckPowerLogic();

        app.Shutdown();

        if (listener.Errors.Count > 0)
        {
            Console.WriteLine("  NG   : binding warnings (" + listener.Errors.Count + ")");
            foreach (var e in listener.Errors) Console.WriteLine("         " + e);
            failures += listener.Errors.Count;
        }
        else
        {
            Console.WriteLine("  OK   : no binding warnings");
        }

        Console.WriteLine(failures == 0 ? "All checks passed." : failures + " problem(s) found.");
        return failures;
    }

    private static int CheckAllWindows(string label)
    {
        var failures = 0;
        failures += Check(label + " / OperationDetailWindow", MakeDetail);
        failures += Check(label + " / MachinesWindow", MakeMachines);
        failures += Check(label + " / SessionsWindow", MakeSessions);
        failures += Check(label + " / AboutWindow", () => new AboutWindow());
        failures += Check(label + " / CredentialWindow", () => new CredentialWindow("Smoke test site"));
        return failures;
    }

    private static Window MakeDetail()
    {
        var targets = new List<BrokerTargetResult>
        {
            BrokerTargetResult.Ok(@"CORP\VDI-001"),
            BrokerTargetResult.Ng(@"CORP\VDI-002", "Access denied")
        };
        return new OperationDetailWindow("Details", "1 / 1", targets);
    }

    private static Window MakeMachines()
    {
        var vm = new MachinesViewModel(new WorkerConnectionService(), _site,
            AuthMode.IntegratedWindows, new StubPrompt());

        vm.Machines.Add(new BrokerMachine
        {
            MachineName = @"CORP\VDI-001",
            AssociatedUserNames = @"CORP\taro; CORP\hanako",
            CatalogName = "Catalog A",
            SessionSupport = "SingleSession",
            InMaintenanceMode = true
        });
        vm.Machines.Add(new BrokerMachine { MachineName = @"CORP\SRV-001", SessionSupport = "MultiSession" });

        return new MachinesWindow(vm);
    }

    private static Window MakeSessions()
    {
        var vm = new SessionsViewModel(new WorkerConnectionService(), _site,
            AuthMode.IntegratedWindows, new StubPrompt());

        vm.Sessions.Add(new BrokerSession
        {
            Uid = 1,
            UserName = @"CORP\taro",
            MachineName = @"CORP\VDI-001",
            SessionState = "Disconnected"
        });

        return new SessionsWindow(vm);
    }

    private static int CheckLanguageSwitch()
    {
        var failures = 0;

        Loc.SetLanguage(AppLanguage.Japanese);
        var window = MakeMachines();
        Show(window);

        var grid = FindGrid(window);
        var headerJa = grid == null ? null : grid.Columns[1].Header as string;

        Loc.SetLanguage(AppLanguage.English);
        window.UpdateLayout();
        var headerEn = grid == null ? null : grid.Columns[1].Header as string;
        var typeHeader = grid == null ? null : grid.Columns[5].Header as string;

        failures += Expect("column header resolves in Japanese", headerJa == "マシン名", headerJa);
        failures += Expect("column header follows language switch", headerEn == "Machine name", headerEn);
        failures += Expect("type column header is English", typeHeader == "Type", typeHeader);

        window.Close();
        return failures;
    }

    private static DataGrid FindGrid(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var grid = child as DataGrid;
            if (grid != null) return grid;

            var found = FindGrid(child);
            if (found != null) return found;
        }
        return null;
    }

    private static List<string> FindMissingKeys(DependencyObject root)
    {
        var found = new List<string>();
        Walk(root, found);
        return found;
    }

    private static void Walk(DependencyObject node, List<string> found)
    {
        var text = node as TextBlock;
        if (text != null && text.Text != null && text.Text.Contains("!!")) found.Add(text.Text);

        var content = node as ContentControl;
        if (content != null)
        {
            var s = content.Content as string;
            if (s != null && s.Contains("!!")) found.Add(s);
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i), found);
    }

    private static int CheckPowerLogic()
    {
        var vm = new SessionsViewModel(new WorkerConnectionService(), _site,
            AuthMode.IntegratedWindows, new StubPrompt());

        var all = new List<BrokerSession>
        {
            new BrokerSession { Uid = 1, UserName = @"CORP\a", MachineName = @"CORP\SRV-001" },
            new BrokerSession { Uid = 2, UserName = @"CORP\b", MachineName = @"CORP\SRV-001" },
            new BrokerSession { Uid = 3, UserName = @"CORP\c", MachineName = @"CORP\SRV-001" },
            new BrokerSession { Uid = 4, UserName = @"CORP\d", MachineName = @"CORP\VDI-001" }
        };
        foreach (var s in all) vm.Sessions.Add(s);

        var failures = 0;

        var picked = new List<BrokerSession> { all[0], all[1] };
        failures += Expect("same machine collapses to one target",
            vm.ResolveMachineNames(picked).Count == 1, null);

        var mixedCase = new List<BrokerSession>
        {
            new BrokerSession { Uid = 9, MachineName = @"corp\srv-001" },
            new BrokerSession { Uid = 10, MachineName = @"CORP\SRV-001" }
        };
        failures += Expect("machine names compare case-insensitively",
            vm.ResolveMachineNames(mixedCase).Count == 1, null);

        return failures;
    }

    private static int Expect(string name, bool condition, string actual)
    {
        Console.WriteLine((condition ? "  OK   : " : "  NG   : ") + name
            + (condition || actual == null ? "" : " -> actual: " + actual));
        return condition ? 0 : 1;
    }

    private static void Show(Window window)
    {
        // 画面には出さずにレイアウトまで走らせる。Show() しないと DataGrid の列テンプレートが評価されない。
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.ShowInTaskbar = false;
        window.Show();
        window.UpdateLayout();
    }

    private static int Check(string name, Func<Window> create)
    {
        try
        {
            var window = create();
            Show(window);

            var missing = FindMissingKeys(window);
            window.Close();

            if (missing.Count > 0)
            {
                Console.WriteLine("  NG   : " + name + " -> undefined string keys (" + missing.Count + ")");
                foreach (var m in missing.Distinct().Take(10)) Console.WriteLine("         " + m);
                return 1;
            }

            Console.WriteLine("  OK   : " + name);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  NG   : " + name + " -> " + ex.GetType().Name + ": " + ex.Message);
            if (ex.InnerException != null)
                Console.WriteLine("         inner: " + ex.InnerException.Message);
            return 1;
        }
    }
}
