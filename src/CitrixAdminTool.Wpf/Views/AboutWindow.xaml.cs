using System.Reflection;
using System.Windows;
using CitrixAdminTool.Wpf.Services.Localization;

namespace CitrixAdminTool.Wpf.Views
{
    /// <summary>
    /// バージョン情報（About）ダイアログ。
    /// バージョン・アルファ注意・無保証・Citrix商標の帰属を表示する。
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            VersionText.Text = Loc.F("About_Version", GetInformationalVersion());
            CopyrightText.Text = GetCopyright();
        }

        /// <summary>
        /// exeのファイルプロパティに埋め込んだ著作権表示（AssemblyCopyright）をそのまま使う。
        /// 画面とファイルプロパティで表記が食い違わないよう、出所を一つにする。
        /// </summary>
        private static string GetCopyright()
        {
            try
            {
                var attr = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>();
                if (attr != null && !string.IsNullOrWhiteSpace(attr.Copyright))
                    return attr.Copyright;
            }
            catch { /* 取得失敗時は固定文字列へフォールバック */ }

            return "Copyright © Infra Tech Shelf";
        }

        /// <summary>
        /// AssemblyInformationalVersion（例 "0.5.0-alpha"）を返す。無ければ数値バージョン。
        /// </summary>
        private static string GetInformationalVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            try
            {
                var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (attr != null && !string.IsNullOrWhiteSpace(attr.InformationalVersion))
                    return attr.InformationalVersion;
            }
            catch { /* 取得失敗時は数値バージョンにフォールバック */ }

            var v = asm.GetName().Version;
            return v == null ? Loc.T("Common_Unknown") : v.ToString();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
