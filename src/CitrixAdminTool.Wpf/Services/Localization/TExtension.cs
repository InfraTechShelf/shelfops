using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace CitrixAdminTool.Wpf.Services.Localization
{
    /// <summary>
    /// XAMLから画面文言を参照するためのマークアップ拡張。
    ///
    /// 使い方: <c>Content="{loc:T Common_Refresh}"</c>
    ///
    /// 単なる文字列を返すのではなく <see cref="Loc"/> のインデクサへの
    /// <see cref="Binding"/> を返している。こうすることで、実行中に言語を切り替えたときも
    /// 画面を開き直さずに文言が入れ替わる。
    ///
    /// Source を明示しているため DataContext に依存しない。これは
    /// <c>DataGridColumn.Header</c> のように**ビジュアルツリーに属さない**要素でも
    /// バインドを効かせるために必要（DataContext の継承が届かないため）。
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public class TExtension : MarkupExtension
    {
        public TExtension() { }

        public TExtension(string key)
        {
            Key = key;
        }

        /// <summary>文言のキー。</summary>
        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding("[" + Key + "]")
            {
                Source = Loc.Instance,
                Mode = BindingMode.OneWay
            };

            return binding.ProvideValue(serviceProvider);
        }
    }
}
