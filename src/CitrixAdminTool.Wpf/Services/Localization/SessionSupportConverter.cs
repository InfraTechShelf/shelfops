using System;
using System.Globalization;
using System.Windows.Data;

namespace CitrixAdminTool.Wpf.Services.Localization
{
    /// <summary>
    /// <c>Get-BrokerMachine</c> の SessionSupport（"SingleSession" / "MultiSession"）を
    /// 表示言語に合わせた短い表記に変換する。
    ///
    /// Coreのモデルは日本語表記を持たせず生の値のまま返す方針にした。
    /// 表示上の言い回しは画面側の関心であり、Coreに言語を持ち込むと
    /// 言語切り替えのたびにモデルを作り直す必要が出るため。
    /// </summary>
    public class SessionSupportConverter : IValueConverter
    {
        /// <summary>言語に依存せずに使える変換本体。書き出し（CSV等）からも呼ぶ。</summary>
        public static string ToLabel(string sessionSupport)
        {
            if (string.IsNullOrWhiteSpace(sessionSupport)) return string.Empty;

            if (sessionSupport.Equals("SingleSession", StringComparison.OrdinalIgnoreCase))
                return Loc.T("SessionSupport_Single");

            if (sessionSupport.Equals("MultiSession", StringComparison.OrdinalIgnoreCase))
                return Loc.T("SessionSupport_Multi");

            // 想定外の値は、隠さずそのまま出す（調査の手がかりになるため）。
            return sessionSupport;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ToLabel(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
