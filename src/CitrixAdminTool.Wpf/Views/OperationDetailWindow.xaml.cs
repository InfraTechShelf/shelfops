using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CitrixAdminTool.Core.Models;
using CitrixAdminTool.Wpf.Services;
using CitrixAdminTool.Wpf.Services.Localization;

namespace CitrixAdminTool.Wpf.Views
{
    /// <summary>
    /// 一括操作の結果を対象ごとに表示するウィンドウ。
    ///
    /// 「成功 8 台 / 失敗 2 台」だけでは、どのマシン・どのセッションが失敗したのか
    /// 分からず対処できない。ログファイルには全件残るが、失敗直後に画面で確認できる方が
    /// 実務では圧倒的に速いため、この内訳を出す。
    ///
    /// マシンの一括操作とセッションの一括操作で同じウィンドウを使う
    /// （Core側が対象ごとの結果を共通の <see cref="BrokerTargetResult"/> で返すため）。
    /// </summary>
    public partial class OperationDetailWindow : Window
    {
        /// <summary>
        /// DataGrid にバインドする1行分。日本語の結果表記をここで持たせる。
        ///
        /// public にしてあるのは、WPFのデータバインディングが
        /// 非公開型のプロパティを解決できず、バインドが黙って失敗するため。
        /// </summary>
        public class Row
        {
            public string TargetName { get; set; }
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public string ResultLabel { get { return Loc.T(Success ? "Common_Succeeded" : "Common_Failed"); } }
        }

        private readonly List<Row> _rows;

        public OperationDetailWindow(string title, string summary, IEnumerable<BrokerTargetResult> targets)
        {
            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(title)) Title = title;
            SummaryText.Text = summary ?? string.Empty;

            // 失敗を先頭に並べる。対象が多いとき、見るべき行を探させないため。
            _rows = (targets ?? Enumerable.Empty<BrokerTargetResult>())
                .Select(t => new Row
                {
                    TargetName = t.TargetName,
                    Success = t.Success,
                    ErrorMessage = t.ErrorMessage
                })
                .OrderBy(r => r.Success)
                .ToList();

            TargetGrid.ItemsSource = _rows;
        }

        /// <summary>内訳をタブ区切りでコピーする。調査記録としてそのまま貼り付けられる。</summary>
        private void OnCopy(object sender, RoutedEventArgs e)
        {
            var columns = new List<ExportColumn<Row>>
            {
                new ExportColumn<Row>(Loc.T("Detail_ColResult"), r => r.ResultLabel),
                new ExportColumn<Row>(Loc.T("Detail_ColTarget"), r => r.TargetName),
                new ExportColumn<Row>(Loc.T("Detail_ColReason"), r => r.ErrorMessage)
            };

            string error;
            if (TableExporter.TrySetClipboard(TableExporter.ToTsv(_rows, columns), out error))
            {
                MessageBox.Show(this,
                    Loc.F("Detail_Copied", _rows.Count),
                    Loc.T("Detail_CopyTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this,
                    Loc.F("Export_ClipboardFailed", error),
                    Loc.T("Detail_CopyTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
