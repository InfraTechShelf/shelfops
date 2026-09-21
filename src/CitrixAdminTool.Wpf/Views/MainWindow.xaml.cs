using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CitrixAdminTool.Wpf.Services;
using CitrixAdminTool.Wpf.Services.Localization;
using CitrixAdminTool.Wpf.ViewModels;

namespace CitrixAdminTool.Wpf.Views
{
    public partial class MainWindow : Window, ICredentialPrompt
    {
        private readonly MainViewModel _viewModel;
        private bool _suppressLanguageEvent;

        public MainWindow()
        {
            InitializeComponent();

            // 資格情報ダイアログの表示はWindowの責務なので、
            // ViewModelにはICredentialPromptとして自分を渡す。
            _viewModel = new MainViewModel(this);
            DataContext = _viewModel;

            InitializeLanguageBox();
        }

        /// <summary>
        /// 言語の選択肢を用意し、現在の言語を選択状態にする。
        ///
        /// 起動時の言語決定は <see cref="App"/> 側で済んでいるので、ここでは表示を合わせるだけ。
        /// 選択変更のイベントは、選択状態を設定し終えてから購読する
        /// （初期化中の SelectionChanged で保存処理が走るのを避けるため）。
        /// </summary>
        private void InitializeLanguageBox()
        {
            _suppressLanguageEvent = true;
            try
            {
                LanguageBox.Items.Clear();
                LanguageBox.Items.Add(new LanguageOption(AppLanguage.Japanese, "\u65e5\u672c\u8a9e"));
                LanguageBox.Items.Add(new LanguageOption(AppLanguage.English, "English"));
                LanguageBox.DisplayMemberPath = "Display";

                foreach (LanguageOption item in LanguageBox.Items)
                {
                    if (item.Value != Loc.Language) continue;
                    LanguageBox.SelectedItem = item;
                    break;
                }
            }
            finally
            {
                _suppressLanguageEvent = false;
            }
        }

        private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressLanguageEvent) return;

            var option = LanguageBox.SelectedItem as LanguageOption;
            if (option == null) return;

            // 画面のバインドは即座に追従する。次回起動時も同じ言語で開けるよう保存する。
            Loc.SetLanguage(option.Value);
            UiStateStore.SaveLanguage(Loc.ToSettingValue(option.Value));
        }

        /// <summary>言語コンボボックスの1項目。</summary>
        private class LanguageOption
        {
            public LanguageOption(AppLanguage value, string display)
            {
                Value = value;
                Display = display;
            }

            public AppLanguage Value { get; private set; }
            public string Display { get; private set; }
        }

        public CredentialInput Prompt(string siteLabel)
        {
            var window = new CredentialWindow(siteLabel) { Owner = this };
            return window.ShowDialog() == true ? window.Result : null;
        }

        private void OnOpenMachines(object sender, RoutedEventArgs e)
        {
            var vm = _viewModel.TryCreateMachinesViewModel();
            if (vm == null) return; // 選択なし等はステータスに理由が出る

            var window = new MachinesWindow(vm) { Owner = this };
            window.Show();
        }

        private void OnOpenSessions(object sender, RoutedEventArgs e)
        {
            var vm = _viewModel.TryCreateSessionsViewModel();
            if (vm == null) return;

            var window = new SessionsWindow(vm) { Owner = this };
            window.Show();
        }

        private void OnOpenDesktopGroups(object sender, RoutedEventArgs e)
        {
            var vm = _viewModel.TryCreateDesktopGroupsViewModel();
            if (vm == null) return;

            var window = new DesktopGroupsWindow(vm) { Owner = this };
            window.Show();
        }

        private void OnAbout(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // 未保存の編集を黙って捨てない。
            if (_viewModel.IsDirty)
            {
                var answer = MessageBox.Show(
                    Loc.T("Main_ConfirmDiscard"),
                    "ShelfOps",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (answer != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // 常駐ワーカーをここで確実に停止する（孤児プロセスを残さない）。
            _viewModel.Shutdown();

            base.OnClosing(e);
        }
    }
}
