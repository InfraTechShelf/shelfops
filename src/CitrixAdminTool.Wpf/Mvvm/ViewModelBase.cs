using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CitrixAdminTool.Wpf.Mvvm
{
    /// <summary>
    /// ViewModelの共通基底。外部のMVVMフレームワークは使わない
    /// （NuGetを持ち込まず単一フォルダ自己完結を守るため）。
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 値が変わったときだけフィールドを更新して通知する。
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            RaisePropertyChanged(propertyName);
            return true;
        }
    }
}
