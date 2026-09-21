using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CitrixAdminTool.Core.Configuration;
using CitrixAdminTool.Core.Connection;
using CitrixAdminTool.Core.Models;
using CitrixAdminTool.Wpf.Mvvm;
using CitrixAdminTool.Wpf.Services.Localization;
using CitrixAdminTool.Wpf.Services;

namespace CitrixAdminTool.Wpf.ViewModels
{
    /// <summary>
    /// メイン画面のViewModel。
    ///
    /// フェーズ2の範囲は仕様書§13の第1項目、すなわち
    /// 「サイト一覧・追加/編集・接続テストボタン・選択中サイト保持」に、
    /// 別資格情報モードを加えたもの。Brokerの管理操作は後続で
    /// このシェルの上にモジュール単位で足していく。
    /// </summary>
    /// <summary>認証モードの選択肢（表示名と値の組）。</summary>
    public class AuthModeOption
    {
        public AuthModeOption(AuthMode value, string display)
        {
            Value = value;
            Display = display;
        }

        public AuthMode Value { get; private set; }
        public string Display { get; private set; }
    }

    public class MainViewModel : ViewModelBase
    {
        // 接続はワーカープロセスへ委譲する（別資格情報モードの接続再利用問題への根本対策。
        // 詳細は docs/SPEC_phase3.md）。SDKをこのGUIプロセスで直接呼ぶことはしない。
        private readonly WorkerConnectionService _service = new WorkerConnectionService();
        private readonly ICredentialPrompt _credentialPrompt;

        private SiteViewModel _selectedSite;
        private string _resultText;
        private string _statusMessage;
        private bool _isBusy;
        private bool _isDirty;

        public MainViewModel(ICredentialPrompt credentialPrompt)
        {
            if (credentialPrompt == null) throw new ArgumentNullException(nameof(credentialPrompt));
            _credentialPrompt = credentialPrompt;

            Sites = new ObservableCollection<SiteViewModel>();
            Sites.CollectionChanged += OnSitesCollectionChanged;

            AddSiteCommand = new RelayCommand(AddSite, () => !IsBusy);
            DeleteSiteCommand = new RelayCommand(DeleteSite, () => !IsBusy && SelectedSite != null);
            CopySiteCommand = new RelayCommand(CopySite, () => !IsBusy && SelectedSite != null);
            SaveCommand = new RelayCommand(Save, () => !IsBusy);
            TestSelectedCommand = new RelayCommand(async () => await TestSelectedAsync(),
                () => !IsBusy && SelectedSite != null);
            TestAllCommand = new RelayCommand(async () => await TestAllAsync(),
                () => !IsBusy && Sites.Count > 0);
            OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
            OpenConfigFolderCommand = new RelayCommand(OpenConfigFolder);

            Load();
        }

        /// <summary>GUI終了時に呼ぶ。常駐ワーカーを停止する。</summary>
        public void Shutdown()
        {
            _service.Dispose();
        }

        /// <summary>
        /// 選択中サイトのマシン一覧用ViewModelを作る。選択が無い／入力不備なら null。
        /// ワーカー（常駐/使い捨て）は本VMと共有するため、同じ _service を渡す。
        /// </summary>
        public MachinesViewModel TryCreateMachinesViewModel()
        {
            if (SelectedSite == null)
            {
                StatusMessage = Loc.T("Main_SelectSite");
                return null;
            }

            var error = SelectedSite.Validate();
            if (error != null)
            {
                StatusMessage = Loc.F("Main_SiteError", SelectedSite.Label, error);
                return null;
            }

            return new MachinesViewModel(
                _service, SelectedSite.ToModel(), SelectedSite.AuthMode, _credentialPrompt);
        }

