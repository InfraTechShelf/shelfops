using System.Linq;
using System.Windows;
using CitrixAdminTool.Wpf.Services;
using CitrixAdminTool.Wpf.Services.Localization;
using CitrixAdminTool.Wpf.ViewModels;

namespace CitrixAdminTool.Wpf.Views
{
    /// <summary>
    /// 保存ファイルと現在の構成の比較結果を表示するウィンドウ（0.9 は表示のみ）。
    /// </summary>
    public partial class DiffWindow : Window
    {
        private readonly DiffViewModel _vm;

        public DiffWindow(DiffViewModel viewModel)
        {
            InitializeComponent();
            _vm = viewModel;
            DataContext = _vm;
        }

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            var selected = DiffGrid.SelectedItems.OfType<DiffRow>().ToList();
            var rows = selected.Count > 0 ? selected : _vm.Rows.ToList();

            string error;
            if (TableExporter.TrySetClipboard(TableExporter.ToTsv(rows, DiffViewModel.ExportColumns()), out error))
                MessageBox.Show(this, Loc.F("Detail_Copied", rows.Count), Loc.T("Detail_CopyTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(this, Loc.F("Export_ClipboardFailed", error), Loc.T("Detail_CopyTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            _vm.Dispose();
            base.OnClosed(e);
        }
    }
}
