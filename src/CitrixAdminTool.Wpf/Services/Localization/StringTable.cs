using System.Collections.Generic;

namespace CitrixAdminTool.Wpf.Services.Localization
{
    /// <summary>
    /// 画面・ログの全文言。キーごとに日本語と英語を**並べて**持つ。
    ///
    /// 言語ごとにファイルを分けると、片方だけ追記して訳が抜ける事故が起きる。
    /// 1つの表に並べておけば、行を足す時点で両方を書くことになり、
    /// 構造的に訳漏れが発生しない（未定義キーは画面に !!キー名!! と出る）。
    ///
    /// 書式指定（{0} など）は日英で同じ番号・同じ個数にすること。
    /// 検証用テストで両言語の書式指定の整合性を確認している。
    /// </summary>
    internal static class StringTable
    {
        private struct Entry
        {
            public readonly string Ja;
            public readonly string En;

            public Entry(string ja, string en)
            {
                Ja = ja;
                En = en;
            }
        }

        private static Entry E(string ja, string en)
        {
            return new Entry(ja, en);
        }

        /// <summary>指定言語の文言辞書を作る。</summary>
        public static Dictionary<string, string> Build(AppLanguage language)
        {
            var result = new Dictionary<string, string>(Table.Count);

            foreach (var pair in Table)
                result[pair.Key] = language == AppLanguage.English ? pair.Value.En : pair.Value.Ja;

            return result;
        }

        /// <summary>検証用: 全キーと、その日英の値を返す。</summary>
        public static IEnumerable<KeyValuePair<string, KeyValuePair<string, string>>> AllEntries()
        {
            foreach (var pair in Table)
            {
                yield return new KeyValuePair<string, KeyValuePair<string, string>>(
                    pair.Key, new KeyValuePair<string, string>(pair.Value.Ja, pair.Value.En));
            }
        }

