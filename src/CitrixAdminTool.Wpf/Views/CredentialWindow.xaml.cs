using System.Windows;
using CitrixAdminTool.Wpf.Services;
using CitrixAdminTool.Wpf.Services.Localization;

namespace CitrixAdminTool.Wpf.Views
{
    /// <summary>
    /// 別資格情報の入力ダイアログ。
    ///
    /// パスワードは <see cref="System.Windows.Controls.PasswordBox"/> から
    /// SecureString のまま取り出し、マネージドな string には一度も変換しない。
    /// 当然どこにも保存しない（仕様書§2「都度入力・非永続」）。
    /// </summary>
    public partial class CredentialWindow : Window
    {
        public CredentialWindow(string siteLabel)
        {
            InitializeComponent();

            HeaderText.Text = Loc.F("Cred_Header", siteLabel);

            UserNameBox.Focus();
        }

        /// <summary>入力結果。ダイアログがOKで閉じた場合のみ非null。</summary>
        public CredentialInput Result { get; private set; }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            var userName = UserNameBox.Text == null ? string.Empty : UserNameBox.Text.Trim();

            if (userName.Length == 0)
            {
                ShowError(Loc.T("Cred_NeedUserName"));
                UserNameBox.Focus();
                return;
            }

            if (PasswordBox.SecurePassword.Length == 0)
            {
                ShowError(Loc.T("Cred_NeedPassword"));
                PasswordBox.Focus();
                return;
            }

            var domain = DomainBox.Text == null ? string.Empty : DomainBox.Text.Trim();

            // SecurePassword はPasswordBoxが所有するインスタンスのコピーを返すため、
            // ここで取得したものは呼び出し側が破棄してよい。
            Result = new CredentialInput(domain, userName, PasswordBox.SecurePassword);

            DialogResult = true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
