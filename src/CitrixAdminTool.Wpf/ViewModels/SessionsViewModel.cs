using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CitrixAdminTool.Core.Connection;
using CitrixAdminTool.Core.Models;
using CitrixAdminTool.Wpf.Mvvm;
using CitrixAdminTool.Wpf.Services;
using CitrixAdminTool.Wpf.Services.Localization;

namespace CitrixAdminTool.Wpf.ViewModels
{
    /// <summary>
    /// 1サイトのセッション一覧と、ログオフ・切断操作を担うViewModel。
    ///
    /// ログオフ・切断は**エンドユーザーのセッションを止める破壊的操作**なので、
    /// 呼び出し側（View）で必ず確認を取ってから該当メソッドを呼ぶこと。
    /// すべての操作はワーカー経由（統合認証=常駐 / 別資格情報=使い捨て）で実行する。
    /// </summary>
    public class SessionsViewModel : ViewModelBase, IDisposable
    {
        /// <summary>デリバリーグループ絞り込みの「すべて」を表す選択肢。</summary>
        public static string FilterAll { get { return Loc.T("Common_FilterAll"); } }

        /// <summary>デリバリーグループ未所属のセッションだけを表す選択肢。</summary>
        public static string FilterNoGroup { get { return Loc.T("Common_FilterNone"); } }

        /// <summary>
        /// セッション状態の絞り込み選択肢。
        /// 「切断されたまま放置されているセッションを探してログオフする」という
        /// 実運用に直結するため、固定の選択肢として用意する。
        /// </summary>
        public static string StateAll { get { return Loc.T("Common_FilterAll"); } }
        public const string StateActive = "Active";
        public const string StateDisconnected = "Disconnected";

        private readonly WorkerConnectionService _service;
        private readonly SiteConnection _site;
        private readonly AuthMode _authMode;
        private readonly ICredentialPrompt _prompt;

        /// <summary>DDCから取得した全件。表示用の <see cref="Sessions"/> はここから絞り込む。</summary>
        private readonly List<BrokerSession> _allSessions = new List<BrokerSession>();

        private CredentialInput _credential;
        private bool _isBusy;
        private string _statusMessage;
        private string _deliveryGroupFilter = FilterAll;
        private string _userNameFilter;
        private string _sessionStateFilter = StateAll;
        private BrokerSession _selectedSession;
        private BrokerOperationResult _lastOperationResult;

