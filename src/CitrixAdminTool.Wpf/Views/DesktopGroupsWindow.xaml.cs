using System.Collections.Generic;
using System.Linq;
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