        /// <summary>選択中サイトのセッション一覧用ViewModelを作る。選択が無い／入力不備なら null。</summary>
        public SessionsViewModel TryCreateSessionsViewModel()
        {
            if (SelectedSite == null)
            {
                StatusMessage = Loc.T("Main_SelectSite");
                return null;
            }

            var error = SelectedSite.Validate();
            if (error != null)
            {
                StatusMessage = Loc.F("Main_SiteError", SelectedSite.Label, error);
                return null;
            }

            return new SessionsViewModel(
                _service, SelectedSite.ToModel(), SelectedSite.AuthMode, _credentialPrompt);
        }

        /// <summary>選択中サイトのデリバリーグループ一覧用ViewModelを作る。選択が無い／入力不備なら null。</summary>
        public DesktopGroupsViewModel TryCreateDesktopGroupsViewModel()
        {
            if (SelectedSite == null)
            {
                StatusMessage = Loc.T("Main_SelectSite");
                return null;
            }

            var error = SelectedSite.Validate();
            if (error != null)
            {
                StatusMessage = Loc.F("Main_SiteError", SelectedSite.Label, error);
                return null;
            }

            return new DesktopGroupsViewModel(
                _service, SelectedSite.ToModel(), SelectedSite.AuthMode, _credentialPrompt);
        }

        public ObservableCollection<SiteViewModel> Sites { get; private set; }

        /// <summary>
        /// 認証モードのコンボボックス用。enumをそのまま並べると英語の識別子が出るため、
        /// 表示名と値の組にしてバインドする（コンバータを増やさずに済む）。
        /// </summary>
        public IEnumerable<AuthModeOption> AuthModes
        {
            get
            {
                return new[]
                {
                    new AuthModeOption(AuthMode.IntegratedWindows, Loc.T("Auth_Integrated")),
                    new AuthModeOption(AuthMode.PromptForCredential, Loc.T("Auth_Prompt"))
                };
            }
        }

        public RelayCommand AddSiteCommand { get; private set; }
        public RelayCommand DeleteSiteCommand { get; private set; }
        public RelayCommand CopySiteCommand { get; private set; }
        public RelayCommand SaveCommand { get; private set; }
        public RelayCommand TestSelectedCommand { get; private set; }
        public RelayCommand TestAllCommand { get; private set; }
        public RelayCommand OpenLogFolderCommand { get; private set; }
        public RelayCommand OpenConfigFolderCommand { get; private set; }

        public SiteViewModel SelectedSite
        {
            get { return _selectedSite; }
            set
            {
                if (!SetProperty(ref _selectedSite, value)) return;

                // 選択中サイトを保持する（次回起動時に復元）。
                UiStateStore.SaveLastSelectedSiteId(value == null ? null : value.Id);
                RaiseCommandStates();
            }
        }

