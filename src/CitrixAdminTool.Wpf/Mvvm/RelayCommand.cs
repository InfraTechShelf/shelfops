using System;
using System.Windows.Input;

namespace CitrixAdminTool.Wpf.Mvvm
{
    /// <summary>
    /// デリゲートで実装するICommand。
    ///
    /// CommandManager.RequerySuggested には**乗せていない**。
    /// 接続テスト中はボタンを確実に無効化したいが、RequerySuggested の発火は
    /// 入力イベント任せで確実性がないため、<see cref="RaiseCanExecuteChanged"/> を
    /// ViewModel側から明示的に呼ぶ方式にしている。
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            if (execute == null) throw new ArgumentNullException(nameof(execute));

            _execute = execute;
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(_ => execute(), canExecute == null ? (Func<object, bool>)null : _ => canExecute())
        {
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            var handler = CanExecuteChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }
}
