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
    /// マシン一覧とメンテナンスモード操作のウィンドウ。
    ///
    /// メンテナンスモード切替は本番環境を変更する書き込み操作なので、
    /// 実行前に必ず確認ダイアログを出す（このコードビハインドの責務）。
    /// 複数選択に対応したため、確認では**必ず件数を明示**する。
    ///
    /// DataGrid の SelectedItems はバインドできないため、選択行の受け渡しは
    /// ここからViewModelのメソッドへ引数で渡す。
    ///
    /// 電源操作はさらに影響が大きい。マシンを止めると、そのマシンで作業している
    /// 利用者のセッションがすべて落ちるため、確認では**影響を受けるセッション数**を示す。
    /// </summary>
    public partial class MachinesWindow : Window
    {
        /// <summary>確認ダイアログに対象名を列挙する上限。これを超えたら件数と代表例に留める。</summary>
        private const int MaxNamesInPrompt = 10;

        private readonly MachinesViewModel _vm;

        public MachinesWindow(MachinesViewModel viewModel)
        {
            InitializeComponent();
            _vm = viewModel;
            DataContext = _vm;
        }

        /// <summary>現在選択されている行。未選択なら空のリスト。</summary>
        private IList<BrokerMachine> SelectedMachines()
        {
            return MachineGrid.SelectedItems.OfType<BrokerMachine>().ToList();
        }

        private async void OnMaintenanceOn(object sender, RoutedEventArgs e)
        {
            await ToggleMaintenanceAsync(true);
        }

        private async void OnMaintenanceOff(object sender, RoutedEventArgs e)
        {
            await ToggleMaintenanceAsync(false);
        }

        private async Task ToggleMaintenanceAsync(bool enabled)
        {
            var machines = SelectedMachines();
            if (machines.Count == 0)
            {
                MessageBox.Show(this, Loc.T("Machines_SelectTargetHint"),
                    Loc.T("Machines_ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var mode = enabled ? "ON" : "OFF";

            // 書き込み操作なので必ず確認する。件数を明示し、少数なら名前も出す。
            var message = machines.Count == 1
                ? Loc.F("Machines_ConfirmOne", machines[0].MachineName, mode)
                : Loc.F("Machines_ConfirmMany", machines.Count, mode, FormatTargets(machines));

            var answer = MessageBox.Show(this, message,
                Loc.T("Machines_ConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes) return;

            await _vm.SetMaintenanceAsync(machines, enabled);
        }

        /// <summary>
        /// 確認ダイアログ用に対象を列挙する。対象が多いときに全部並べると
        /// ダイアログが画面からはみ出すため、上限を超えたら代表例と残数に切り替える。
        /// </summary>
        private static string FormatTargets(IList<BrokerMachine> machines)
        {
            var names = machines.Select(m => m.MachineName).ToList();

            if (names.Count <= MaxNamesInPrompt)
                return string.Join("\n", names.ToArray());

            var shown = string.Join("\n", names.Take(MaxNamesInPrompt).ToArray());
            return shown + Loc.F("Machines_MoreItems", names.Count - MaxNamesInPrompt);
        }

        // ------------------------------------------------------------------
        // 電源操作（破壊的）
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
        /// 【警告の強さを影響範囲で変える】
        /// 対象マシンでセッションが動いていれば、その利用者は作業中に切断される。
        /// 一方、未登録マシンの再起動のように誰も使っていない場合もある。
        /// 毎回同じ強さで脅かすと警告そのものが読まれなくなるため、
        /// セッション数が 0 のときは「影響を受ける利用者はいません」と伝える。
        /// </summary>
        private async Task PowerActionAsync(BrokerPowerAction action, string actionLabel)
        {
            var machines = SelectedMachines();
            if (machines.Count == 0)
            {
                MessageBox.Show(this, Loc.T("Machines_SelectTargetHint"),
                    Loc.F("Power_ConfirmTitle", actionLabel), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var names = _vm.ResolveMachineNames(machines);
            var affected = _vm.CountSessionsOn(machines);

            var message = new StringBuilder();
            message.Append(Loc.F("Power_ConfirmHeader", names.Count, actionLabel));
            message.AppendLine();
            message.AppendLine();
            message.AppendLine(FormatTargets(machines));
            message.AppendLine();

            if (affected > 0)
            {
                message.AppendLine(Loc.F("Machines_PowerAffected", affected));
                message.AppendLine(Loc.T("Power_ConfirmAffected3"));
            }
            else
            {
                message.AppendLine(Loc.T("Machines_PowerNoSessions"));
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

            await _vm.PowerActionAsync(machines, action);
        }

        // ------------------------------------------------------------------
        // エクスポート
        // ------------------------------------------------------------------

        private void OnCopyToClipboard(object sender, RoutedEventArgs e)
        {
            var rows = _vm.ResolveExportRows(SelectedMachines());
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
            var rows = _vm.ResolveExportRows(SelectedMachines());
            if (rows.Count == 0)
            {
                MessageBox.Show(this, Loc.T("Export_NoRows"),
                    Loc.T("Export_ActionCsv"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.T("Machines_CsvDialogTitle"),
                FileName = _vm.BuildExportFileName(),
                DefaultExt = ".csv",
                Filter = Loc.T("Export_CsvFilter"),
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true) return;

            _vm.ExportCsv(dialog.FileName, rows);
        }

        // ------------------------------------------------------------------

        /// <summary>直近の一括操作の内訳を表示する。どのマシンが失敗したかを画面で確認するため。</summary>
        private void OnShowDetail(object sender, RoutedEventArgs e)
        {
            var result = _vm.LastOperationResult;
            if (result == null || result.Targets.Count == 0) return;

            var summary = Loc.F("Machines_DetailSummary",
                result.SuccessCount, result.FailureCount, result.Targets.Count);

            // 直近の操作がメンテナンス切替か電源操作かで見出しを変える。
            var title = result.Operation == "powerAction"
                ? Loc.T("Power_DetailTitle")
                : Loc.T("Machines_DetailTitle");

            var window = new OperationDetailWindow(title, summary, result.Targets)
            {
                Owner = this
            };
            window.ShowDialog();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            // ウィンドウ中だけ保持していた資格情報を破棄する。
            _vm.Dispose();
            base.OnClosed(e);
        }
    }
}
