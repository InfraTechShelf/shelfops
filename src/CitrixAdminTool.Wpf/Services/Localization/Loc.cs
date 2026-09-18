using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace CitrixAdminTool.Wpf.Services.Localization
{
    /// <summary>表示言語。</summary>
    public enum AppLanguage
    {
        Japanese,
        English
    }

    /// <summary>
    /// 画面文言の切り替え。
    ///
    /// 【なぜ .resx / サテライトアセンブリを使わないか】
    /// 本ツールは「フォルダをコピーすれば動く」形を維持しており、外部依存も増やさない方針。
    /// サテライトアセンブリは言語別サブフォルダを増やすうえ、実行中の言語切り替えには
    /// 結局この種の通知の仕組みが要る。文字列は2言語だけなので、アセンブリに埋め込んだ
    /// 辞書で持つ方が構成が単純で、切り替えも即座に反映できる。
    ///
    /// 【XAMLからの使い方】
    /// <c>Content="{loc:T Common_Refresh}"</c> のように <see cref="TExtension"/> 経由で参照する。
    /// インデクサへのバインドになるため、言語を切り替えると画面が即座に追従する。
    ///
    /// 【C#からの使い方】
    /// <see cref="T(string)"/> / <see cref="F(string, object[])"/> を都度呼ぶ。
    /// 呼んだ時点の言語で解決されるので、メッセージ生成のたびに正しい言語になる。
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        /// <summary>XAMLのバインド先。言語切り替え時にこのインスタンスが変更通知を出す。</summary>
        public static readonly Loc Instance = new Loc();

        private static Dictionary<string, string> _map = StringTable.Build(AppLanguage.Japanese);
        private static AppLanguage _language = AppLanguage.Japanese;

        private Loc() { }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 言語が変わったときに発火する。バインドで追従できない箇所
        /// （既に組み立て済みの文字列を持つViewModel等）が作り直すために使う。
        /// </summary>
        public static event EventHandler LanguageChanged;

        public static AppLanguage Language
        {
            get { return _language; }
        }

        /// <summary>
        /// XAMLバインド用のインデクサ。キーが無ければ <c>!!キー名!!</c> を返す。
        /// 黙って空文字にすると訳し漏れに気づけないため、画面上で目立つ形にしてある
        /// （検証用のテストでも同じ規則で未定義キーを検出している）。
        /// </summary>
        public string this[string key]
        {
            get { return T(key); }
        }

        /// <summary>現在の言語で文言を引く。</summary>
        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            string value;
            return _map.TryGetValue(key, out value) ? value : "!!" + key + "!!";
        }

        /// <summary>書式付きの文言を引く（<c>{0}</c> 等を差し替える）。</summary>
        public static string F(string key, params object[] args)
        {
            var format = T(key);
            if (args == null || args.Length == 0) return format;

            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                // 訳文の書式指定が壊れていても、画面が落ちるよりは素の文言を出す方がまし。
                return format;
            }
        }

        /// <summary>表示言語を切り替える。画面のバインドは即座に追従する。</summary>
        public static void SetLanguage(AppLanguage language)
        {
            if (_language == language && _map != null) return;

            _language = language;
            _map = StringTable.Build(language);

            // "Item[]" はインデクサ全体が変わったことを示すWPFの規約。
            var handler = Instance.PropertyChanged;
            if (handler != null) handler(Instance, new PropertyChangedEventArgs("Item[]"));

            var changed = LanguageChanged;
            if (changed != null) changed(null, EventArgs.Empty);
        }

        /// <summary>
        /// OSの表示言語から既定を決める。日本語環境なら日本語、それ以外は英語。
        /// 利用者が明示的に選んでいる場合は、そちらが優先される（呼び出し側で判断）。
        /// </summary>
        public static AppLanguage DetectFromOs()
        {
            try
            {
                var name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return string.Equals(name, "ja", StringComparison.OrdinalIgnoreCase)
                    ? AppLanguage.Japanese
                    : AppLanguage.English;
            }
            catch
            {
                return AppLanguage.English;
            }
        }

        /// <summary>設定文字列（保存用）と言語の相互変換。</summary>
        public static string ToSettingValue(AppLanguage language)
        {
            return language == AppLanguage.English ? "en" : "ja";
        }

        public static AppLanguage? FromSettingValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var v = value.Trim();
            if (string.Equals(v, "ja", StringComparison.OrdinalIgnoreCase)) return AppLanguage.Japanese;
            if (string.Equals(v, "en", StringComparison.OrdinalIgnoreCase)) return AppLanguage.English;
            return null;
        }

        /// <summary>検証用: 現在読み込まれている文言のキー一覧。</summary>
        public static IEnumerable<string> CurrentKeys
        {
            get { return _map.Keys; }
        }
    }
}
