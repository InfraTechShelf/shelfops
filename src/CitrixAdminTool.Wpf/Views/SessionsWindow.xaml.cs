using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CitrixAdminTool.Core.Models;
using CitrixAdminTool.Wpf.Services.Localization;
using CitrixAdminTool.Wpf.ViewModels;

namespace CitrixAdminTool.Wpf.Views
{
    /// <summary>
    /// セッション一覧と、ログオフ・切断操作のウィンドウ。
    ///
    /// ログオフ・切断はエンドユーザーのセッションを止める破壊的操作なので、
    /// 実行前に必ず確認ダイアログを出す。特にログオフは未保存の作業が失われるため強く警告する。
    /// 複数選択に対応したため、確認では**必ず件数を明示**する。
    ///
    /// 電源操作（シャットダウン・再起動）はさらに影響が大きい。対象はセッションではなく
    /// **セッションをホストしているマシン**であり、同じマシン上の他のユーザーも巻き添えになる。
    /// そのため確認ダイアログでは、選択件数ではなく実際に影響を受けるセッション数を示す。
    /// </summary>
    public partial class SessionsWindow : Window
    {
        /// <summary>確認ダイアログに対象名を列挙する上限。これを超えたら件数と代表例に留める。</summary>
        private const int MaxNamesInPrompt = 10;

        private readonly SessionsViewModel _vm;

        public SessionsWindow(SessionsViewModel viewModel)
        {
            InitializeComponent();
            _vm = viewModel;
            DataContext = _vm;
        }

        /// <summary>現在選択されている行。未選択なら空のリスト。</summary>
        private IList<BrokerSession> SelectedSessions()
        {
            return SessionGrid.SelectedItems.OfType<BrokerSession>().ToList();
        }

