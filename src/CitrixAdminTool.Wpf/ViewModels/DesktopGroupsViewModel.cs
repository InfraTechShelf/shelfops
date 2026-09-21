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
    /// 1サイトのデリバリーグループ一覧を担うViewModel（フェーズ5・0.8）。
    ///
    /// この段階は読み取りのみ。0.9 以降で「設定を保存」「保存ファイルと比較」「復元」を
    /// この画面から起動する予定なので、操作行とフィルタ行の2段構成にしておく。
    ///
    /// マシン一覧・セッション一覧と同じ作法: ワーカー経由で取得し、絞り込みはクライアント側、
    /// 別資格情報サイトではウィンドウ中だけ資格情報を保持する。
    /// </summary>
    public class DesktopGroupsViewModel : ViewModelBase, IDisposable
    {
        public static string FilterAll { get { return Loc.T("Common_FilterAll"); } }

        /// <summary>種別・配信形態の値は SDK の英語表記をそのまま選択肢にする（訳すと突き合わせにくい）。</summary>
        public const string KindPrivate = "Private";
        public const string KindShared = "Shared";
        public const string DeliveryDesktopsOnly = "DesktopsOnly";
        public const string DeliveryAppsOnly = "AppsOnly";
        public const string DeliveryDesktopsAndApps = "DesktopsAndApps";

        public static string EnabledOnly { get { return Loc.T("Dg_FilterEnabled"); } }
        public static string DisabledOnly { get { return Loc.T("Dg_FilterDisabled"); } }

        private readonly WorkerConnectionService _service;
        private readonly SiteConnection _site;
        private readonly AuthMode _authMode;
        private readonly ICredentialPrompt _prompt;

        private readonly List<BrokerDesktopGroup> _all = new List<BrokerDesktopGroup>();

        private CredentialInput _credential;
        private bool _isBusy;
        private string _statusMessage;
        private string _kindFilter;
        private string _deliveryFilter;
        private string _enabledFilter;
        private string _nameFilter;
        private BrokerDesktopGroup _selected;

        public DesktopGroupsViewModel(WorkerConnectionService service, SiteConnection site,
            AuthMode authMode, ICredentialPrompt prompt)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _site = site ?? throw new ArgumentNullException(nameof(site));
            _authMode = authMode;
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));

            Groups = new ObservableCollection<BrokerDesktopGroup>();
            Kinds = new ObservableCollection<string> { FilterAll, KindPrivate, KindShared };
            DeliveryTypes = new ObservableCollection<string>
                { FilterAll, DeliveryDesktopsOnly, DeliveryAppsOnly, DeliveryDesktopsAndApps };
            EnabledStates = new ObservableCollection<string> { FilterAll, EnabledOnly, DisabledOnly };

            _kindFilter = FilterAll;
            _deliveryFilter = FilterAll;
            _enabledFilter = FilterAll;

            RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsBusy);

            _statusMessage = _authMode == AuthMode.PromptForCredential
                ? Loc.T("Dg_HintCredential")
                : Loc.T("Dg_HintReady");

            Loc.LanguageChanged += OnLanguageChanged;
        }

        /// <summary>表示言語が変わったら、文言を値に持つ選択肢を位置で復元する。</summary>
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            var kindIndex = Kinds.IndexOf(_kindFilter);
            var deliveryIndex = DeliveryTypes.IndexOf(_deliveryFilter);
            var enabledIndex = EnabledStates.IndexOf(_enabledFilter);

            Replace(Kinds, new[] { FilterAll, KindPrivate, KindShared });
            Replace(DeliveryTypes, new[] { FilterAll, DeliveryDesktopsOnly, DeliveryAppsOnly, DeliveryDesktopsAndApps });
            Replace(EnabledStates, new[] { FilterAll, EnabledOnly, DisabledOnly });

            _kindFilter = Pick(Kinds, kindIndex);
            _deliveryFilter = Pick(DeliveryTypes, deliveryIndex);
            _enabledFilter = Pick(EnabledStates, enabledIndex);
            RaisePropertyChanged(nameof(KindFilter));
            RaisePropertyChanged(nameof(DeliveryFilter));
            RaisePropertyChanged(nameof(EnabledFilter));
            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(FilterSummary));

            ApplyFilter();
        }

        private static void Replace(ObservableCollection<string> target, IEnumerable<string> items)
        {
            target.Clear();
            foreach (var i in items) target.Add(i);
        }

        private static string Pick(ObservableCollection<string> items, int index)
        {
            return index >= 0 && index < items.Count ? items[index] : items.FirstOrDefault();
        }

        public ObservableCollection<BrokerDesktopGroup> Groups { get; private set; }
        public ObservableCollection<string> Kinds { get; private set; }
        public ObservableCollection<string> DeliveryTypes { get; private set; }
        public ObservableCollection<string> EnabledStates { get; private set; }

        public string Title { get { return Loc.F("Dg_Title", _site.Label); } }

        public RelayCommand RefreshCommand { get; private set; }

        public BrokerDesktopGroup SelectedGroup
        {
            get { return _selected; }
            set { SetProperty(ref _selected, value); }
        }

        public string KindFilter
        {
            get { return _kindFilter; }
            set { if (SetProperty(ref _kindFilter, value)) ApplyFilter(); }
        }

        public string DeliveryFilter
        {
            get { return _deliveryFilter; }
            set { if (SetProperty(ref _deliveryFilter, value)) ApplyFilter(); }
        }

        public string EnabledFilter
        {
            get { return _enabledFilter; }
            set { if (SetProperty(ref _enabledFilter, value)) ApplyFilter(); }
        }

        /// <summary>名前・表示名・説明の部分一致（大文字小文字を区別しない）。</summary>
        public string NameFilter
        {
            get { return _nameFilter; }
            set { if (SetProperty(ref _nameFilter, value)) ApplyFilter(); }
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

        public async Task RefreshAsync()
        {
            if (IsBusy) return;
            if (!EnsureCredential()) return;

            IsBusy = true;
            StatusMessage = Loc.T("Dg_Loading");
            try
            {
                var result = await Task.Run(() => Query());
                var logFile = WriteOperationLog(result);

                if (result.Success)
                {
                    _all.Clear();
                    _all.AddRange(result.DesktopGroups);
                    ApplyFilter();

                    StatusMessage = WithLogNote(
                        Loc.F("Dg_Loaded", _all.Count)
                        + (result.WorkingDdc != null ? Loc.F("Common_DdcNote", result.WorkingDdc) : string.Empty),
                        logFile);
                }
                else if (result.SdkUnavailable)
                {
                    StatusMessage = WithLogNote(Loc.F("Err_SdkUnavailable", Loc.T("Dg_ListName")), logFile);
                }
                else
                {
                    StatusMessage = WithLogNote(
                        Loc.F("Err_OpFailed", Loc.T("Dg_ListName"), result.ErrorMessage ?? Loc.T("Common_UnknownError")),
                        logFile);
                }
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

        private BrokerOperationResult Query()
        {
            if (_authMode == AuthMode.PromptForCredential)
                return _service.ListDesktopGroupsWithCredential(
                    _site, _credential.Domain, _credential.UserName, _credential.Password);

            return _service.ListDesktopGroupsIntegrated(_site);
        }

        private void ApplyFilter()
        {
            var keyword = (_nameFilter ?? string.Empty).Trim();

            IEnumerable<BrokerDesktopGroup> query = _all;

            if (_kindFilter != FilterAll && !string.IsNullOrEmpty(_kindFilter))
                query = query.Where(g => string.Equals(g.DesktopKind, _kindFilter, StringComparison.OrdinalIgnoreCase));

            if (_deliveryFilter != FilterAll && !string.IsNullOrEmpty(_deliveryFilter))
                query = query.Where(g => string.Equals(g.DeliveryType, _deliveryFilter, StringComparison.OrdinalIgnoreCase));

            if (_enabledFilter == EnabledOnly) query = query.Where(g => g.Enabled);
            else if (_enabledFilter == DisabledOnly) query = query.Where(g => !g.Enabled);

            if (keyword.Length > 0)
                query = query.Where(g =>
                    Contains(g.Name, keyword) || Contains(g.PublishedName, keyword) || Contains(g.Description, keyword));

            var selectedUid = SelectedGroup == null ? (int?)null : SelectedGroup.Uid;

            Groups.Clear();
            foreach (var g in query.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)) Groups.Add(g);

            if (selectedUid.HasValue)
                SelectedGroup = Groups.FirstOrDefault(g => g.Uid == selectedUid.Value);

            RaisePropertyChanged(nameof(FilterSummary));
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public string FilterSummary
        {
            get
            {
                if (_all.Count == 0) return string.Empty;
                return Groups.Count == _all.Count
                    ? Loc.F("Dg_SummaryAll", _all.Count)
                    : Loc.F("Dg_SummaryFiltered", Groups.Count, _all.Count);
            }
        }

        // ------------------------------------------------------------------
        // エクスポート（マシン一覧と同じ規則）
        // ------------------------------------------------------------------

        public IList<BrokerDesktopGroup> ResolveExportRows(IList<BrokerDesktopGroup> selected)
        {
            if (selected != null && selected.Count > 0)
                return selected.Where(g => g != null).ToList();
            return Groups.ToList();
        }

        private static IList<ExportColumn<BrokerDesktopGroup>> BuildColumns()
        {
            return new List<ExportColumn<BrokerDesktopGroup>>
            {
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Dg_ColName"), g => g.Name),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Dg_ColPublishedName"), g => g.PublishedName),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Dg_ColEnabled"), g => g.Enabled ? "Yes" : "No"),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Col_Maintenance"), g => g.MaintenanceLabel),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Dg_ColKind"), g => g.DesktopKind),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Dg_ColDelivery"), g => g.DeliveryType),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Col_Type"), g => SessionSupportConverter.ToLabel(g.SessionSupport)),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Dg_ColTotal"), g => g.TotalDesktops.ToString()),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Dg_ColAvailable"), g => g.DesktopsAvailable.ToString()),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Dg_ColInUse"), g => g.DesktopsInUse.ToString()),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Dg_ColUnregistered"), g => g.DesktopsUnregistered.ToString()),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Col_SessionCount"), g => g.Sessions.ToString()),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Dg_ColDescription"), g => g.Description),
                new ExportColumn<BrokerDesktopGroup>(Loc.T("Col_Uid"), g => g.Uid.ToString())
            };
        }

        public string BuildExportFileName()
        {
            return TableExporter.BuildFileName("deliverygroups", _site.Id);
        }

        public void ExportCsv(string path, IList<BrokerDesktopGroup> rows)
        {
            string error;
            if (TableExporter.TryWriteCsvFile(path, TableExporter.ToCsv(rows, BuildColumns()), out error))
            {
                StatusMessage = Loc.F("Dg_CsvSaved", rows.Count, path);
                WriteExportLog(Loc.T("Export_ActionCsv"), rows.Count, path);
            }
            else
            {
                StatusMessage = Loc.F("Export_CsvFailed", error);
            }
        }

        public void CopyToClipboard(IList<BrokerDesktopGroup> rows)
        {
            string error;
            if (TableExporter.TrySetClipboard(TableExporter.ToTsv(rows, BuildColumns()), out error))
            {
                StatusMessage = Loc.F("Dg_Copied", rows.Count);
                WriteExportLog(Loc.T("Export_ActionClipboard"), rows.Count, null);
            }
            else
            {
                StatusMessage = Loc.F("Export_ClipboardFailed", error);
            }
        }

        private void WriteExportLog(string action, int count, string path)
        {
            string failureReason;
            RunLogWriter.Write(
                ConnectionReportFormatter.FormatExportReport(_site.Label, Loc.T("Dg_ListName"), action, count, path),
                out failureReason);
        }

        // ------------------------------------------------------------------

        private string WriteOperationLog(BrokerOperationResult result)
        {
            string failureReason;
            var path = RunLogWriter.Write(
                ConnectionReportFormatter.FormatOperationReport(_site.Label, result), out failureReason);
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
            Loc.LanguageChanged -= OnLanguageChanged;

            var cred = _credential;
            _credential = null;
            if (cred != null) cred.Dispose();
        }
    }
}
