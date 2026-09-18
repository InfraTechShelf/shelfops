using System;
using System.Windows;
using System.Windows.Threading;
using CitrixAdminTool.Wpf.Services;
using CitrixAdminTool.Wpf.Services.Localization;
using CitrixAdminTool.Wpf.Views;

namespace CitrixAdminTool.Wpf
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 想定外の例外でウィンドウが黙って消えるのを防ぐ。
            // 検証環境ではデバッガが使えないため、内容を必ず画面に出す。
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            ApplyStartupLanguage();

            new MainWindow().Show();
        }

        /// <summary>
        /// 起動時の表示言語を決める。
        ///
        /// 利用者が画面で明示的に選んでいればそれを最優先し、
        /// 未選択のときだけOSの表示言語から判定する。
        /// ウィンドウを作る前に確定させる必要がある（初期表示に間に合わせるため）。
        /// </summary>
        private static void ApplyStartupLanguage()
        {
            var saved = Loc.FromSettingValue(UiStateStore.LoadLanguage());
            Loc.SetLanguage(saved ?? Loc.DetectFromOs());
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                Loc.T("Err_Unexpected") + Environment.NewLine + Environment.NewLine
                + e.Exception.GetType().FullName + Environment.NewLine
                + e.Exception.Message + Environment.NewLine + Environment.NewLine
                + e.Exception.StackTrace,
                "ShelfOps",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }
    }
}
