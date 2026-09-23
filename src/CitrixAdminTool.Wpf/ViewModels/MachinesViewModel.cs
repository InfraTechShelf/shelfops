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
    /// 1サイトのマシン一覧とメンテナンスモード操作を担うViewModel。
    ///
    /// すべての操作は <see cref="WorkerConnectionService"/> 経由（統合認証は常駐ワーカー、
    /// 別資格情報は使い捨てワーカー）で実行し、接続分離の安全性を保つ。
    ///
    /// 別資格情報サイトでは、このウィンドウを開いている間だけ資格情報を保持し
    /// （初回操作時に1回入力）、閉じるときに破棄する。ディスクには一切書かない。
    /// </summary>
    public class MachinesViewModel : ViewModelBase, IDisposable
    {
        // 絞り込みの選択肢は表示文言そのものを値として使う（ComboBox が文字列を並べるため）。
        // 言語を切り替えると文言が変わるので const にはできず、都度引き直す。
        // 切り替え時の選択の対応付けは OnLanguageChanged が位置で行う。

        /// <summary>デリバリーグループ絞り込みの「すべて」を表す選択肢。</summary>
        public static string FilterAll { get { return Loc.T("Common_FilterAll"); } }

        /// <summary>デリバリーグループ未所属のマシンだけを表す選択肢。</summary>
        public static string FilterNoGroup { get { return Loc.T("Common_FilterNone"); } }

        /// <summary>セッション種別の絞り込み選択肢。</summary>
        public static string SessionTypeAll { get { return Loc.T("Common_FilterAll"); } }
        public static string SessionTypeSingle { get { return Loc.T("SessionType_Single"); } }
        public static string SessionTypeMulti { get { return Loc.T("SessionType_Multi"); } }

        /// <summary>メンテナンスモードの絞り込み選択肢。ON/OFF は言語を問わず同じ表記。</summary>
        public static string MaintenanceAll { get { return Loc.T("Common_FilterAll"); } }
        public const string MaintenanceOn = "ON";
        public const string MaintenanceOff = "OFF";

        private readonly WorkerConnectionService _service;
        private readonly SiteConnection _site;
        private readonly AuthMode _authMode;
        private readonly ICredentialPrompt _prompt;

        /// <summary>DDCから取得した全件。表示用の <see cref="Machines"/> はここから絞り込む。</summary>
        private readonly List<BrokerMachine> _allMachines = new List<BrokerMachine>();

        private CredentialInput _credential;   // 別資格情報サイトでウィンドウ中だけ保持
        private bool _isBusy;
        private string _statusMessage;
        private string _deliveryGroupFilter = FilterAll;
        private string _machineNameFilter;
        private string _sessionTypeFilter = SessionTypeAll;
        private string _catalogFilter = FilterAll;
        private string _maintenanceFilter = MaintenanceAll;
        private BrokerMachine _selectedMachine;
        private BrokerOperationResult _lastOperationResult;

        public MachinesViewModel(WorkerConnectionService service, SiteConnection site,
            AuthMode authMode, ICredentialPrompt prompt)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _site = site ?? throw new ArgumentNullException(nameof(site));
            _authMode = authMode;
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));

            Machines = new ObservableCollection<BrokerMachine>();
            DeliveryGroups = new ObservableCollection<string> { FilterAll };
            Catalogs = new ObservableCollection<string> { FilterAll };
            SessionTypes = new ObservableCollection<string> { SessionTypeAll, SessionTypeSingle, SessionTypeMulti };
            MaintenanceStates = new ObservableCollection<string> { MaintenanceAll, MaintenanceOn, MaintenanceOff };
            RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsBusy);

            _statusMessage = _authMode == AuthMode.PromptForCredential
                ? Loc.T("Machines_HintCredential")
                : Loc.T("Machines_HintReady");

            Loc.LanguageChanged += OnLanguageChanged;
        }

        /// <summary>
        /// 表示言語が変わったときに、文言を持つ表示物を作り直す。
        ///
        /// 絞り込みの選択肢は文言そのものが値なので、作り直すと選択が外れる。
        /// 一覧の並び順は言語で変わらないため、**選択位置を覚えて位置で戻す**。
        /// </summary>
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            var groupIndex = DeliveryGroups.IndexOf(_deliveryGroupFilter);
            var catalogIndex = Catalogs.IndexOf(_catalogFilter);
            var typeIndex = SessionTypes.IndexOf(_sessionTypeFilter);
            var maintIndex = MaintenanceStates.IndexOf(_maintenanceFilter);

            ReplaceItems(SessionTypes, new[] { SessionTypeAll, SessionTypeSingle, SessionTypeMulti });
            ReplaceItems(MaintenanceStates, new[] { MaintenanceAll, MaintenanceOn, MaintenanceOff });
            RebuildDeliveryGroups();
            RebuildCatalogs();

            RestoreSelection(DeliveryGroups, groupIndex, ref _deliveryGroupFilter, "DeliveryGroupFilter");
            RestoreSelection(Catalogs, catalogIndex, ref _catalogFilter, "CatalogFilter");
            RestoreSelection(SessionTypes, typeIndex, ref _sessionTypeFilter, "SessionTypeFilter");
            RestoreSelection(MaintenanceStates, maintIndex, ref _maintenanceFilter, "MaintenanceFilter");

            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(FilterSummary));

            ApplyFilter();
        }

        private static void ReplaceItems(ObservableCollection<string> target, IEnumerable<string> items)
        {
            target.Clear();
            foreach (var item in items) target.Add(item);
        }

        private void RestoreSelection(
            ObservableCollection<string> items, int index, ref string field, string propertyName)
        {
            field = index >= 0 && index < items.Count ? items[index] : items.FirstOrDefault();
            RaisePropertyChanged(propertyName);
        }

        /// <summary>画面に表示するマシン（絞り込み後）。</summary>
        public ObservableCollection<BrokerMachine> Machines { get; private set; }

        /// <summary>
        /// デリバリーグループ絞り込みの選択肢。取得結果から実際に存在する名前を集めて作る。
        /// 先頭は「(すべて)」、未所属マシンがあれば「(未所属)」も入る。
        /// </summary>
        public ObservableCollection<string> DeliveryGroups { get; private set; }

        /// <summary>
        /// カタログ絞り込みの選択肢。デリバリーグループと同型で、取得結果から実在する名前を集める。
        /// </summary>
        public ObservableCollection<string> Catalogs { get; private set; }

        /// <summary>セッション種別の絞り込み選択肢（固定）。</summary>
        public ObservableCollection<string> SessionTypes { get; private set; }

        /// <summary>メンテナンスモードの絞り込み選択肢（固定）。</summary>
        public ObservableCollection<string> MaintenanceStates { get; private set; }

        public string Title
        {
            get { return Loc.F("Machines_Title", _site.Label); }
        }

        public RelayCommand RefreshCommand { get; private set; }

        public BrokerMachine SelectedMachine
        {
            get { return _selectedMachine; }
            set { SetProperty(ref _selectedMachine, value); }
        }

        /// <summary>選択中のデリバリーグループ。変更すると即座に表示へ反映する。</summary>
        public string DeliveryGroupFilter
        {
            get { return _deliveryGroupFilter; }
            set { if (SetProperty(ref _deliveryGroupFilter, value)) ApplyFilter(); }
        }

        /// <summary>マシン名の部分一致検索（大文字小文字を区別しない）。</summary>
        public string MachineNameFilter
        {
            get { return _machineNameFilter; }
            set { if (SetProperty(ref _machineNameFilter, value)) ApplyFilter(); }
        }

        /// <summary>セッション種別（シングル／マルチ）の絞り込み。</summary>
        public string SessionTypeFilter
        {
            get { return _sessionTypeFilter; }
            set { if (SetProperty(ref _sessionTypeFilter, value)) ApplyFilter(); }
        }

        /// <summary>選択中のカタログ。変更すると即座に表示へ反映する。</summary>
        public string CatalogFilter
        {
            get { return _catalogFilter; }
            set { if (SetProperty(ref _catalogFilter, value)) ApplyFilter(); }
        }

        /// <summary>メンテナンスモード（ON／OFF）の絞り込み。</summary>
        public string MaintenanceFilter
        {
            get { return _maintenanceFilter; }
            set { if (SetProperty(ref _maintenanceFilter, value)) ApplyFilter(); }
        }

        /// <summary>
        /// 直近の書き込み操作の結果。対象ごとの内訳を「内訳」ボタンから表示するために保持する。
        /// 一覧取得（読み取り）では設定しない。
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
        // マシン一覧の取得
        // ------------------------------------------------------------------

        /// <summary>ユーザーが「更新」を押したときのマシン一覧取得。結果はログにも残す。</summary>
        public async Task RefreshAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                var result = await LoadMachinesCoreAsync();
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

        /// <summary>
        /// マシン一覧を取得して画面へ反映する中核処理。
        /// IsBusyのガードを持たないので、メンテナンス切替後の自動更新からも呼べる
        /// （切替処理はIsBusy=trueのまま進むため、ガード付きのRefreshAsyncからは呼べない）。
        /// </summary>
        private async Task<BrokerOperationResult> LoadMachinesCoreAsync()
        {
            if (!EnsureCredential()) return null;

            StatusMessage = Loc.T("Machines_Loading");
            var result = await Task.Run(() => QueryMachines());
            ApplyResult(result, Loc.T("Machines_ListName"));
            return result;
        }

        /// <summary>
        /// マシンを全件取得する。
        ///
        /// 絞り込みはDDC側に渡さず、取得後にクライアント側で行う（<see cref="ApplyFilter"/>）。
        /// DDC側の -DesktopGroupName は完全一致で、部分一致も「未所属」の指定もできず、
        /// 実用にならなかったため（本番検証のフィードバック）。
        /// 件数上限も設けない（0 = 無制限）。
        /// </summary>
        private BrokerOperationResult QueryMachines()
        {
            if (_authMode == AuthMode.PromptForCredential)
                return _service.ListMachinesWithCredential(
                    _site, null, 0,
                    _credential.Domain, _credential.UserName, _credential.Password);

            return _service.ListMachinesIntegrated(_site, null, 0);
        }

        // ------------------------------------------------------------------
        // メンテナンスモード切替（呼び出し側で確認を取ってから呼ぶこと）
        // ------------------------------------------------------------------

        /// <summary>
        /// 選択された全マシンのメンテナンスモードをまとめて切り替える。
        /// 呼び出し側（View）で件数を明示した確認を取ってから呼ぶこと。
        ///
        /// 途中で失敗しても止めず、対象ごとの成否を集計して返す（Core側の一括操作に委譲）。
        /// どのマシンが失敗したかは <see cref="LastOperationResult"/> の内訳で確認できる。
        /// </summary>
        public async Task SetMaintenanceAsync(IList<BrokerMachine> machines, bool enabled)
        {
            if (IsBusy) return;

            var names = (machines ?? new List<BrokerMachine>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.MachineName))
                .Select(m => m.MachineName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
            {
                StatusMessage = Loc.T("Machines_SelectTarget");
                return;
            }

            if (!EnsureCredential()) return;

            IsBusy = true;
            LastOperationResult = null;
            StatusMessage = names.Count == 1
                ? Loc.F("Machines_SettingOne", names[0], enabled ? "ON" : "OFF")
                : Loc.F("Machines_SettingMany", names.Count, enabled ? "ON" : "OFF");
            try
            {
                var result = await Task.Run(() => SetMaintenance(names, enabled));

                // 書き込み操作は成否にかかわらず監査記録としてログに残す。
                var logFile = WriteOperationLog(result);
                LastOperationResult = result;

                if (result.Success)
                {
                    // 反映後の状態を一覧へ取り込む（IsBusyのままなので中核処理を直接呼ぶ）。
                    await LoadMachinesCoreAsync();

                    var note = result.Message
                        + (result.WorkingDdc != null ? Loc.F("Common_DdcNote", result.WorkingDdc) : string.Empty);

                    // 一部失敗しているなら、内訳を見るよう促す。
                    if (result.FailureCount > 0)
                        note += Loc.T("Machines_HintFailures");

                    StatusMessage = WithLogNote(note, logFile);
                }
                else
                {
                    ApplyResult(result, Loc.T("Machines_OpMaintenance"));
                    StatusMessage = WithLogNote(StatusMessage, logFile);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = Loc.F("Machines_ToggleError", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private BrokerOperationResult SetMaintenance(IList<string> machineNames, bool enabled)
        {
            if (_authMode == AuthMode.PromptForCredential)
                return _service.SetMaintenanceBulkWithCredential(
                    _site, machineNames, enabled,
                    _credential.Domain, _credential.UserName, _credential.Password);

            return _service.SetMaintenanceBulkIntegrated(_site, machineNames, enabled);
        }

        // ------------------------------------------------------------------
        // 電源操作（破壊的。呼び出し側で確認を取ってから呼ぶこと）
        // ------------------------------------------------------------------

        /// <summary>
        /// 選択されたマシンに電源操作を要求する。
        ///
        /// セッション一覧の同名メソッドと違い、対象は最初からマシンなので変換は要らない。
        /// Core 側（PowerActionBulk）はマシン名のリストを受け取る作りなので、そのまま渡せる。
        ///
        /// 呼び出し側（View）は <see cref="CountSessionsOn"/> で影響を受けるセッション数を
        /// 示したうえで確認を取ること。
        /// </summary>
        public async Task PowerActionAsync(IList<BrokerMachine> machines, BrokerPowerAction action)
        {
            if (IsBusy) return;

            var names = ResolveMachineNames(machines);
            if (names.Count == 0)
            {
                StatusMessage = Loc.T("Machines_SelectTarget");
                return;
            }

            if (!EnsureCredential()) return;

            IsBusy = true;
            LastOperationResult = null;
            StatusMessage = names.Count == 1
                ? Loc.F("Power_RequestingOne", names[0], action)
                : Loc.F("Power_RequestingMany", names.Count, action);
            try
            {
                var result = await Task.Run(() => RunPowerAction(names, action));

                // 破壊的操作は成否にかかわらず監査記録として必ずログに残す。
                var logFile = WriteOperationLog(result);
                LastOperationResult = result;

                if (result.Success)
                {
                    // 電源操作は非同期に進むため、直後に取り直しても電源状態は変わっていない。
                    // それでも取り直すのは、セッション数など他の値を最新にするため。
                    await LoadMachinesCoreAsync();

                    var note = result.Message
                        + (result.WorkingDdc != null ? Loc.F("Common_DdcNote", result.WorkingDdc) : string.Empty);

                    if (result.FailureCount > 0)
                        note += Loc.T("Power_HintFailures");

                    StatusMessage = WithLogNote(note, logFile);
                }
                else
                {
                    ApplyResult(result, Loc.T("Power_OpLabel"));
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

        /// <summary>選択されたマシンから、対象のマシン名を重複なく取り出す。</summary>
        public IList<string> ResolveMachineNames(IList<BrokerMachine> machines)
        {
            return (machines ?? new List<BrokerMachine>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.MachineName))
                .Select(m => m.MachineName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 指定マシン上で動作しているセッション数の合計。確認ダイアログで影響範囲を示すために使う。
        ///
        /// セッション一覧と違いセッションの一覧を持っていないため、取得済みの
        /// <see cref="BrokerMachine.SessionCount"/> を合計する。
        /// これは前回の「更新」時点のスナップショットであり、その後に増えた分は含まれない
        /// （確認ダイアログでもその旨に触れる）。
        /// </summary>
        public int CountSessionsOn(IList<BrokerMachine> machines)
        {
            if (machines == null) return 0;

            var total = 0;
            foreach (var m in machines)
            {
                if (m == null) continue;
                total += m.SessionCount;
            }
            return total;
        }

        // ------------------------------------------------------------------
        // エクスポート
        // ------------------------------------------------------------------

        /// <summary>
        /// 書き出す対象を決める。選択があれば選択行、無ければ表示中（絞り込み後）の全行。
        /// エクスプローラやExcelと同じ感覚にし、出力範囲を選ぶUIを増やさずに済ませる。
        /// </summary>
        public IList<BrokerMachine> ResolveExportRows(IList<BrokerMachine> selected)
        {
            if (selected != null && selected.Count > 0)
                return selected.Where(m => m != null).ToList();

            return Machines.ToList();
        }

        /// <summary>CSV／TSVに出す列。画面の列に加え、調査で要るDNS名も含める。</summary>
        private static IList<ExportColumn<BrokerMachine>> BuildColumns()
        {
            return new List<ExportColumn<BrokerMachine>>
            {
                new ExportColumn<BrokerMachine>(Loc.T("Col_MachineName"), m => m.MachineName),
                new ExportColumn<BrokerMachine>(Loc.T("Col_DnsName"), m => m.DnsName),
                new ExportColumn<BrokerMachine>(Loc.T("Col_Catalog"), m => m.CatalogName),
                new ExportColumn<BrokerMachine>(Loc.T("Col_DeliveryGroup"), m => m.DeliveryGroupName),
                new ExportColumn<BrokerMachine>(Loc.T("Col_Type"), m => SessionSupportConverter.ToLabel(m.SessionSupport)),
                new ExportColumn<BrokerMachine>(Loc.T("Col_AssignedUsers"), m => m.AssociatedUserNames),
                new ExportColumn<BrokerMachine>(Loc.T("Col_Registration"), m => m.RegistrationState),
                new ExportColumn<BrokerMachine>(Loc.T("Col_Power"), m => m.PowerState),
                new ExportColumn<BrokerMachine>(Loc.T("Col_State"), m => m.SummaryState),
                new ExportColumn<BrokerMachine>(Loc.T("Col_SessionCount"), m => m.SessionCount.ToString()),
                // True/False は Excel で真偽値に変換され表記が揺れるため、画面と同じ ON/OFF にする。
                new ExportColumn<BrokerMachine>(Loc.T("Col_Maintenance"), m => m.MaintenanceLabel)
            };
        }

        /// <summary>既定のファイル名（保存ダイアログの初期値）。</summary>
        public string BuildExportFileName()
        {
            return TableExporter.BuildFileName("machines", _site.Id);
        }

        /// <summary>CSVとして保存する。保存先は呼び出し側がダイアログで決めてから渡す。</summary>
        public void ExportCsv(string path, IList<BrokerMachine> rows)
        {
            var csv = TableExporter.ToCsv(rows, BuildColumns());

            string error;
            if (TableExporter.TryWriteCsvFile(path, csv, out error))
            {
                StatusMessage = Loc.F("Machines_CsvSaved", rows.Count, path);
                WriteExportLog(Loc.T("Export_ActionCsv"), rows.Count, path);
            }
            else
            {
                StatusMessage = Loc.F("Export_CsvFailed", error);
            }
        }

        /// <summary>表をタブ区切りでクリップボードへ入れる（Excelへそのまま貼れる形）。</summary>
        public void CopyToClipboard(IList<BrokerMachine> rows)
        {
            var tsv = TableExporter.ToTsv(rows, BuildColumns());

            string error;
            if (TableExporter.TrySetClipboard(tsv, out error))
            {
                // クリップボードは中身が見えないので、件数を必ず出して成功を伝える。
                StatusMessage = Loc.F("Machines_Copied", rows.Count);
                WriteExportLog(Loc.T("Export_ActionClipboard"), rows.Count, null);
            }
            else
            {
                StatusMessage = Loc.F("Export_ClipboardFailed", error);
            }
        }

        /// <summary>
        /// 持ち出しの記録をログに残す（誰がいつ何件出したか）。
        /// 一覧の中身そのものは書かない（ログが肥大化するため）。
        /// </summary>
        private void WriteExportLog(string action, int count, string path)
        {
            var content = ConnectionReportFormatter.FormatExportReport(
                _site.Label, Loc.T("Machines_ListName"), action, count, path);

            string failureReason;
            RunLogWriter.Write(content, out failureReason);
        }

        // ------------------------------------------------------------------

        private void ApplyResult(BrokerOperationResult result, string label)
        {
            if (result.Success)
            {
                _allMachines.Clear();
                _allMachines.AddRange(result.Machines);

                RebuildDeliveryGroups();
                RebuildCatalogs();
                ApplyFilter();

                StatusMessage = (result.Message ?? label)
                    + (result.WorkingDdc != null ? Loc.F("Common_DdcNote", result.WorkingDdc) : string.Empty);
            }
            else if (result.SdkUnavailable)
            {
                StatusMessage = Loc.F("Err_SdkUnavailable", label);
            }
            else
            {
                StatusMessage = Loc.F("Err_OpFailed", label, result.ErrorMessage ?? Loc.T("Common_UnknownError"));
            }
        }

        /// <summary>
        /// 取得結果から、デリバリーグループ絞り込みの選択肢を作り直す。
        /// 未所属のマシンが1台でもあれば「(未所属)」も選べるようにする。
        /// 現在の選択が新しい一覧に無ければ「(すべて)」へ戻す。
        /// </summary>
        private void RebuildDeliveryGroups()
        {
            var current = _deliveryGroupFilter;

            var names = _allMachines
                .Select(m => m.DeliveryGroupName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            DeliveryGroups.Clear();
            DeliveryGroups.Add(FilterAll);
            if (_allMachines.Any(m => string.IsNullOrWhiteSpace(m.DeliveryGroupName)))
                DeliveryGroups.Add(FilterNoGroup);
            foreach (var n in names) DeliveryGroups.Add(n);

            // 選択の維持（無くなっていたら「すべて」に戻す）。ApplyFilterは呼び出し側で行う。
            _deliveryGroupFilter = DeliveryGroups.Contains(current) ? current : FilterAll;
            RaisePropertyChanged(nameof(DeliveryGroupFilter));
        }

        /// <summary>
        /// 取得結果から、カタログ絞り込みの選択肢を作り直す。
        /// デリバリーグループと同型（実在する名前だけを並べ、未所属があれば「(未所属)」も出す）。
        /// </summary>
        private void RebuildCatalogs()
        {
            var current = _catalogFilter;

            var names = _allMachines
                .Select(m => m.CatalogName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Catalogs.Clear();
            Catalogs.Add(FilterAll);
            if (_allMachines.Any(m => string.IsNullOrWhiteSpace(m.CatalogName)))
                Catalogs.Add(FilterNoGroup);
            foreach (var n in names) Catalogs.Add(n);

            _catalogFilter = Catalogs.Contains(current) ? current : FilterAll;
            RaisePropertyChanged(nameof(CatalogFilter));
        }

        /// <summary>
        /// 全件（<see cref="_allMachines"/>）から、現在の絞り込み条件で表示用一覧を作る。
        /// デリバリーグループは完全一致（「(未所属)」は空欄のもの）、マシン名は部分一致。
        /// </summary>
        private void ApplyFilter()
        {
            var group = _deliveryGroupFilter;
            var keyword = (_machineNameFilter ?? string.Empty).Trim();

            IEnumerable<BrokerMachine> query = _allMachines;

            if (group == FilterNoGroup)
                query = query.Where(m => string.IsNullOrWhiteSpace(m.DeliveryGroupName));
            else if (!string.IsNullOrEmpty(group) && group != FilterAll)
                query = query.Where(m => string.Equals(m.DeliveryGroupName, group, StringComparison.OrdinalIgnoreCase));

            // カタログ。デリバリーグループと同じ規則（完全一致 ／「(未所属)」は空欄）。
            var catalog = _catalogFilter;
            if (catalog == FilterNoGroup)
                query = query.Where(m => string.IsNullOrWhiteSpace(m.CatalogName));
            else if (!string.IsNullOrEmpty(catalog) && catalog != FilterAll)
                query = query.Where(m => string.Equals(m.CatalogName, catalog, StringComparison.OrdinalIgnoreCase));

            // 検索欄はマシン名・DNS名に加えて割り当てユーザーも対象にする。
            // 「このユーザーの専有マシンはどれか」を1つの欄で引けるようにするため。
            if (keyword.Length > 0)
                query = query.Where(m =>
                    IndexOfIgnoreCase(m.MachineName, keyword) >= 0 ||
                    IndexOfIgnoreCase(m.DnsName, keyword) >= 0 ||
                    IndexOfIgnoreCase(m.AssociatedUserNames, keyword) >= 0);

            // セッション種別。SessionSupport は "SingleSession" / "MultiSession"。
            if (_sessionTypeFilter == SessionTypeSingle)
                query = query.Where(m => IsSessionSupport(m, "SingleSession"));
            else if (_sessionTypeFilter == SessionTypeMulti)
                query = query.Where(m => IsSessionSupport(m, "MultiSession"));

            // メンテナンスモード。
            if (_maintenanceFilter == MaintenanceOn)
                query = query.Where(m => m.InMaintenanceMode);
            else if (_maintenanceFilter == MaintenanceOff)
                query = query.Where(m => !m.InMaintenanceMode);

            var selected = SelectedMachine == null ? null : SelectedMachine.MachineName;

            Machines.Clear();
            foreach (var m in query) Machines.Add(m);

            // 絞り込み後も同じマシンが残っていれば選択を維持する。
            if (selected != null)
                SelectedMachine = Machines.FirstOrDefault(
                    m => string.Equals(m.MachineName, selected, StringComparison.OrdinalIgnoreCase));

            RaisePropertyChanged(nameof(FilterSummary));
        }

        private static int IndexOfIgnoreCase(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack)) return -1;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSessionSupport(BrokerMachine m, string expected)
        {
            return string.Equals(m.SessionSupport, expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>「表示 N 台 / 全 M 台」の表示。絞り込みの効き具合が一目で分かるように。</summary>
        public string FilterSummary
        {
            get
            {
                if (_allMachines.Count == 0) return string.Empty;
                return Machines.Count == _allMachines.Count
                    ? Loc.F("Machines_SummaryAll", _allMachines.Count)
                    : Loc.F("Machines_SummaryFiltered", Machines.Count, _allMachines.Count);
            }
        }

        /// <summary>
        /// 操作結果をログファイルに残し、ログのファイル名を返す（失敗時はnull）。
        /// 特にメンテナンス切替（書き込み）の監査記録として重要。接続テストと同じ方針で、
        /// 検証・障害調査の情報を開発環境へ持ち帰れるようにする。
        /// </summary>
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

        /// <summary>
        /// 別資格情報サイトなら、まだ入力していなければ1回だけ資格情報を求める。
        /// キャンセルされたら false。統合認証なら常に true。
        /// </summary>
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