        public string ResultText
        {
            get { return _resultText; }
            private set { SetProperty(ref _resultText, value); }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { SetProperty(ref _statusMessage, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (!SetProperty(ref _isBusy, value)) return;
                RaisePropertyChanged(nameof(IsNotBusy));
                RaiseCommandStates();
            }
        }

        public bool IsNotBusy
        {
            get { return !_isBusy; }
        }

        /// <summary>未保存の変更があるか。</summary>
        public bool IsDirty
        {
            get { return _isDirty; }
            private set { if (SetProperty(ref _isDirty, value)) RaisePropertyChanged(nameof(WindowTitle)); }
        }

        public string WindowTitle
        {
            get { return Loc.T("Main_WindowTitle") + (_isDirty ? " *" : string.Empty); }
        }

        public string ConfigPath
        {
            get { return SiteConfigStore.GetDefaultRoamingPath(); }
        }

        // ------------------------------------------------------------------
        // 読み込み・保存
        // ------------------------------------------------------------------

        private void Load()
        {
            var roamingPath = SiteConfigStore.GetDefaultRoamingPath();

            // フェーズ1のコンソール版で作った sites.json が実行フォルダにある場合、
            // 手で移し替えさせるのは不親切なので取り込む（保存先はローミング側）。
            var legacyPath = SiteConfigStore.GetDefaultConsolePath();

            string sourcePath = null;
            if (File.Exists(roamingPath)) sourcePath = roamingPath;
            else if (File.Exists(legacyPath)) sourcePath = legacyPath;

            if (sourcePath == null)
            {
                StatusMessage = Loc.F("Main_NoConfig", roamingPath);
                return;
            }

            try
            {
                var sites = SiteConfigStore.Load(sourcePath);

                foreach (var site in sites)
                    Sites.Add(SiteViewModel.FromModel(site));

                RestoreSelection();

                if (sourcePath == legacyPath)
                {
                    // 取り込んだだけで保存はしていないことを明示する。
                    IsDirty = true;
                    StatusMessage = Loc.F("Main_LoadedLegacy", legacyPath, roamingPath);
                }
                else
                {
                    IsDirty = false;
                    StatusMessage = Loc.F("Main_LoadedSites", sites.Count, roamingPath);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = Loc.F("Main_LoadFailed", ex.Message);
            }
        }

        private void RestoreSelection()
        {
            var lastId = UiStateStore.LoadLastSelectedSiteId();

            SiteViewModel target = null;
            if (!string.IsNullOrWhiteSpace(lastId))
                target = Sites.FirstOrDefault(s => string.Equals(s.Id, lastId, StringComparison.OrdinalIgnoreCase));

            SelectedSite = target ?? Sites.FirstOrDefault();
        }

        private void Save()
        {
            var duplicate = Sites
                .GroupBy(s => (s.Id ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicate != null)
            {
                StatusMessage = Loc.F("Main_DuplicateId", duplicate.Key);
                return;
            }

            foreach (var site in Sites)
            {
                var error = site.Validate();
                if (error != null)
                {
                    SelectedSite = site;
                    StatusMessage = Loc.F("Main_SiteError", site.Label, error);
                    return;
                }
            }

            try
            {
                var path = SiteConfigStore.GetDefaultRoamingPath();
                SiteConfigStore.Save(path, Sites.Select(s => s.ToModel()).ToList());

                IsDirty = false;
                StatusMessage = Loc.F("Main_Saved", path);
            }
            catch (Exception ex)
            {
                StatusMessage = Loc.F("Main_SaveFailed", ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // サイトの追加・削除
        // ------------------------------------------------------------------

        private void AddSite()
        {
            var site = new SiteViewModel
            {
                Id = MakeUniqueId(),
                DisplayName = Loc.T("Site_NewSite")
            };

            Sites.Add(site);
            SelectedSite = site;
            StatusMessage = Loc.T("Main_SiteAdded");
        }

        private string MakeUniqueId()
        {
            for (var i = 1; ; i++)
            {
                var candidate = "site-" + i;
                if (!Sites.Any(s => string.Equals(s.Id, candidate, StringComparison.OrdinalIgnoreCase)))
                    return candidate;
            }
        }

        /// <summary>
        /// 選択中サイトを複製する。DDCのFQDNを手で打ち直さずに、
        /// 認証モード違いや検証用のサイトを増やせるようにするため（打ち間違いの防止にもなる）。
        /// </summary>
        private void CopySite()
        {
            var source = SelectedSite;
            if (source == null) return;

            var copy = source.CreateCopy(MakeCopyId(source.Id), MakeCopyDisplayName(source.DisplayName));

            // 元の行の直下に入れる。末尾に足すとサイトが多いときに見失うため。
            var index = Sites.IndexOf(source);
            Sites.Insert(index < 0 ? Sites.Count : index + 1, copy);

            SelectedSite = copy;
            StatusMessage = Loc.F("Main_SiteCopied", source.Label);
        }

        /// <summary>
        /// 複製用のIDを作る。IDが重複していると保存時に弾かれるため、必ず新しいものにする。
        /// 「元のID-copy」、既にあれば -copy2, -copy3 … と採番する。
        /// </summary>
        private string MakeCopyId(string sourceId)
        {
            var baseId = string.IsNullOrWhiteSpace(sourceId) ? "site" : sourceId.Trim();

            var candidate = baseId + "-copy";
            if (!IdExists(candidate)) return candidate;

            for (var i = 2; ; i++)
            {
                candidate = baseId + "-copy" + i;
                if (!IdExists(candidate)) return candidate;
            }
        }

        private bool IdExists(string id)
        {
            return Sites.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static string MakeCopyDisplayName(string sourceDisplayName)
        {
            return string.IsNullOrWhiteSpace(sourceDisplayName)
                ? Loc.T("Site_CopyOfNew")
                : Loc.F("Site_CopySuffix", sourceDisplayName.Trim());
        }

        private void DeleteSite()
        {
            var target = SelectedSite;
            if (target == null) return;

            var index = Sites.IndexOf(target);
            Sites.Remove(target);

            SelectedSite = Sites.Count == 0
                ? null
                : Sites[Math.Min(index, Sites.Count - 1)];

            StatusMessage = Loc.F("Main_SiteDeleted", target.Label);
        }

        // ------------------------------------------------------------------
        // 接続テスト
        // ------------------------------------------------------------------

        private async Task TestSelectedAsync()
        {
            if (SelectedSite == null) return;
            await TestSitesAsync(new[] { SelectedSite });
        }

        private async Task TestAllAsync()
        {
            await TestSitesAsync(Sites.ToList());
        }

        private async Task TestSitesAsync(IList<SiteViewModel> targets)
        {
            IsBusy = true;

            var display = new StringBuilder();
            var logged = new StringBuilder();
            logged.Append(ConnectionReportFormatter.FormatHeader());
            logged.AppendLine();

            var results = new List<SiteConnectionResult>();

            try
            {
                foreach (var siteVm in targets)
                {
                    var error = siteVm.Validate();
                    if (error != null)
                    {
                        display.AppendLine(Loc.F("Main_SiteError", siteVm.Label, error));
                        display.AppendLine();
                        siteVm.Status = SiteStatus.Failed;
                        siteVm.StatusDetail = error;
                        continue;
                    }

                    var model = siteVm.ToModel();

                    // 資格情報の入力はUIスレッドで、かつ接続開始前に済ませる。
                    // 入力された CredentialInput はワーカー起動まで保持し、使用後に破棄する。
                    CredentialInput credential = null;
                    if (model.AuthMode == AuthMode.PromptForCredential)
                    {
                        credential = PromptForCredential(siteVm, display);
                        if (credential == null) continue; // キャンセル
                    }

                    siteVm.Status = SiteStatus.Testing;
                    StatusMessage = Loc.F("Main_Connecting", siteVm.Label);

                    try
                    {
                        // 接続処理は必ず別プロセス（ワーカー）で行う。
                        // 統合認証はGUIと同じユーザーのワーカー、別資格情報は
                        // netonly で起動したワーカー。資格情報ごとにプロセスが分かれるため、
                        // フェーズ2の接続再利用問題が構造的に起きない。
                        SiteConnectionResult result;
                        if (credential == null)
                        {
                            result = await Task.Run(() => _service.TestSiteIntegrated(model));
                        }
                        else
                        {
                            var cred = credential;
                            result = await Task.Run(() => _service.TestSiteWithCredential(
                                model, cred.Domain, cred.UserName, cred.Password));
                        }

                        results.Add(result);

                        ApplyResultToSite(siteVm, result);

                        display.Append(ConnectionReportFormatter.FormatSiteResult(result, false));
                        display.AppendLine();

                        logged.Append(ConnectionReportFormatter.FormatSiteResult(result, true));
                        logged.AppendLine();
                    }
                    finally
                    {
                        if (credential != null) credential.Dispose();
                    }
                }

                if (results.Count == 0)
                {
                    // 1件も接続を試みていない（入力不備や資格情報のキャンセルのみ）。
                    // ヘッダだけのログを残しても後で読む価値がないので書き出さない。
                    ResultText = display.ToString();
                    StatusMessage = Loc.T("Main_TestNotRun");
                    return;
                }

                var summary = ConnectionReportFormatter.FormatSummary(results);
                display.Append(summary);
                logged.Append(summary);

                ResultText = display.ToString();
                WriteLog(logged.ToString(), results.Count);
            }
            catch (Exception ex)
            {
                ResultText = display + Environment.NewLine
                    + Loc.T("Main_TestAborted") + ex.GetType().FullName + ": " + ex.Message;
                StatusMessage = Loc.T("Main_TestError");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 資格情報を入力させる。キャンセル時は null を返し、理由を表示に残す。
        /// 返した <see cref="CredentialInput"/> は呼び出し側が使用後に破棄する。
        /// </summary>
        private CredentialInput PromptForCredential(SiteViewModel siteVm, StringBuilder display)
        {
            var credential = _credentialPrompt.Prompt(siteVm.Label);
            if (credential == null)
            {
                display.AppendLine(Loc.F("Main_CredentialCancelled", siteVm.Label));
                display.AppendLine();
                siteVm.Status = SiteStatus.Unknown;
                siteVm.StatusDetail = Loc.T("Common_Cancel");
            }

            return credential;
        }

        private static void ApplyResultToSite(SiteViewModel siteVm, SiteConnectionResult result)
        {
            if (result.Success)
            {
                siteVm.Status = SiteStatus.Connected;
                siteVm.StatusDetail = string.Format("{0} / Ver {1}",
                    result.SuccessfulAttempt.DdcAddress,
                    result.SuccessfulAttempt.Version ?? Loc.T("Common_Unknown"));

                // 接続できていてもライセンスサーバーと通信できていない状態は、
                // 猶予が切れた時点で新規セッションが拒否される。一覧の状態欄で先に気づかせる。
                var license = result.SuccessfulAttempt.License;
                if (license != null && (license.GracePeriodActive == true || license.HasControllerProblem))
                    siteVm.StatusDetail += "  " + Loc.T("Lic_WarnShort");
            }
            else
            {
                siteVm.Status = SiteStatus.Failed;

                var last = result.Attempts.LastOrDefault();
                siteVm.StatusDetail = result.SdkUnavailable
                    ? Loc.T("Main_StatusSdkMissing")
                    : (last == null ? Loc.T("Common_Failed") : last.ErrorMessage);
            }
        }

        private void WriteLog(string content, int siteCount)
        {
            string failureReason;
            var path = RunLogWriter.Write(content, out failureReason);

            StatusMessage = path != null
                ? Loc.F("Main_TestDoneLog", siteCount, path)
                : Loc.F("Main_TestDoneNoLog", siteCount, failureReason);
        }

        // ------------------------------------------------------------------
        // フォルダを開く
        // ------------------------------------------------------------------

        private void OpenLogFolder()
        {
            OpenFolder(RunLogWriter.GetLogDirectory());
        }

        private void OpenConfigFolder()
        {
            OpenFolder(Path.GetDirectoryName(SiteConfigStore.GetDefaultRoamingPath()));
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                StatusMessage = Loc.F("Main_OpenFolderFailed", ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // 変更追跡
        // ------------------------------------------------------------------

        private void OnSitesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (SiteViewModel item in e.OldItems)
                    item.PropertyChanged -= OnSitePropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (SiteViewModel item in e.NewItems)
                    item.PropertyChanged += OnSitePropertyChanged;
            }

            IsDirty = true;
            RaiseCommandStates();
        }

        private void OnSitePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 接続状態は編集内容ではないので、未保存扱いにはしない。
            if (e.PropertyName == nameof(SiteViewModel.Status) ||
                e.PropertyName == nameof(SiteViewModel.StatusGlyph) ||
                e.PropertyName == nameof(SiteViewModel.StatusDetail))
                return;

            IsDirty = true;
        }

        private void RaiseCommandStates()
        {
            AddSiteCommand.RaiseCanExecuteChanged();
            DeleteSiteCommand.RaiseCanExecuteChanged();
            CopySiteCommand.RaiseCanExecuteChanged();
            SaveCommand.RaiseCanExecuteChanged();
            TestSelectedCommand.RaiseCanExecuteChanged();
            TestAllCommand.RaiseCanExecuteChanged();
        }
    }
}