        private static readonly Dictionary<string, Entry> Table = new Dictionary<string, Entry>
        {
            // ============================================================
            // 共通
            // ============================================================
            { "Common_Refresh",        E("更新", "Refresh") },
            { "Common_Close",          E("閉じる", "Close") },
            { "Common_Cancel",         E("キャンセル", "Cancel") },
            { "Common_Connect",        E("接続", "Connect") },
            { "Common_Save",           E("保存", "Save") },
            { "Common_Add",            E("追加", "Add") },
            { "Common_Duplicate",      E("複製", "Duplicate") },
            { "Common_Delete",         E("削除", "Delete") },
            { "Common_CopyForExcel",   E("Excelへコピー", "Copy for Excel") },
            { "Common_SaveCsv",        E("CSV保存...", "Save as CSV...") },
            { "Common_Details",        E("内訳", "Details") },
            { "Common_FilterAll",      E("(すべて)", "(All)") },
            { "Common_FilterNone",     E("(未所属)", "(Unassigned)") },
            { "Common_LabelSearch",    E("検索:", "Search:") },
            { "Common_LabelGroup",     E("グループ:", "Group:") },
            { "Common_Unknown",        E("不明", "Unknown") },
            { "Common_UnknownError",   E("不明なエラー", "Unknown error") },
            { "Common_Succeeded",      E("成功", "Succeeded") },
            { "Common_Failed",         E("失敗", "Failed") },
            { "Common_LogNote",        E("  （ログ: {0}）", "  (Log: {0})") },
            { "Common_DdcNote",        E("（DDC: {0}）", " (DDC: {0})") },
            { "Common_TipDetails",     E("直近の操作について、対象ごとの成功・失敗を表示します。",
                                        "Shows the per-target result of the most recent operation.") },

            // ============================================================
            // メインウィンドウ
            // ============================================================
            { "Main_WindowTitle",      E("ShelfOps (Alpha)", "ShelfOps (Alpha)") },
            { "Main_About",            E("バージョン情報", "About") },
            { "Main_TestSelected",     E("接続テスト", "Test connection") },
            { "Main_TestAll",          E("全サイト接続テスト", "Test all sites") },
            { "Main_Machines",         E("マシン一覧", "Machines") },
            { "Main_Sessions",         E("セッション一覧", "Sessions") },
            { "Main_ConfigFolder",     E("設定フォルダ", "Config folder") },
            { "Main_LogFolder",        E("ログフォルダ", "Log folder") },
            { "Main_SiteList",         E("サイト一覧", "Sites") },
            { "Main_SiteSettings",     E("サイト設定", "Site settings") },
            { "Main_ConnectionResult", E("接続結果", "Connection results") },
            { "Main_LabelLanguage",    E("言語:", "Language:") },
            { "Main_TipDuplicate",     E("選択中のサイトをDDC構成ごと複製します。IDと表示名は自動で付け直されます。",
                                        "Duplicates the selected site including its DDC configuration. A new ID and display name are assigned automatically.") },

            // ============================================================
            // サイト設定の項目
            // ============================================================
            { "Site_Id",               E("ID", "ID") },
            { "Site_DisplayName",      E("表示名", "Display name") },
            { "Site_PrimaryDdc",       E("プライマリDDC", "Primary DDC") },
            { "Site_AlternateDdc",     E("代替DDC", "Alternate DDCs") },
            { "Site_AlternateDdcHint", E("1行に1台。上から順にフェイルオーバーします。",
                                        "One per line. Tried in order for failover.") },
            { "Site_AuthMode",         E("認証モード", "Authentication") },
            { "Site_CredentialHint",   E("接続のたびに資格情報を入力します。入力内容は保存されません。指定したアカウントはネットワーク認証にのみ使われます。",
                                        "You are prompted for credentials each time you connect. Nothing is saved. The account is used for network authentication only.") },
            { "Site_NewSite",          E("新しいサイト", "New site") },
            { "Site_NewSiteUnnamed",   E("(新しいサイト)", "(New site)") },
            { "Site_Unnamed",          E("(名称未設定のサイト)", "(Unnamed site)") },
            { "Site_CopySuffix",       E("{0} のコピー", "{0} (copy)") },
            { "Site_CopyOfNew",        E("新しいサイトのコピー", "New site (copy)") },
            { "Site_ValidateId",       E("IDを入力してください。", "Enter an ID.") },
            { "Site_ValidateDdc",      E("DDCを1台以上入力してください（プライマリまたは代替）。",
                                        "Enter at least one DDC (primary or alternate).") },

            { "Auth_Integrated",       E("統合Windows認証（現在のログオンユーザー）",
                                        "Integrated Windows authentication (current user)") },
            { "Auth_Prompt",           E("別の資格情報（接続時に入力）",
                                        "Different credentials (prompt on connect)") },

            // ============================================================
            // メインウィンドウのメッセージ
            // ============================================================
            { "Main_SelectSite",       E("サイトを選択してください。", "Select a site.") },
            { "Main_NoConfig",         E("設定ファイルがありません。「追加」でサイトを登録してください。 ({0})",
                                        "No configuration file found. Use \"Add\" to register a site. ({0})") },
            { "Main_LoadedLegacy",     E("{0} を読み込みました。「保存」すると {1} に保存されます。",
                                        "Loaded {0}. It will be saved to {1} when you click \"Save\".") },
            { "Main_LoadedSites",      E("{0} 件のサイトを読み込みました。 ({1})",
                                        "Loaded {0} site(s). ({1})") },
            { "Main_LoadFailed",       E("設定ファイルを読み込めませんでした: {0}",
                                        "Could not load the configuration file: {0}") },
            { "Main_DuplicateId",      E("IDが重複しています: {0}", "Duplicate ID: {0}") },
            { "Main_Saved",            E("保存しました: {0}", "Saved: {0}") },
            { "Main_SaveFailed",       E("保存に失敗しました: {0}", "Could not save: {0}") },
            { "Main_SiteAdded",        E("サイトを追加しました。内容を編集して「保存」してください。",
                                        "Site added. Edit the details and click \"Save\".") },
            { "Main_SiteCopied",       E("[{0}] を複製しました。内容を確認して「保存」してください。",
                                        "Duplicated [{0}]. Review the details and click \"Save\".") },
            { "Main_SiteDeleted",      E("[{0}] を削除しました。「保存」で確定します。",
                                        "Deleted [{0}]. Click \"Save\" to confirm.") },
            { "Main_Connecting",       E("[{0}] に接続しています...", "Connecting to [{0}]...") },
            { "Main_TestDoneLog",      E("{0} サイトの接続テストが完了しました。ログ: {1}",
                                        "Connection test finished for {0} site(s). Log: {1}") },
            { "Main_TestDoneNoLog",    E("{0} サイトの接続テストが完了しました。※ログを保存できませんでした: {1}",
                                        "Connection test finished for {0} site(s). Note: the log could not be saved: {1}") },
            { "Main_TestNotRun",       E("接続テストは実行されませんでした。",
                                        "The connection test was not run.") },
            { "Main_TestAborted",      E("想定外のエラーで中断しました: ",
                                        "Aborted due to an unexpected error: ") },
            { "Main_TestError",        E("接続テスト中にエラーが発生しました。",
                                        "An error occurred during the connection test.") },
            { "Main_CredentialCancelled", E("[{0}] 資格情報の入力がキャンセルされました。",
                                        "[{0}] Credential entry was cancelled.") },
            { "Main_OpenFolderFailed", E("フォルダを開けませんでした: {0}", "Could not open the folder: {0}") },
            { "Main_StatusSdkMissing", E("Citrix SDKが見つかりません", "Citrix SDK not found") },
            { "Main_SiteError",        E("[{0}] {1}", "[{0}] {1}") },
            { "Main_ConfirmDiscard",   E("保存していない変更があります。保存せずに終了しますか？",
                                        "You have unsaved changes. Exit without saving?") },

            // ============================================================
            // マシン一覧
            // ============================================================
            { "Machines_Title",        E("マシン一覧 - {0}", "Machines - {0}") },
            { "Machines_ListName",     E("マシン一覧", "Machine list") },
            { "Machines_MaintOn",      E("メンテナンス ON", "Maintenance ON") },
            { "Machines_MaintOff",     E("メンテナンス OFF", "Maintenance OFF") },
            { "Machines_LabelCatalog", E("カタログ:", "Catalog:") },
            { "Machines_LabelType",    E("種別:", "Type:") },
            { "Machines_LabelMaint",   E("メンテ:", "Maint:") },
            { "Machines_TipGroup",     E("取得したマシンのデリバリーグループから選べます。(未所属) はグループ未割り当てのマシンです。",
                                        "Choose from the delivery groups found in the retrieved machines. (Unassigned) means no delivery group.") },
            { "Machines_TipCatalog",   E("取得したマシンのカタログから選べます。(未所属) はカタログ名が取得できなかったマシンです。",
                                        "Choose from the catalogs found in the retrieved machines. (Unassigned) means no catalog name was returned.") },
            { "Machines_TipType",      E("シングルセッション（VDI等）／マルチセッション（共有デスクトップ・アプリ）で絞り込みます。",
                                        "Filter by single-session (VDI) or multi-session (shared desktops and apps).") },
            { "Machines_TipMaint",     E("メンテナンスモードのON／OFFで絞り込みます。",
                                        "Filter by maintenance mode ON or OFF.") },
            { "Machines_TipSearch",    E("マシン名・DNS名・割り当てユーザーの部分一致で検索します（大文字小文字は区別しません）。",
                                        "Substring search over machine name, DNS name and assigned users (case-insensitive).") },
            { "Machines_HintReady",    E("「更新」でマシン一覧を取得します。", "Click \"Refresh\" to load the machine list.") },
            { "Machines_HintCredential", E("「更新」を押すと資格情報の入力を求めます。",
                                        "Clicking \"Refresh\" will prompt for credentials.") },
            { "Machines_Loading",      E("マシン一覧を取得しています...", "Loading machines...") },
            { "Machines_OpMaintenance", E("メンテナンスモード切替", "Maintenance mode change") },
            { "Machines_SelectTarget", E("対象マシンを一覧から選択してください。",
                                        "Select one or more machines from the list.") },
            { "Machines_SelectTargetHint", E("対象マシンを一覧から選択してください。（Ctrl・Shift クリックで複数選択できます）",
                                        "Select one or more machines from the list. (Use Ctrl+click or Shift+click for multiple selection.)") },
            { "Machines_SettingOne",   E("[{0}] のメンテナンスモードを {1} にしています...",
                                        "Setting maintenance mode of [{0}] to {1}...") },
            { "Machines_SettingMany",  E("{0} 台のメンテナンスモードを {1} にしています...",
                                        "Setting maintenance mode of {0} machine(s) to {1}...") },
            { "Machines_ToggleError",  E("切替中にエラー: {0}", "Error while changing maintenance mode: {0}") },
            { "Machines_SummaryAll",   E("全 {0} 台", "{0} machine(s)") },
            { "Machines_SummaryFiltered", E("表示 {0} 台 / 全 {1} 台", "Showing {0} of {1} machine(s)") },
            { "Machines_ConfirmTitle", E("メンテナンスモードの変更", "Change maintenance mode") },
            { "Machines_ConfirmOne",   E("[{0}] のメンテナンスモードを {1} にします。よろしいですか？",
                                        "Set maintenance mode of [{0}] to {1}. Continue?") },
            { "Machines_ConfirmMany",  E("{0} 台のメンテナンスモードを {1} にします。よろしいですか？\n\n{2}",
                                        "Set maintenance mode of {0} machine(s) to {1}. Continue?\n\n{2}") },
            { "Machines_DetailTitle",  E("メンテナンスモード切替の内訳", "Maintenance mode change details") },
            { "Machines_DetailSummary", E("成功 {0} 台 / 失敗 {1} 台（全 {2} 台）",
                                        "{0} succeeded / {1} failed (of {2})") },
            { "Machines_HintFailures", E("  ※「内訳」で失敗したマシンを確認できます",
                                        "  See \"Details\" for the machines that failed") },
            { "Machines_CsvSaved",     E("{0} 台をCSVに保存しました: {1}", "Saved {0} machine(s) to CSV: {1}") },
            { "Machines_Copied",       E("{0} 台をクリップボードにコピーしました（Excelに貼り付けられます）。",
                                        "Copied {0} machine(s) to the clipboard (ready to paste into Excel).") },
            { "Machines_CsvDialogTitle", E("マシン一覧をCSVに保存", "Save machine list as CSV") },
            { "Machines_MoreItems",    E("\n… ほか {0} 台", "\n... and {0} more") },

            { "SessionType_Single",    E("シングルセッション", "Single-session") },
            { "SessionType_Multi",     E("マルチセッション", "Multi-session") },
            { "SessionSupport_Single", E("シングル", "Single") },
            { "SessionSupport_Multi",  E("マルチ", "Multi") },

            // 列見出し
            { "Col_Maint",             E("メンテ", "Maint") },
            { "Col_Maintenance",       E("メンテナンス", "Maintenance") },
            { "Col_MachineName",       E("マシン名", "Machine name") },
            { "Col_DnsName",           E("DNS名", "DNS name") },
            { "Col_AssignedUsers",     E("割り当てユーザー", "Assigned users") },
            { "Col_Catalog",           E("カタログ", "Catalog") },
            { "Col_DeliveryGroup",     E("デリバリーグループ", "Delivery group") },
            { "Col_Type",              E("種別", "Type") },
            { "Col_Registration",      E("登録", "Registration") },
            { "Col_Power",             E("電源", "Power") },
            { "Col_State",             E("状態", "State") },
            { "Col_SessionCount",      E("セッション", "Sessions") },
            { "Col_User",              E("ユーザー", "User") },
            { "Col_Machine",           E("マシン", "Machine") },
            { "Col_Client",            E("クライアント", "Client") },
            { "Col_StartTime",         E("開始時刻", "Start time") },
            { "Col_StateChangeTime",   E("状態変更時刻", "State changed") },
            { "Col_Uid",               E("Uid", "Uid") },

            // ============================================================
            // セッション一覧
            // ============================================================
            { "Sessions_Title",        E("セッション一覧 - {0}", "Sessions - {0}") },
            { "Sessions_ListName",     E("セッション一覧", "Session list") },
            { "Sessions_Disconnect",   E("切断", "Disconnect") },
            { "Sessions_Logoff",       E("ログオフ", "Log off") },
            { "Sessions_LabelState",   E("状態:", "State:") },
            { "Sessions_TipGroup",     E("取得したセッションのデリバリーグループから選べます。",
                                        "Choose from the delivery groups found in the retrieved sessions.") },
            { "Sessions_TipState",     E("Active（接続中）／Disconnected（切断済み）で絞り込みます。\n切断されたまま放置されているセッションを探すのに使います。",
                                        "Filter by Active or Disconnected.\nUseful for finding sessions left disconnected.") },
            { "Sessions_TipSearch",    E("ユーザー名・マシン名の部分一致で検索します（大文字小文字は区別しません）。",
                                        "Substring search over user name and machine name (case-insensitive).") },
            { "Sessions_HintReady",    E("「更新」でセッション一覧を取得します。", "Click \"Refresh\" to load the session list.") },
            { "Sessions_HintCredential", E("「更新」を押すと資格情報の入力を求めます。",
                                        "Clicking \"Refresh\" will prompt for credentials.") },
            { "Sessions_Loading",      E("セッション一覧を取得しています...", "Loading sessions...") },
            { "Sessions_SummaryAll",   E("全 {0} 件", "{0} session(s)") },
            { "Sessions_SummaryFiltered", E("表示 {0} 件 / 全 {1} 件", "Showing {0} of {1} session(s)") },
            { "Sessions_SelectTarget", E("対象セッションを一覧から選択してください。",
                                        "Select one or more sessions from the list.") },
            { "Sessions_SelectTargetHint", E("対象セッションを一覧から選択してください。（Ctrl・Shift クリックで複数選択できます）",
                                        "Select one or more sessions from the list. (Use Ctrl+click or Shift+click for multiple selection.)") },
            { "Sessions_ActingOne",    E("セッション（{0}）を{1}しています...", "{1} session ({0})...") },
            { "Sessions_ActingMany",   E("{0} 件のセッションを{1}しています...", "{1} {0} session(s)...") },
            { "Sessions_ActionError",  E("{0}中にエラー: {1}", "Error during {0}: {1}") },
            { "Sessions_ConfirmLogoffTitle", E("セッションのログオフ", "Log off session") },
            { "Sessions_ConfirmLogoffOne", E("ユーザー [{0}]（マシン {1}）のセッションを【ログオフ】します。\n\n保存されていない作業は失われます。よろしいですか？",
                                        "Log off the session of user [{0}] on machine {1}.\n\nAny unsaved work will be lost. Continue?") },
            { "Sessions_ConfirmLogoffMany", E("{0} 件のセッションを【ログオフ】します。\n\n対象ユーザーの保存されていない作業は失われます。よろしいですか？\n\n{1}",
                                        "Log off {0} session(s).\n\nAny unsaved work of those users will be lost. Continue?\n\n{1}") },
            { "Sessions_ConfirmDisconnectTitle", E("セッションの切断", "Disconnect session") },
            { "Sessions_ConfirmDisconnectOne", E("ユーザー [{0}]（マシン {1}）のセッションを【切断】します。よろしいですか？",
                                        "Disconnect the session of user [{0}] on machine {1}. Continue?") },
            { "Sessions_ConfirmDisconnectMany", E("{0} 件のセッションを【切断】します。よろしいですか？\n\n{1}",
                                        "Disconnect {0} session(s). Continue?\n\n{1}") },
            { "Sessions_DetailTitle",  E("セッション操作の内訳", "Session operation details") },
            { "Sessions_DetailSummary", E("成功 {0} 件 / 失敗 {1} 件（全 {2} 件）",
                                        "{0} succeeded / {1} failed (of {2})") },
            { "Sessions_HintFailures", E("  ※「内訳」で失敗したセッションを確認できます",
                                        "  See \"Details\" for the sessions that failed") },
            { "Sessions_CsvSaved",     E("{0} 件をCSVに保存しました: {1}", "Saved {0} session(s) to CSV: {1}") },
            { "Sessions_Copied",       E("{0} 件をクリップボードにコピーしました（Excelに貼り付けられます）。",
                                        "Copied {0} session(s) to the clipboard (ready to paste into Excel).") },
            { "Sessions_CsvDialogTitle", E("セッション一覧をCSVに保存", "Save session list as CSV") },
            { "Sessions_MoreItems",    E("\n… ほか {0} 件", "\n... and {0} more") },
            { "Sessions_ActionLogoff", E("ログオフ", "log off") },
            { "Sessions_ActionDisconnect", E("切断", "disconnect") },

            // ============================================================
            // 電源操作
            // ============================================================
            { "Power_Menu",            E("電源操作 ▾", "Power ▾") },
            { "Power_MenuPlain",       E("電源操作", "Power") },
            { "Power_Shutdown",        E("シャットダウン", "Shut down") },
            { "Power_Restart",         E("再起動", "Restart") },
            { "Power_TurnOff",         E("強制電源オフ", "Force power off") },
            { "Power_Reset",           E("強制リセット", "Force reset") },
            { "Power_TipMenu",         E("選択したセッションが動いているマシンに対して電源操作を行います。\n同じマシン上の他のユーザーにも影響します。",
                                        "Performs a power operation on the machines hosting the selected sessions.\nOther users on the same machine are affected as well.") },
            { "Power_TipShutdown",     E("OSに正常終了を要求します。応答しないマシンには効きません。",
                                        "Asks the guest OS to shut down gracefully. Has no effect on an unresponsive machine.") },
            { "Power_TipRestart",      E("OSに正常終了を要求してから再起動します。応答しないマシンには効きません。",
                                        "Asks the guest OS to shut down gracefully, then restarts. Has no effect on an unresponsive machine.") },
            { "Power_TipTurnOff",      E("OSに終了処理をさせず電源を切ります。保存されていない作業は失われます。",
                                        "Cuts power without letting the OS shut down. Unsaved work is lost.") },
            { "Power_TipReset",        E("OSに終了処理をさせず電源を入れ直します。保存されていない作業は失われます。",
                                        "Power-cycles without letting the OS shut down. Unsaved work is lost.") },
            { "Power_ConfirmTitle",    E("マシンの{0}", "{0}") },
            { "Power_ConfirmHeader",   E("{0} 台のマシンに【{1}】を要求します。",
                                        "Request [{1}] on {0} machine(s).") },
            { "Power_ConfirmAffected", E("⚠ 選択したセッションは {0} 件ですが、これらのマシン上には合計 {1} 件のセッションがあります。",
                                        "WARNING: you selected {0} session(s), but these machines host {1} session(s) in total.") },
            { "Power_ConfirmAffected2", E("選択していないユーザーも同時に切断されます。",
                                        "Users you did not select will be disconnected as well.") },
            { "Power_ConfirmAffected3", E("（件数は前回の「更新」時点のもので、その後に増えた分は含まれません）",
                                        "(Counts are from the last refresh and do not include sessions started since then.)") },
            { "Power_WarnForced",      E("⚠ この操作はOSに終了処理をさせません。対象マシン上の保存されていない作業はすべて失われます。",
                                        "WARNING: this does not let the OS shut down. All unsaved work on the target machines will be lost.") },
            { "Power_WarnGraceful",    E("対象マシン上の保存されていない作業は失われる可能性があります。",
                                        "Unsaved work on the target machines may be lost.") },
            { "Power_ConfirmTail",     E("よろしいですか？", "Continue?") },
            { "Power_NoMachine",       E("選択したセッションからマシン名を特定できませんでした。",
                                        "Could not determine machine names from the selected sessions.") },
            { "Power_RequestingOne",   E("[{0}] に {1} を要求しています...", "Requesting {1} on [{0}]...") },
            { "Power_RequestingMany",  E("{0} 台のマシンに {1} を要求しています...", "Requesting {1} on {0} machine(s)...") },
            { "Power_Error",           E("電源操作中にエラー: {0}", "Error during power operation: {0}") },
            { "Power_OpLabel",         E("電源操作", "Power operation") },
            { "Power_DetailTitle",     E("電源操作の内訳", "Power operation details") },
            { "Power_HintFailures",    E("  ※「内訳」で失敗したマシンを確認できます",
                                        "  See \"Details\" for the machines that failed") },

            // ============================================================
            // 内訳ウィンドウ
            // ============================================================
            { "Detail_Title",          E("操作結果の内訳", "Operation details") },
            { "Detail_CopyList",       E("一覧をコピー", "Copy list") },
            { "Detail_TipCopy",        E("この内訳をタブ区切りでクリップボードにコピーします（Excelに貼り付けられます）。",
                                        "Copies this list to the clipboard as tab-separated text (ready to paste into Excel).") },
            { "Detail_LogNote",        E("全件の記録はログファイルにも残っています。",
                                        "The full record is also written to the log file.") },
            { "Detail_ColResult",      E("結果", "Result") },
            { "Detail_ColTarget",      E("対象", "Target") },
            { "Detail_ColReason",      E("理由", "Reason") },
            { "Detail_CopyTitle",      E("コピー", "Copy") },
            { "Detail_Copied",         E("{0} 件をクリップボードにコピーしました。", "Copied {0} row(s) to the clipboard.") },

            // ============================================================
            // 書き出し
            // ============================================================
            { "Export_NoRows",         E("書き出す行がありません。先に「更新」で一覧を取得してください。",
                                        "There is nothing to export. Click \"Refresh\" to load the list first.") },
            { "Export_CsvFilter",      E("CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
                                        "CSV files (*.csv)|*.csv|All files (*.*)|*.*") },
            { "Export_CsvFailed",      E("CSVを保存できませんでした: {0}", "Could not save the CSV file: {0}") },
            { "Export_ClipboardFailed", E("クリップボードにコピーできませんでした: {0}",
                                        "Could not copy to the clipboard: {0}") },
            { "Export_ActionCsv",      E("CSV保存", "Save as CSV") },
            { "Export_ActionClipboard", E("クリップボードにコピー", "Copy to clipboard") },
            { "Export_TipClipboard",   E("表をタブ区切りでクリップボードにコピーします。Excelにそのまま貼り付けられます。\n選択している行があればその行だけ、無ければ表示中の全行が対象です。",
                                        "Copies the table to the clipboard as tab-separated text, ready to paste into Excel.\nExports the selected rows, or all displayed rows if nothing is selected.") },
            { "Export_TipCsv",         E("表をCSVファイルに保存します（UTF-8 BOM付き・Excelで文字化けしません）。\n選択している行があればその行だけ、無ければ表示中の全行が対象です。",
                                        "Saves the table as a CSV file (UTF-8 with BOM, opens correctly in Excel).\nExports the selected rows, or all displayed rows if nothing is selected.") },

            // ============================================================
            // 資格情報ダイアログ
            // ============================================================
            { "Cred_Title",            E("資格情報の入力", "Enter credentials") },
            { "Cred_Domain",           E("ドメイン", "Domain") },
            { "Cred_UserName",         E("ユーザー名", "User name") },
            { "Cred_Password",         E("パスワード", "Password") },
            { "Cred_Hint",             E("ユーザー名を user@domain 形式で入力する場合、ドメイン欄は空のままで構いません。入力内容は保存されず、この接続にのみ使われます。",
                                        "If you enter the user name in user@domain form, you can leave the domain box empty. Nothing is saved; the credentials are used for this connection only.") },
            { "Cred_Cancelled",        E("資格情報の入力がキャンセルされました。", "Credential entry was cancelled.") },

            // ============================================================
            // バージョン情報
            // ============================================================
            { "About_Title",           E("バージョン情報", "About") },
            { "About_Version",         E("バージョン {0}", "Version {0}") },
            { "About_Description",     E("Citrix Virtual Apps and Desktops（オンプレミス）の運用管理ツールです。サイトへの接続確認、マシン一覧・メンテナンスモード、セッション一覧・切断・ログオフ・電源操作を行えます。",
                                        "An operations console for Citrix Virtual Apps and Desktops (on-premises). It provides connection testing, machine listing and maintenance mode, and session listing, disconnect, log off and power operations.") },
            { "About_AlphaWarning",    E("⚠ アルファ版です。予告なく仕様が変わる場合があり、不具合が含まれることがあります。メンテナンスモード・セッションの切断／ログオフ・電源操作は実環境に影響します。\n本ソフトウェアは無保証です。ご利用は自己責任でお願いします。",
                                        "WARNING: this is an alpha release. It may change without notice and may contain defects. Maintenance mode, session disconnect/log off and power operations affect your live environment.\nThis software is provided without warranty. Use it at your own risk.") },
            { "About_License",         E("オープンソースソフトウェアです（Apache License 2.0）。ソースコード: https://github.com/InfraTechShelf/shelfops",
                                        "Open source software (Apache License 2.0). Source code: https://github.com/InfraTechShelf/shelfops") },
            { "About_Trademark",       E("本ツールは個人が開発した非公式のサードパーティ製ツールであり、Cloud Software Group, Inc.（旧 Citrix Systems, Inc.）とは一切関係ありません。Citrix, Citrix Virtual Apps and Desktops 等は同社またはその関連会社の商標です。",
                                        "This is an unofficial third-party tool developed by an individual and is not affiliated with Cloud Software Group, Inc. (formerly Citrix Systems, Inc.) in any way. Citrix and Citrix Virtual Apps and Desktops are trademarks of that company or its affiliates.") },

            // ============================================================
            // 失敗メッセージ
            // ============================================================
            { "Err_SdkUnavailable",    E("{0} 失敗: Citrix SDKが見つかりません（管理端末で実行してください）。",
                                        "{0} failed: Citrix SDK not found. Run this tool on a machine with Citrix Studio installed.") },
            { "Err_OpFailed",          E("{0} 失敗: {1}", "{0} failed: {1}") },
            { "Err_LoadError",         E("取得中にエラー: {0}", "Error while loading: {0}") },
            { "Err_Unexpected",        E("想定外のエラーが発生しました。", "An unexpected error occurred.") },

            // ============================================================
            // ログファイルの見出し
            // ============================================================
            { "Log_HeaderTitle",       E("ShelfOps 実行ログ", "ShelfOps run log") },
            { "Log_RunAt",             E("実行日時", "Run at") },
            { "Log_RunOn",             E("実行マシン", "Run on") },
            { "Log_RunAs",             E("実行ユーザー", "Run as") },
            { "Log_Os",                E("OS", "OS") },
            { "Log_NoDdc",             E("(DDC未設定)", "(no DDC configured)") },
            { "Log_FunctionalLevel",   E("機能レベル", "Functional level") },
            { "Log_Process",           E("プロセス", "Process") },
            { "Log_ProcessInfo",       E("PID {0} / {1} / 起動 {2:yyyy-MM-dd HH:mm:ss}",
                                        "PID {0} / {1} / started {2:yyyy-MM-dd HH:mm:ss}") },
            { "Log_SdkAborted",        E("Citrix SDKが見つからないため、このサイトの残りのDDCへの試行は打ち切りました。",
                                        "Citrix SDK was not found, so the remaining DDCs for this site were not tried.") },
            { "Log_SdkNotReachability", E("これはDDCへの到達性ではなく、実行しているマシン側の問題です。",
                                        "This is not a DDC reachability problem; it is a problem with the machine running the tool.") },
            { "Log_NoDdcReachable",    E("このサイトはどのDDCにも接続できませんでした。",
                                        "None of the DDCs for this site could be reached.") },
            { "Log_FailedOver",        E("プライマリDDCが失敗したため代替DDCへフェイルオーバーしました。",
                                        "The primary DDC failed, so the tool failed over to an alternate DDC.") },
            { "Cred_Header",           E("[{0}] への接続に使用するアカウントを入力してください。",
                                        "Enter the account to use when connecting to [{0}].") },
            { "Cred_NeedUserName",     E("ユーザー名を入力してください。", "Enter a user name.") },
            { "Cred_NeedPassword",     E("パスワードを入力してください。", "Enter a password.") },
            { "Log_Site",              E("サイト", "Site") },
            { "Log_Operation",         E("操作", "Operation") },
            { "Log_Auth",              E("認証", "Auth") },
            { "Log_AuthIntegrated",    E("統合Windows認証", "Integrated Windows authentication") },
            { "Log_AuthCredential",    E("別資格情報（専用プロセス） {0}", "Alternate credentials (dedicated process) {0}") },
            { "Log_Ddc",               E("DDC", "DDC") },
            { "Log_Result",            E("結果", "Result") },
            { "Log_ResultSuccess",     E("成功", "Succeeded") },
            { "Log_ResultFailed",      E("失敗", "Failed") },
            { "Log_ResultSdkMissing",  E("失敗（Citrix SDKが見つかりません）", "Failed (Citrix SDK not found)") },
            { "Log_SdkHint",           E("実行しているマシン側の問題です。Citrix Studio がインストールされた管理端末で実行してください。",
                                        "This is a problem with the machine running the tool. Run it on a machine with Citrix Studio installed.") },
            { "Log_Reason",            E("理由", "Reason") },
            { "Log_TargetResults",     E("対象ごとの結果（成功 {0} / 失敗 {1} / 全 {2}）",
                                        "Per-target results ({0} succeeded / {1} failed / {2} total)") },
            { "Log_ExportTitle",       E("{0} の書き出し（{1}）", "Export of {0} ({1})") },
            { "Log_ExportCount",       E("件数", "Rows") },
            { "Log_ExportPath",        E("保存先", "Saved to") },
            { "Log_MachineTable",      E("マシン一覧（{0} 台）", "Machines ({0})") },
            { "Log_SessionTable",      E("セッション一覧（{0} 件）", "Sessions ({0})") },

            // 操作名（ログ用）
            { "Op_ListMachines",       E("マシン一覧取得（Get-BrokerMachine）", "List machines (Get-BrokerMachine)") },
            { "Op_SetMaintenance",     E("メンテナンスモード切替（Set-BrokerMachine）", "Change maintenance mode (Set-BrokerMachine)") },
            { "Op_ListSessions",       E("セッション一覧取得（Get-BrokerSession）", "List sessions (Get-BrokerSession)") },
            { "Op_LogoffSession",      E("セッションのログオフ（Stop-BrokerSession）", "Log off session (Stop-BrokerSession)") },
            { "Op_DisconnectSession",  E("セッションの切断（Disconnect-BrokerSession）", "Disconnect session (Disconnect-BrokerSession)") },
            { "Op_PowerAction",        E("マシンの電源操作（New-BrokerHostingPowerAction）", "Machine power operation (New-BrokerHostingPowerAction)") },
        };
    }
}
