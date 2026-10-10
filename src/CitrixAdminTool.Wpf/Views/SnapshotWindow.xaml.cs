using System.Linq;
using System.Windows;
using CitrixAdminTool.Core.Models;
using CitrixAdminTool.Wpf.Services;
using CitrixAdminTool.Wpf.Services.Localization;
using CitrixAdminTool.Wpf.ViewModels;

namespace CitrixAdminTool.Wpf.Views
{
    /// <summary>
    /// デリバリーグループ構成のスナップショットを表示し、ファイルへの保存と、
    /// 保存ファイルとの比較を行うウィンドウ（フェーズ5・0.9。読み取りのみ）。
    /// </summary>
    public partial class SnapshotWindow : Window
    {
        private readonly SnapshotViewModel _vm;

        public SnapshotWindow(SnapshotViewModel viewModel)
        {
            InitializeComponent();
            _vm = viewModel;
            DataContext = _vm;
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.T("Snap_SaveDialogTitle"),
                FileName = SnapshotFile.BuildFileName(_vm.Snapshot, _vm.SiteId),
                DefaultExt = ".json",
                Filter = Loc.T("Snap_FileFilter"),
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true) return;

            string error;
            if (SnapshotFile.TrySave(dialog.FileName, _vm.Snapshot, out error))
            {
                _vm.StatusMessage = Loc.F("Snap_Saved", dialog.FileName);

                // 構成情報の持ち出しも、一覧の書き出しと同じく記録に残す（中身はログに書かない）。
                string failure;
                RunLogWriter.Write(ConnectionReportFormatter.FormatExportReport(
                    _vm.SiteLabel,
                    Loc.F("Snap_LogListName", _vm.Snapshot.DesktopGroupName),
                    Loc.T("Snap_ActionSave"),
                    _vm.Snapshot.RelatedItemCount + 1,
                    dialog.FileName), out failure);
            }
            else
            {
                MessageBox.Show(this, Loc.F("Snap_SaveFailed", error),
                    Loc.T("Snap_SaveFile"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnCompare(object sender, RoutedEventArgs e)
        {
            var path = PickSnapshotFile(this);
            if (path == null) return;

            ShowComparison(this, path, _vm.Snapshot);
        }

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            var selected = RowGrid.SelectedItems.OfType<SnapshotRow>().ToList();
            var rows = selected.Count > 0 ? selected : _vm.Rows.ToList();

            string error;
            if (TableExporter.TrySetClipboard(TableExporter.ToTsv(rows, SnapshotViewModel.ExportColumns()), out error))
                _vm.StatusMessage = Loc.F("Snap_Copied", rows.Count);
            else
                _vm.StatusMessage = Loc.F("Export_ClipboardFailed", error);
        }

        // ------------------------------------------------------------------
        // 比較の共通処理（デリバリーグループ一覧からも使う）
        // ------------------------------------------------------------------

        /// <summary>保存ファイルを選ばせる。キャンセルなら null。</summary>
        public static string PickSnapshotFile(Window owner)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("Snap_OpenDialogTitle"),
                DefaultExt = ".json",
                Filter = Loc.T("Snap_FileFilter"),
                CheckFileExists = true
            };

            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }

        /// <summary>
        /// 保存ファイルを読み、現在のスナップショットと比較して結果を表示する。
        /// ファイルが読めない・形式が違う場合は、理由を出して止める。
        /// </summary>
        public static void ShowComparison(Window owner, string savedPath, DesktopGroupSnapshot current)
        {
            string error;
            var saved = SnapshotFile.TryLoad(savedPath, out error);
            if (saved == null)
            {
                MessageBox.Show(owner, error, Loc.T("Snap_CompareFile"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var diff = SnapshotComparer.Compare(saved, current);
            var window = new DiffWindow(new DiffViewModel(diff, saved, current, savedPath)) { Owner = owner };
            window.Show();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            _vm.Dispose();
            base.OnClosed(e);
        }
    }
}