        public SessionsViewModel(WorkerConnectionService service, SiteConnection site,
            AuthMode authMode, ICredentialPrompt prompt)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _site = site ?? throw new ArgumentNullException(nameof(site));
            _authMode = authMode;
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));

            Sessions = new ObservableCollection<BrokerSession>();
            DeliveryGroups = new ObservableCollection<string> { FilterAll };
            SessionStates = new ObservableCollection<string> { StateAll, StateActive, StateDisconnected };
            RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsBusy);

            _statusMessage = _authMode == AuthMode.PromptForCredential
                ? Loc.T("Sessions_HintCredential")
                : Loc.T("Sessions_HintReady");

            Loc.LanguageChanged += OnLanguageChanged;
        }

        /// <summary>
        /// 表示言語が変わったときに、文言を持つ表示物を作り直す。
        /// 選択肢は文言そのものが値なので、選択位置を覚えて位置で戻す
        /// （並び順は言語で変わらない）。
        /// </summary>
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            var groupIndex = DeliveryGroups.IndexOf(_deliveryGroupFilter);
            var stateIndex = SessionStates.IndexOf(_sessionStateFilter);

            SessionStates.Clear();
            SessionStates.Add(StateAll);
            SessionStates.Add(StateActive);
            SessionStates.Add(StateDisconnected);

            RebuildDeliveryGroups();

            _deliveryGroupFilter = groupIndex >= 0 && groupIndex < DeliveryGroups.Count
                ? DeliveryGroups[groupIndex] : FilterAll;
            RaisePropertyChanged(nameof(DeliveryGroupFilter));

            _sessionStateFilter = stateIndex >= 0 && stateIndex < SessionStates.Count
                ? SessionStates[stateIndex] : StateAll;
            RaisePropertyChanged(nameof(SessionStateFilter));

            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(FilterSummary));

            ApplyFilter();
        }

        /// <summary>画面に表示するセッション（絞り込み後）。</summary>
        public ObservableCollection<BrokerSession> Sessions { get; private set; }

        /// <summary>デリバリーグループ絞り込みの選択肢（取得結果から生成）。</summary>
        public ObservableCollection<string> DeliveryGroups { get; private set; }

        /// <summary>セッション状態の絞り込み選択肢（固定）。</summary>
        public ObservableCollection<string> SessionStates { get; private set; }

        public string Title { get { return Loc.F("Sessions_Title", _site.Label); } }

        public RelayCommand RefreshCommand { get; private set; }

        public BrokerSession SelectedSession
        {
            get { return _selectedSession; }
            set { SetProperty(ref _selectedSession, value); }
        }

        /// <summary>選択中のデリバリーグループ。変更すると即座に表示へ反映する。</summary>
        public string DeliveryGroupFilter
        {
            get { return _deliveryGroupFilter; }
            set { if (SetProperty(ref _deliveryGroupFilter, value)) ApplyFilter(); }
        }

        /// <summary>ユーザー名の部分一致検索（大文字小文字を区別しない）。</summary>
        public string UserNameFilter
        {
            get { return _userNameFilter; }
            set { if (SetProperty(ref _userNameFilter, value)) ApplyFilter(); }
        }

        /// <summary>セッション状態（Active / Disconnected）の絞り込み。</summary>
        public string SessionStateFilter
        {
            get { return _sessionStateFilter; }
            set { if (SetProperty(ref _sessionStateFilter, value)) ApplyFilter(); }
        }

        /// <summary>
        /// 直近の書き込み操作の結果。対象ごとの内訳を「内訳」ボタンから表示するために保持する。
        /// </summary>
        public BrokerOperationResult LastOperationResult
        {
            get { return _lastOperationResult; }
            private set
            {
                if (SetProperty(ref _lastOperationResult, value))
                    RaisePropertyChanged(nameof(HasOperationDetail));
            }
        }

        /// <summary>内訳を表示できるか（直近の操作に対象ごとの結果があるか）。</summary>
        public bool HasOperationDetail
        {
            get { return _lastOperationResult != null && _lastOperationResult.Targets.Count > 0; }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaisePropertyChanged(nameof(IsNotBusy));
                    RefreshCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsNotBusy { get { return !_isBusy; } }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { SetProperty(ref _statusMessage, value); }
        }

        // ------------------------------------------------------------------
        // セッション一覧の取得
        // ------------------------------------------------------------------

        public async Task RefreshAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                var result = await LoadSessionsCoreAsync();
                if (result != null)
                    StatusMessage = WithLogNote(StatusMessage, WriteOperationLog(result));
            }
            catch (Exception ex)
            {
                StatusMessage = Loc.F("Err_LoadError", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>IsBusyガードを持たない中核処理。操作後の自動更新からも呼べる。</summary>
        private async Task<BrokerOperationResult> LoadSessionsCoreAsync()
        {
            if (!EnsureCredential()) return null;

            StatusMessage = Loc.T("Sessions_Loading");
            var result = await Task.Run(() => QuerySessions());
            ApplyListResult(result);
            return result;
        }

        /// <summary>
        /// セッションを全件取得する。
        ///
        /// 絞り込みはDDC側に渡さずクライアント側で行う（<see cref="ApplyFilter"/>）。
        /// DDC側の -UserName は "DOMAIN\user" の完全一致が要求され、部分一致で探せなかったため
        /// （本番検証のフィードバック）。件数上限も設けない（0 = 無制限）。
        /// </summary>
        private BrokerOperationResult QuerySessions()
        {
            if (_authMode == AuthMode.PromptForCredential)
                return _service.ListSessionsWithCredential(
                    _site, null, null, 0,
                    _credential.Domain, _credential.UserName, _credential.Password);

            return _service.ListSessionsIntegrated(_site, null, null, 0);
        }

        // ------------------------------------------------------------------
        // ログオフ・切断（破壊的。呼び出し側で確認を取ってから呼ぶこと）
        // ------------------------------------------------------------------

        public Task LogoffAsync(IList<BrokerSession> sessions) { return SessionActionAsync(sessions, true); }
        public Task DisconnectAsync(IList<BrokerSession> sessions) { return SessionActionAsync(sessions, false); }

        /// <summary>
        /// 選択された全セッションをまとめてログオフ／切断する。
        /// 破壊的操作なので、呼び出し側（View）で件数を明示した確認を取ってから呼ぶこと。
        ///
        /// 途中で失敗しても止めず、対象ごとの成否を集計して返す（Core側の一括操作に委譲）。
        /// どのセッションが失敗したかは <see cref="LastOperationResult"/> の内訳で確認できる。
        /// </summary>
        private async Task SessionActionAsync(IList<BrokerSession> sessions, bool logoff)
        {
            if (IsBusy) return;

            var uids = (sessions ?? new List<BrokerSession>())
                .Where(s => s != null && s.Uid > 0)
                .Select(s => s.Uid)
                .Distinct()
                .ToList();

            if (uids.Count == 0)
            {
                StatusMessage = Loc.T("Sessions_SelectTarget");
                return;
            }

            if (!EnsureCredential()) return;

            var label = Loc.T(logoff ? "Sessions_ActionLogoff" : "Sessions_ActionDisconnect");

            IsBusy = true;
            LastOperationResult = null;
            StatusMessage = uids.Count == 1
                ? Loc.F("Sessions_ActingOne",
                    sessions.First(s => s != null && s.Uid > 0).UserName ?? Loc.T("Common_Unknown"), label)
                : Loc.F("Sessions_ActingMany", uids.Count, label);
            try
            {
                var result = await Task.Run(() => RunSessionAction(logoff, uids));

                // 破壊的操作は成否にかかわらず監査記録として必ずログに残す。
                var logFile = WriteOperationLog(result);
                LastOperationResult = result;

                if (result.Success)
                {
                    await LoadSessionsCoreAsync(); // 一覧を最新化（対象は消えるはず）

                    var note = result.Message
                        + (result.WorkingDdc != null ? Loc.F("Common_DdcNote", result.WorkingDdc) : string.Empty);

                    if (result.FailureCount > 0)
                        note += Loc.T("Sessions_HintFailures");

                    StatusMessage = WithLogNote(note, logFile);
                }
                else
                {
                    ApplyFailure(result, label);
                    StatusMessage = WithLogNote(StatusMessage, logFile);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = Loc.F("Sessions_ActionError", label, ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private BrokerOperationResult RunSessionAction(bool logoff, IList<long> uids)
        {
            if (_authMode == AuthMode.PromptForCredential)
            {
                return logoff
                    ? _service.LogoffSessionsBulkWithCredential(_site, uids, _credential.Domain, _credential.UserName, _credential.Password)
                    : _service.DisconnectSessionsBulkWithCredential(_site, uids, _credential.Domain, _credential.UserName, _credential.Password);
            }

            return logoff
                ? _service.LogoffSessionsBulkIntegrated(_site, uids)
                : _service.DisconnectSessionsBulkIntegrated(_site, uids);
        }

        // ------------------------------------------------------------------
        // マシンの電源操作（破壊的。呼び出し側で確認を取ってから呼ぶこと）
        // ------------------------------------------------------------------

        /// <summary>
        /// 選択されたセッションが動いているマシンに対して電源操作を要求する。
        ///
        /// 対象は「セッション」ではなく、そのセッションを**ホストしているマシン**である点に注意。
        /// 同じマシン上の他のセッションもすべて巻き添えになるため、
        /// 呼び出し側（View）は <see cref="CountSessionsOnMachines"/> で影響範囲を示したうえで
        /// 確認を取ること。
        /// </summary>
        public async Task PowerActionAsync(IList<BrokerSession> sessions, BrokerPowerAction action)
        {
            if (IsBusy) return;

            var machineNames = ResolveMachineNames(sessions);
            if (machineNames.Count == 0)
            {
                StatusMessage = Loc.T("Sessions_SelectTarget");
                return;
            }

            if (!EnsureCredential()) return;

            IsBusy = true;
            LastOperationResult = null;
            StatusMessage = machineNames.Count == 1
                ? Loc.F("Power_RequestingOne", machineNames[0], action)
                : Loc.F("Power_RequestingMany", machineNames.Count, action);
            try
            {
                var result = await Task.Run(() => RunPowerAction(machineNames, action));

                // 破壊的操作は成否にかかわらず監査記録として必ずログに残す。
                var logFile = WriteOperationLog(result);
                LastOperationResult = result;

                if (result.Success)
                {
                    // 電源操作は非同期に進むため、直後に一覧を取り直しても状態は変わっていない。
                    // それでも取り直すのは、既に消えたセッションを画面から落とすため。
                    await LoadSessionsCoreAsync();

                    var note = result.Message
                        + (result.WorkingDdc != null ? Loc.F("Common_DdcNote", result.WorkingDdc) : string.Empty);

                    if (result.FailureCount > 0)
                        note += Loc.T("Power_HintFailures");

                    StatusMessage = WithLogNote(note, logFile);
                }
                else
                {
                    ApplyFailure(result, Loc.T("Power_OpLabel"));
                    StatusMessage = WithLogNote(StatusMessage, logFile);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = Loc.F("Power_Error", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private BrokerOperationResult RunPowerAction(IList<string> machineNames, BrokerPowerAction action)
        {
            if (_authMode == AuthMode.PromptForCredential)
                return _service.PowerActionWithCredential(
                    _site, machineNames, action,
                    _credential.Domain, _credential.UserName, _credential.Password);

            return _service.PowerActionIntegrated(_site, machineNames, action);
        }

        /// <summary>
        /// 選択されたセッションから、電源操作の対象となるマシン名を重複なく取り出す。
        /// 同じマシンの複数セッションを選んでも、そのマシンへの操作は1回で足りる。
        /// </summary>
        public IList<string> ResolveMachineNames(IList<BrokerSession> sessions)
        {
            return (sessions ?? new List<BrokerSession>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.MachineName))
                .Select(s => s.MachineName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 指定マシン上にある**取得済みの全セッション数**を返す。選択の有無は問わない。
        ///
        /// マルチセッションのマシンを落とすと、選んだ1セッションだけでなく
        /// そのマシン上の全ユーザーが巻き添えになる。確認ダイアログで実際の影響範囲を
        /// 示すために使う。取得済みの一覧から数えるだけなので、追加のDDC問い合わせは不要。
        ///
        /// 注意: これは「更新」時点のスナップショットであり、その後に増えたセッションは
        /// 含まれない。確認ダイアログでもその旨に触れる。
        /// </summary>
        public int CountSessionsOnMachines(IEnumerable<string> machineNames)
        {
            if (machineNames == null) return 0;

            var targets = new HashSet<string>(machineNames, StringComparer.OrdinalIgnoreCase);

            return _allSessions.Count(s =>
                !string.IsNullOrWhiteSpace(s.MachineName) && targets.Contains(s.MachineName.Trim()));
        }

        // ------------------------------------------------------------------
        // エクスポート
        // ------------------------------------------------------------------

        /// <summary>
        /// 書き出す対象を決める。選択があれば選択行、無ければ表示中（絞り込み後）の全行。
        /// マシン一覧と同じ規則にそろえる。
        /// </summary>
        public IList<BrokerSession> ResolveExportRows(IList<BrokerSession> selected)
        {
            if (selected != null && selected.Count > 0)
                return selected.Where(s => s != null).ToList();

            return Sessions.ToList();
        }

        /// <summary>CSV／TSVに出す列。画面の列に加え、調査で要る Uid も含める。</summary>
        private static IList<ExportColumn<BrokerSession>> BuildColumns()
        {
            return new List<ExportColumn<BrokerSession>>
            {
                new ExportColumn<BrokerSession>(Loc.T("Col_User"), s => s.UserName),
                new ExportColumn<BrokerSession>(Loc.T("Col_Machine"), s => s.MachineName),
                new ExportColumn<BrokerSession>(Loc.T("Col_DeliveryGroup"), s => s.DeliveryGroupName),
                new ExportColumn<BrokerSession>(Loc.T("Col_State"), s => s.SessionState),
                new ExportColumn<BrokerSession>(Loc.T("Col_Client"), s => s.ClientName),
                new ExportColumn<BrokerSession>(Loc.T("Col_StartTime"), s => s.StartTime),
                new ExportColumn<BrokerSession>(Loc.T("Col_StateChangeTime"), s => s.SessionStateChangeTime),
                new ExportColumn<BrokerSession>(Loc.T("Col_Uid"), s => s.Uid.ToString())
            };
        }

        /// <summary>既定のファイル名（保存ダイアログの初期値）。</summary>
        public string BuildExportFileName()
        {
            return TableExporter.BuildFileName("sessions", _site.Id);
        }

        /// <summary>CSVとして保存する。保存先は呼び出し側がダイアログで決めてから渡す。</summary>
        public void ExportCsv(string path, IList<BrokerSession> rows)
        {
            var csv = TableExporter.ToCsv(rows, BuildColumns());

            string error;
            if (TableExporter.TryWriteCsvFile(path, csv, out error))
            {
                StatusMessage = Loc.F("Sessions_CsvSaved", rows.Count, path);
                WriteExportLog(Loc.T("Export_ActionCsv"), rows.Count, path);
            }
            else
            {
                StatusMessage = Loc.F("Export_CsvFailed", error);
            }
        }

        /// <summary>表をタブ区切りでクリップボードへ入れる（Excelへそのまま貼れる形）。</summary>
        public void CopyToClipboard(IList<BrokerSession> rows)
        {
            var tsv = TableExporter.ToTsv(rows, BuildColumns());

            string error;
            if (TableExporter.TrySetClipboard(tsv, out error))
            {
                StatusMessage = Loc.F("Sessions_Copied", rows.Count);
                WriteExportLog(Loc.T("Export_ActionClipboard"), rows.Count, null);
            }
            else
            {
                StatusMessage = Loc.F("Export_ClipboardFailed", error);
            }
        }

        /// <summary>持ち出しの記録をログに残す（中身は書かない）。</summary>
        private void WriteExportLog(string action, int count, string path)
        {
            var content = ConnectionReportFormatter.FormatExportReport(
                _site.Label, Loc.T("Sessions_ListName"), action, count, path);

            string failureReason;
            RunLogWriter.Write(content, out failureReason);
        }

        // ------------------------------------------------------------------

        private void ApplyListResult(BrokerOperationResult result)
        {
            if (result.Success)
            {
                _allSessions.Clear();
                _allSessions.AddRange(result.Sessions);

                RebuildDeliveryGroups();
                ApplyFilter();

                StatusMessage = (result.Message ?? Loc.T("Sessions_ListName"))
                    + (result.WorkingDdc != null ? Loc.F("Common_DdcNote", result.WorkingDdc) : string.Empty);
            }
            else
            {
                ApplyFailure(result, Loc.T("Sessions_ListName"));
            }
        }

        /// <summary>取得結果からデリバリーグループの選択肢を作り直す。</summary>
        private void RebuildDeliveryGroups()
        {
            var current = _deliveryGroupFilter;

            var names = _allSessions
                .Select(s => s.DeliveryGroupName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            DeliveryGroups.Clear();
            DeliveryGroups.Add(FilterAll);
            if (_allSessions.Any(s => string.IsNullOrWhiteSpace(s.DeliveryGroupName)))
                DeliveryGroups.Add(FilterNoGroup);
            foreach (var n in names) DeliveryGroups.Add(n);

            _deliveryGroupFilter = DeliveryGroups.Contains(current) ? current : FilterAll;
            RaisePropertyChanged(nameof(DeliveryGroupFilter));
        }

        /// <summary>
        /// 全件から現在の絞り込み条件で表示用一覧を作る。
        /// デリバリーグループは完全一致、ユーザー名は部分一致（マシン名も対象にする）。
        /// </summary>
        private void ApplyFilter()
        {
            var group = _deliveryGroupFilter;
            var keyword = (_userNameFilter ?? string.Empty).Trim();

            IEnumerable<BrokerSession> query = _allSessions;

            if (group == FilterNoGroup)
                query = query.Where(s => string.IsNullOrWhiteSpace(s.DeliveryGroupName));
            else if (!string.IsNullOrEmpty(group) && group != FilterAll)
                query = query.Where(s => string.Equals(s.DeliveryGroupName, group, StringComparison.OrdinalIgnoreCase));

            if (keyword.Length > 0)
                query = query.Where(s =>
                    IndexOfIgnoreCase(s.UserName, keyword) >= 0 ||
                    IndexOfIgnoreCase(s.MachineName, keyword) >= 0);

            // セッション状態。SDKの値は "Active" / "Disconnected" 等の英語表記。
            if (!string.IsNullOrEmpty(_sessionStateFilter) && _sessionStateFilter != StateAll)
                query = query.Where(s =>
                    string.Equals(s.SessionState, _sessionStateFilter, StringComparison.OrdinalIgnoreCase));

            var selectedUid = SelectedSession == null ? (long?)null : SelectedSession.Uid;

            Sessions.Clear();
            foreach (var s in query) Sessions.Add(s);

            if (selectedUid.HasValue)
                SelectedSession = Sessions.FirstOrDefault(s => s.Uid == selectedUid.Value);

            RaisePropertyChanged(nameof(FilterSummary));
        }

        private static int IndexOfIgnoreCase(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack)) return -1;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>「表示 N 件 / 全 M 件」の表示。</summary>
        public string FilterSummary
        {
            get
            {
                if (_allSessions.Count == 0) return string.Empty;
                return Sessions.Count == _allSessions.Count
                    ? Loc.F("Sessions_SummaryAll", _allSessions.Count)
                    : Loc.F("Sessions_SummaryFiltered", Sessions.Count, _allSessions.Count);
            }
        }

        private void ApplyFailure(BrokerOperationResult result, string label)
        {
            StatusMessage = result.SdkUnavailable
                ? Loc.F("Err_SdkUnavailable", label)
                : Loc.F("Err_OpFailed", label, result.ErrorMessage ?? Loc.T("Common_UnknownError"));
        }

        private string WriteOperationLog(BrokerOperationResult result)
        {
            var content = ConnectionReportFormatter.FormatOperationReport(_site.Label, result);
            string failureReason;
            var path = RunLogWriter.Write(content, out failureReason);
            return path == null ? null : System.IO.Path.GetFileName(path);
        }

        private static string WithLogNote(string status, string logFileName)
        {
            return logFileName == null ? status : status + Loc.F("Common_LogNote", logFileName);
        }

        private bool EnsureCredential()
        {
            if (_authMode != AuthMode.PromptForCredential) return true;
            if (_credential != null) return true;

            var input = _prompt.Prompt(_site.Label);
            if (input == null)
            {
                StatusMessage = Loc.T("Cred_Cancelled");
                return false;
            }

            _credential = input;
            return true;
        }

        public void Dispose()
        {
            // ウィンドウを閉じたあとも言語変更を受け取り続けないよう、必ず外す。
            Loc.LanguageChanged -= OnLanguageChanged;

            var cred = _credential;
            _credential = null;
            if (cred != null) cred.Dispose();
        }
    }
}
