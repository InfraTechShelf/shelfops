using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CitrixAdminTool.Core.Models;
using CitrixAdminTool.Wpf.Services.Localization;
using CitrixAdminTool.Wpf.ViewModels;

namespace CitrixAdminTool.Wpf.Views
{
    /// <summary>
    /// デリバリーグループ一覧のウィンドウ（フェーズ5・0.8）。
    /// この段階では読み取りと書き出しのみ。書き込み操作は 0.9 以降で追加する。
    /// </summary>
    public partial class DesktopGroupsWindow : Window
    {
        private readonly DesktopGroupsViewModel _vm;

        public DesktopGroupsWindow(DesktopGroupsViewModel viewModel)
        {
            InitializeComponent();
            _vm = viewModel;
            DataContext = _vm;
        }

        private IList<BrokerDesktopGroup> SelectedGroups()
        {
            return GroupGrid.SelectedItems.OfType<BrokerDesktopGroup>().ToList();
        }

        // ------------------------------------------------------------------
        // 構成の採取・比較
        // ------------------------------------------------------------------

        /// <summary>
        /// 対象を1つに絞る。採取は1グループ単位（複数を1ファイルにまとめると、
        /// 比較・復元のときにどのグループの話か曖昧になるため）。
        /// </summary>
        private BrokerDesktopGroup SingleSelection()
        {
            var selected = SelectedGroups();
            if (selected.Count == 1) return selected[0];

            MessageBox.Show(this,
                Loc.T(selected.Count == 0 ? "Snap_SelectOne" : "Snap_SelectOnlyOne"),
                Loc.T("Snap_Capture"), MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        private async void OnCapture(object sender, RoutedEventArgs e)
        {
            var group = SingleSelection();
            if (group == null) return;

            var snapshot = await _vm.CaptureAsync(group);
            if (snapshot == null) return;

            new SnapshotWindow(new SnapshotViewModel(snapshot, _vm.SiteId, _vm.SiteLabel)) { Owner = this }.Show();
        }

        /// <summary>
        /// 保存ファイルと比較する。ファイルを先に選ばせてから採取する
        /// （ファイル選択でキャンセルされたときに、無駄な問い合わせをしないため）。
        /// </summary>
        private async void OnCompare(object sender, RoutedEventArgs e)
        {
            var group = SingleSelection();
            if (group == null) return;

            var path = SnapshotWindow.PickSnapshotFile(this);
            if (path == null) return;

            var current = await _vm.CaptureAsync(group);
            if (current == null) return;

            SnapshotWindow.ShowComparison(this, path, current);
        }

        private void OnCopyToClipboard(object sender, RoutedEventArgs e)
        {
            var rows = _vm.ResolveExportRows(SelectedGroups());
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
            var rows = _vm.ResolveExportRows(SelectedGroups());
            if (rows.Count == 0)
            {
                MessageBox.Show(this, Loc.T("Export_NoRows"),
                    Loc.T("Export_ActionCsv"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.T("Dg_CsvDialogTitle"),
                FileName = _vm.BuildExportFileName(),
                DefaultExt = ".csv",
                Filter = Loc.T("Export_CsvFilter"),
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true) return;

            _vm.ExportCsv(dialog.FileName, rows);
        }

        protected override void OnClosed(System.EventArgs e)
        {
            _vm.Dispose();
            base.OnClosed(e);
        }
    }
}