        private async void OnLogoff(object sender, RoutedEventArgs e)
        {
            var sessions = SelectedSessions();
            if (!EnsureSelected(sessions, Loc.T("Sessions_ConfirmLogoffTitle"))) return;

            var message = sessions.Count == 1
                ? Loc.F("Sessions_ConfirmLogoffOne", sessions[0].UserName, sessions[0].MachineName)
                : Loc.F("Sessions_ConfirmLogoffMany", sessions.Count, FormatTargets(sessions));

            var answer = MessageBox.Show(this, message,
                Loc.T("Sessions_ConfirmLogoffTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes) return;

            await _vm.LogoffAsync(sessions);
        }

        private async void OnDisconnect(object sender, RoutedEventArgs e)
        {
            var sessions = SelectedSessions();
            if (!EnsureSelected(sessions, Loc.T("Sessions_ConfirmDisconnectTitle"))) return;

            var message = sessions.Count == 1
                ? Loc.F("Sessions_ConfirmDisconnectOne", sessions[0].UserName, sessions[0].MachineName)
                : Loc.F("Sessions_ConfirmDisconnectMany", sessions.Count, FormatTargets(sessions));

            var answer = MessageBox.Show(this, message,
                Loc.T("Sessions_ConfirmDisconnectTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes) return;

            await _vm.DisconnectAsync(sessions);
        }

        private bool EnsureSelected(IList<BrokerSession> sessions, string action)
        {
            if (sessions.Count > 0) return true;

            MessageBox.Show(this, Loc.T("Sessions_SelectTargetHint"),
                action, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        /// <summary>
        /// 確認ダイアログ用に対象を列挙する。対象はユーザー名＋マシン名で示す
        /// （同じユーザーが複数マシンにセッションを持つことがあるため、両方ないと特定できない）。
        /// </summary>
        private static string FormatTargets(IList<BrokerSession> sessions)
        {
            var names = sessions
                .Select(s => string.Format("{0} / {1}", s.UserName ?? Loc.T("Common_Unknown"), s.MachineName ?? Loc.T("Common_Unknown")))
                .ToList();

            if (names.Count <= MaxNamesInPrompt)
                return string.Join("\n", names.ToArray());

            var shown = string.Join("\n", names.Take(MaxNamesInPrompt).ToArray());
            return shown + Loc.F("Sessions_MoreItems", names.Count - MaxNamesInPrompt);
        }

        // ------------------------------------------------------------------
        // 電源操作（マシンに対する破壊的操作）
        // ------------------------------------------------------------------

        private async void OnPowerShutdown(object sender, RoutedEventArgs e)
        {
            await PowerActionAsync(BrokerPowerAction.Shutdown, Loc.T("Power_Shutdown"));
        }

        private async void OnPowerRestart(object sender, RoutedEventArgs e)
        {
            await PowerActionAsync(BrokerPowerAction.Restart, Loc.T("Power_Restart"));
        }

        private async void OnPowerTurnOff(object sender, RoutedEventArgs e)
        {
            await PowerActionAsync(BrokerPowerAction.TurnOff, Loc.T("Power_TurnOff"));
        }

        private async void OnPowerReset(object sender, RoutedEventArgs e)
        {
            await PowerActionAsync(BrokerPowerAction.Reset, Loc.T("Power_Reset"));
        }

        /// <summary>
        /// 電源操作の確認と実行。
        ///
        /// 【この操作の対象は「セッション」ではなく「マシン」である】
        /// 選んだセッションが動いているマシンごと止めるため、同じマシン上の
        /// 他のユーザーも巻き添えになる。確認ダイアログでは、選択件数ではなく
        /// **実際に影響を受けるセッション数**を示す。
        /// </summary>
        private async Task PowerActionAsync(BrokerPowerAction action, string actionLabel)
        {
            var sessions = SelectedSessions();
            if (!EnsureSelected(sessions, actionLabel)) return;

            var machineNames = _vm.ResolveMachineNames(sessions);
            if (machineNames.Count == 0)
            {
                MessageBox.Show(this, Loc.T("Power_NoMachine"),
                    actionLabel, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 選択していないセッションも含め、対象マシン上の全セッションを数える。
            var affected = _vm.CountSessionsOnMachines(machineNames);

            var message = new StringBuilder();
            message.Append(Loc.F("Power_ConfirmHeader", machineNames.Count, actionLabel));
            message.AppendLine();
            message.AppendLine();
            message.AppendLine(FormatMachines(machineNames));

            // 巻き添えになるセッションがある場合は、それを最初に伝える。
            if (affected > sessions.Count)
            {
                message.AppendLine();
                message.AppendLine(Loc.F("Power_ConfirmAffected", sessions.Count, affected));
                message.AppendLine(Loc.T("Power_ConfirmAffected2"));
                message.AppendLine(Loc.T("Power_ConfirmAffected3"));
            }

            message.AppendLine();
            message.AppendLine(Loc.T(BrokerPowerActions.IsForced(action)
                ? "Power_WarnForced"
                : "Power_WarnGraceful"));

            message.AppendLine();
            message.Append(Loc.T("Power_ConfirmTail"));

            var answer = MessageBox.Show(this, message.ToString(),
                Loc.F("Power_ConfirmTitle", actionLabel),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes) return;

            await _vm.PowerActionAsync(sessions, action);
        }

        /// <summary>確認ダイアログ用にマシン名を列挙する。多い場合は代表例と残数に切り替える。</summary>
        private static string FormatMachines(IList<string> machineNames)
        {
            if (machineNames.Count <= MaxNamesInPrompt)
                return string.Join("\n", machineNames.ToArray());

            var shown = string.Join("\n", machineNames.Take(MaxNamesInPrompt).ToArray());
            return shown + Loc.F("Machines_MoreItems", machineNames.Count - MaxNamesInPrompt);
        }

        // ------------------------------------------------------------------
        // エクスポート
        // ------------------------------------------------------------------

        private void OnCopyToClipboard(object sender, RoutedEventArgs e)
        {
            var rows = _vm.ResolveExportRows(SelectedSessions());
            if (rows.Count == 0)
            {
                MessageBox.Show(this, Loc.T("Export_NoRows"),
                    Loc.T("Common_CopyForExcel"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _vm.CopyToClipboard(rows);
        }

        private void OnExportCsv(object sender, RoutedEventArgs e)
        {
            var rows = _vm.ResolveExportRows(SelectedSessions());
            if (rows.Count == 0)
            {
                MessageBox.Show(this, Loc.T("Export_NoRows"),
                    Loc.T("Export_ActionCsv"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.T("Sessions_CsvDialogTitle"),
                FileName = _vm.BuildExportFileName(),
                DefaultExt = ".csv",
                Filter = Loc.T("Export_CsvFilter"),
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true) return;

            _vm.ExportCsv(dialog.FileName, rows);
        }

        // ------------------------------------------------------------------

        /// <summary>直近の一括操作の内訳を表示する。どのセッションが失敗したかを画面で確認するため。</summary>
        private void OnShowDetail(object sender, RoutedEventArgs e)
        {
            var result = _vm.LastOperationResult;
            if (result == null || result.Targets.Count == 0) return;

            var summary = Loc.F("Sessions_DetailSummary",
                result.SuccessCount, result.FailureCount, result.Targets.Count);

            var window = new OperationDetailWindow(Loc.T("Sessions_DetailTitle"), summary, result.Targets)
            {
                Owner = this
            };
            window.ShowDialog();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            _vm.Dispose();
            base.OnClosed(e);
        }
    }
}
